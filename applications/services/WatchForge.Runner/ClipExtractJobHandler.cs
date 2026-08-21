using System.Diagnostics;
using OpenCvSharp;
using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre JobType.ClipExtract (S5-5): generuje z analyzovaného záznamu
/// krátke klipy okolo detekcií (event-first) + fotku s detekčným obdĺžnikom.
/// - video: ffmpeg cut (±context okolo detekcie), H.264 MP4,
/// - photo: frame v čase detekcie + OpenCV nakreslí región (normalizovaný → pixely),
/// - oba záznamy do CLIPS s expiráciou (ClipExpiry, purge job ich zmaže).
/// Request sa po spracovaní označí completed.
/// </summary>
public sealed class ClipExtractJobHandler(
    IRecordingRepository recordingRepository,
    IDetectionRepository detectionRepository,
    IClipRepository clipRepository,
    IRequestRepository requestRepository,
    INvrRepository nvrRepository,
    IClock clock,
    DownloadOptions downloadOptions,
    ClipOptions clipOptions,
    Func<DvripClientOptions, IDvripClient> clientFactory) : IJobHandler
{
    public JobType HandlesType => JobType.ClipExtract;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        if (job.RecordingId is null)
            throw new InvalidOperationException("ClipExtract job requires RecordingId.");

        var recording = await recordingRepository.GetByIdAsync(job.RecordingId.Value, ct)
            ?? throw new InvalidOperationException($"Recording {job.RecordingId} not found.");
        var request = job.RequestId is int rid ? await requestRepository.GetByIdAsync(rid, ct) : null;

        var contextBefore = request?.ContextBeforeSec ?? clipOptions.DefaultContextBeforeSec;
        var contextAfter = request?.ContextAfterSec ?? clipOptions.DefaultContextAfterSec;
        var detectionType = request?.DetectionTypeFilter ?? "motion";

        var detections = (await detectionRepository.GetByRecordingAsync(recording.RecordingId, ct))
            .Where(d => d.DetectionType == detectionType)
            .Take(clipOptions.MaxClipsPerRequest)
            .ToList();

        // Žiadne detekcie — request je hotový bez klipov (0 klipov je platný výsledok)
        if (detections.Count == 0)
        {
            await CompleteRequestAsync(request, ct);
            job.Progress = 100;
            job.Error = "clips=0 (no detections)";
            return;
        }

        var videoPath = await EnsureVideoAsync(recording, ct);
        Directory.CreateDirectory(clipOptions.ClipsDir);

        var totalDurationMs = (long)(recording.EndTime - recording.BeginTime).TotalMilliseconds;
        int created = 0;

        foreach (var detection in detections)
        {
            var (startMs, lengthMs) = ComputeRange(detection, totalDurationMs, contextBefore, contextAfter);
            if (lengthMs < 500) continue; // príliš krátky klip

            var clipPath = Path.Combine(clipOptions.ClipsDir,
                $"clip_{recording.RecordingId}_{detection.DetectionId}.mp4");
            await CutClipAsync(videoPath, clipPath, startMs / 1000.0, lengthMs / 1000.0, ct);

            var rangeStart = recording.BeginTime.AddMilliseconds(startMs);
            var rangeEnd = recording.BeginTime.AddMilliseconds(startMs + lengthMs);
            var expiresAt = clock.UtcNow + clipOptions.ClipExpiry;

            await clipRepository.InsertAsync(new Clip
            {
                RequestId = job.RequestId ?? 0,
                RecordingId = recording.RecordingId,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                FilePath = clipPath,
                SizeBytes = new FileInfo(clipPath).Length,
                Kind = "video",
                ExpiresAt = expiresAt,
            }, ct);
            created++;

            var photoPath = Path.Combine(clipOptions.ClipsDir,
                $"photo_{recording.RecordingId}_{detection.DetectionId}.jpg");
            await RenderPhotoAsync(videoPath, photoPath, detection, ct);
            await clipRepository.InsertAsync(new Clip
            {
                RequestId = job.RequestId ?? 0,
                RecordingId = recording.RecordingId,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                FilePath = photoPath,
                SizeBytes = new FileInfo(photoPath).Length,
                Kind = "photo",
                ExpiresAt = expiresAt,
            }, ct);
            created++;
        }

        await CompleteRequestAsync(request, ct);
        job.Progress = 100;
        job.Error = $"clips={created}";
    }

    private async Task CompleteRequestAsync(Request? request, CancellationToken ct)
    {
        if (request is null) return;
        request.Status = "completed";
        request.CompletedAt = clock.UtcNow;
        await requestRepository.UpdateAsync(request, ct);
    }

    /// <summary>Časový rozsah okolo detekcie (ms): timestamp ± kontext, orezané na dĺžku záznamu.</summary>
    public static (long StartMs, long LengthMs) ComputeRange(
        Detection detection, long totalDurationMs, int contextBeforeSec, int contextAfterSec)
    {
        var start = Math.Max(0, detection.TimestampMs - contextBeforeSec * 1000L);
        var end = Math.Min(totalDurationMs, detection.TimestampMs + detection.DurationMs + contextAfterSec * 1000L);
        return (start, Math.Max(0, end - start));
    }

    /// <summary>Nájde stiahnuté video v media cache; ak chýba, stiahne ho z NVR.</summary>
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

    /// <summary>ffmpeg cut: výsek [startSec, startSec+lengthSec] → H.264 MP4.</summary>
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

    /// <summary>Fotka v čase detekcie + detekčný obdĺžnik (región normalizovaný → pixely).</summary>
    private static async Task RenderPhotoAsync(string inputPath, string outputPath, Detection detection, CancellationToken ct)
    {
        var framePath = Path.ChangeExtension(outputPath, ".frame.jpg");
        try
        {
            // Extrahovať frame v čase detekcie
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(
                (detection.TimestampMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inputPath);
            psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("2");
            psi.ArgumentList.Add(framePath);
            using (var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg."))
            {
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                await stderrTask;
                if (proc.ExitCode != 0 || !File.Exists(framePath))
                    throw new InvalidOperationException($"ffmpeg frame extract failed (exit {proc.ExitCode}).");
            }

            // OpenCV: nakresliť obdĺžnik (región 0..1 → pixely framu) a uložiť
            using var frame = Cv2.ImRead(framePath, ImreadModes.Color);
            if (frame.Empty())
                throw new InvalidOperationException("Cannot decode extracted frame.");

            var x = (int)(detection.Region.X * frame.Width);
            var y = (int)(detection.Region.Y * frame.Height);
            var w = (int)(detection.Region.W * frame.Width);
            var h = (int)(detection.Region.H * frame.Height);
            Cv2.Rectangle(frame, new Rect(x, y, w, h), new Scalar(0, 255, 0), thickness: 3);

            Cv2.ImWrite(outputPath, frame);
        }
        finally
        {
            if (File.Exists(framePath)) File.Delete(framePath);
        }
    }
}
