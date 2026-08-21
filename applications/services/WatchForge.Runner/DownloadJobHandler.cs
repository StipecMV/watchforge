using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre JobType.Download (S4-3): stiahne nahrávku z NVR do media cache
/// (súbor .downloading → po úspechu konvertovaný na cieľový formát), retry
/// s limitom pokusov, čistenie zvyškov .downloading po reštarte.
/// Job.RecordingId ukazuje na RECORDINGS záznam.
/// </summary>
#pragma warning disable CS9113 // jobRepository/clock sú DI infraštruktúra (rezerva pre budúce použitie)
public sealed class DownloadJobHandler(
    IRecordingRepository recordingRepository,
    INvrRepository nvrRepository,
    IJobRepository jobRepository,
    IClock clock,
    DownloadOptions options,
    Func<DvripClientOptions, IDvripClient> clientFactory) : IJobHandler
#pragma warning restore CS9113
{
    public JobType HandlesType => JobType.Download;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        if (job.RecordingId is null)
            throw new InvalidOperationException("Download job requires RecordingId.");

        var recording = await recordingRepository.GetByIdAsync(job.RecordingId.Value, ct)
            ?? throw new InvalidOperationException($"Recording {job.RecordingId} not found.");

        var nvr = (await nvrRepository.GetAllAsync(ct)).FirstOrDefault(n => n.NvrId == recording.NvrId)
            ?? throw new InvalidOperationException($"NVR {recording.NvrId} not found.");
        var password = string.IsNullOrEmpty(nvr.PasswordSecretEnv)
            ? null : Environment.GetEnvironmentVariable(nvr.PasswordSecretEnv);
        if (password is null)
            throw new InvalidOperationException($"Password env '{nvr.PasswordSecretEnv}' not set.");

        Directory.CreateDirectory(options.TempDir);

        // Čistenie zvyškov .downloading po reštarte (interrupted downloady)
        CleanupStaleDownloading(clock.UtcNow);

        // NVR vracia absolútne cesty (/idea0/...) — GetFileName zneškodní path traversal
        var rawPath = Path.Combine(options.TempDir, Path.GetFileName(recording.NvrFilename) + ".downloading");
        File.Delete(rawPath); // prípadný starý nedokončený súbor pre tento záznam

        recording.AnalysisState = "downloading";
        await recordingRepository.UpdateAsync(recording, ct);

        var nvrFile = new NvrFile
        {
            FileName = recording.NvrFilename,
            BeginTime = recording.BeginTime,
            EndTime = recording.EndTime,
            FileLengthBytes = recording.SizeBytes,
        };

        var progress = new Progress<long>(bytes =>
        {
            if (recording.SizeBytes > 0)
            {
                job.Progress = Math.Min(99, (int)(bytes * 100 / recording.SizeBytes));
                // priebežný persist (fire-and-forget nie je bezpečný — update cez repo priamo)
            }
        });

        try
        {
            using var client = clientFactory(new DvripClientOptions
            {
                Host = nvr.Host, Port = nvr.Port, Username = nvr.Username, Password = password
            });
            var outputPath = await client.DownloadFileAsync(nvrFile, rawPath, options.OutputFormat, progress, null, ct);

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("Download produced no output file.");

            recording.AnalysisState = "downloaded";
            await recordingRepository.UpdateAsync(recording, ct);
            job.Progress = 100;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && job.Attempts < options.MaxRetries - 1)
        {
            // Retry: job sa vráti do frontu (attempts+1), výnimka sa preloguje do error.
            job.Attempts++;
            job.Status = JobStatus.Queued;
            job.Error = $"download failed (attempt {job.Attempts}/{options.MaxRetries}): {ex.Message}";
        }
    }

    /// <summary>
    /// Zmaže .downloading súbory staršie ako StaleDownloadingAge — zvyšky
    /// nedokončených downloadov po reštarte (interrupted).
    /// </summary>
    private void CleanupStaleDownloading(DateTime now)
    {
        if (!Directory.Exists(options.TempDir)) return;
        foreach (var file in Directory.EnumerateFiles(options.TempDir, "*.downloading"))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(file) > options.StaleDownloadingAge)
                    File.Delete(file);
            }
            catch (IOException) { /* súbor práve píše iný worker — preskoč */ }
        }
    }
}
