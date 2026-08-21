using System.Diagnostics;
using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// S20: Handler pre JobType.ExportRange — export vybranej časovej úsečky
/// (dôkazový klip). Na rozdiel od ClipExtract (klipy okolo detekcií) vystrihne
/// PRESNÝ interval [request.FromTime, request.ToTime] zo záznamu a uloží ho
/// ako MP4 klip (H.264 + faststart, rovnaký ffmpeg mechanizmus).
/// Request sa po spracovaní označí completed; klip je stiahnuteľný cez
/// GET /api/v1/clips/{id} (ClipsController).
/// </summary>
public sealed class ExportRangeJobHandler(
    IRecordingRepository recordingRepository,
    IClipRepository clipRepository,
    IRequestRepository requestRepository,
    INvrRepository nvrRepository,
    IClock clock,
    DownloadOptions downloadOptions,
    ClipOptions clipOptions,
    Func<DvripClientOptions, IDvripClient> clientFactory) : IJobHandler
{
    public JobType HandlesType => JobType.ExportRange;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        if (job.RecordingId is null)
            throw new InvalidOperationException("ExportRange job requires RecordingId.");
        if (job.RequestId is not int requestId)
            throw new InvalidOperationException("ExportRange job requires RequestId.");

        var recording = await recordingRepository.GetByIdAsync(job.RecordingId.Value, ct)
            ?? throw new InvalidOperationException($"Recording {job.RecordingId} not found.");
        var request = await requestRepository.GetByIdAsync(requestId, ct)
            ?? throw new InvalidOperationException($"Request {requestId} not found.");

        if (request.ToTime <= request.FromTime)
            throw new InvalidOperationException("Request range is invalid (toTime <= fromTime).");

        var videoPath = await EnsureVideoAsync(recording, ct);
        Directory.CreateDirectory(clipOptions.ClipsDir);

        // Prekryv intervalu so záznamom
        var totalDurationMs = (long)(recording.EndTime - recording.BeginTime).TotalMilliseconds;
        var recStart = recording.BeginTime;
        var fromMs = Math.Max(0, (long)(request.FromTime - recStart).TotalMilliseconds);
        var toMs = Math.Min(totalDurationMs, (long)(request.ToTime - recStart).TotalMilliseconds);
        var lengthMs = toMs - fromMs;

        if (lengthMs < 500)
        {
            await CompleteRequestAsync(request, ct);
            job.Progress = 100;
            job.Error = "range outside recording (length < 500 ms)";
            return;
        }

        var clipPath = Path.Combine(clipOptions.ClipsDir,
            $"export_{recording.RecordingId}_{requestId}.mp4");
        await CutClipAsync(videoPath, clipPath, fromMs / 1000.0, lengthMs / 1000.0, ct);

        await clipRepository.InsertAsync(new Clip
        {
            RequestId = requestId,
            RecordingId = recording.RecordingId,
            RangeStart = request.FromTime,
            RangeEnd = request.ToTime,
            FilePath = clipPath,
            SizeBytes = new FileInfo(clipPath).Length,
            Kind = "video",
            ExpiresAt = clock.UtcNow + clipOptions.ClipExpiry,
        }, ct);

        await CompleteRequestAsync(request, ct);
        job.Progress = 100;
        job.Error = $"export {lengthMs / 1000.0:F1}s -> {clipPath}";
    }

    private async Task CompleteRequestAsync(Request request, CancellationToken ct)
    {
        request.Status = "completed";
        request.CompletedAt = clock.UtcNow;
        await requestRepository.UpdateAsync(request, ct);
    }

    /// <summary>Nájde stiahnuté video v media cache; ak chýba, stiahne ho z NVR (rovnaké ako ClipExtract).</summary>
    private async Task<string> EnsureVideoAsync(Recording recording, CancellationToken ct)
    {
        var baseName = Path.GetFileNameWithoutExtension(recording.NvrFilename);
        if (Directory.Exists(downloadOptions.TempDir))
        {
            // IBA konvertované súbory (.mp4/.mkv) — .downloading je raw HEVC,
            // ktorý ffmpeg cut nevie načítať (exit 183). Stale raw sa vyčistí.
            foreach (var stale in Directory.GetFiles(downloadOptions.TempDir, baseName + ".downloading"))
            {
                try { File.Delete(stale); } catch { /* zamknutý — necháme */ }
            }
            var existing = Directory.GetFiles(downloadOptions.TempDir, baseName + ".mp4").FirstOrDefault()
                ?? Directory.GetFiles(downloadOptions.TempDir, baseName + ".mkv").FirstOrDefault();
            if (existing is not null) return existing;
        }

        var nvr = (await nvrRepository.GetAllAsync(ct)).FirstOrDefault(n => n.NvrId == recording.NvrId)
            ?? throw new InvalidOperationException($"NVR {recording.NvrId} not found.");
        var password = string.IsNullOrEmpty(nvr.PasswordSecretEnv)
            ? null : Environment.GetEnvironmentVariable(nvr.PasswordSecretEnv);
        if (password is null)
            throw new InvalidOperationException($"Password env '{nvr.PasswordSecretEnv}' not set.");

        Directory.CreateDirectory(downloadOptions.TempDir);
        // NVR vracia absolútne cesty (/idea0/...) — GetFileName zneškodní path traversal
        var rawPath = Path.Combine(downloadOptions.TempDir, Path.GetFileName(recording.NvrFilename) + ".downloading");
        using var client = clientFactory(new DvripClientOptions
        {
            Host = nvr.Host, Port = nvr.Port, Username = nvr.Username, Password = password
        });
        return await client.DownloadFileAsync(new NvrFile
        {
            FileName = recording.NvrFilename,
            BeginTime = recording.BeginTime,
            EndTime = recording.EndTime,
            FileLengthBytes = recording.SizeBytes,
        }, rawPath, "mp4", null, null, ct);
    }

    /// <summary>ffmpeg cut: výsek [startSec, startSec+lengthSec] → H.264 MP4 (S5-5 mechanizmus).</summary>
    private static async Task CutClipAsync(string inputPath, string outputPath, double startSec, double lengthSec, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(startSec.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(lengthSec.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset"); psi.ArgumentList.Add("fast");
        psi.ArgumentList.Add("-crf"); psi.ArgumentList.Add("23");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(outputPath);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for clip cut.");
        // stderr musí byť čítaný — inak sa pipe zaplní a ffmpeg zablokuje (produkčný objav)
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await stderrTask;
        if (proc.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"ffmpeg clip cut failed (exit {proc.ExitCode}).");
    }
}
