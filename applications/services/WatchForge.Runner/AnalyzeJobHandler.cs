using System.Text.Json;
using OpenCvSharp;
using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.VideoSources;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre JobType.Analyze (S4-4): motion analysis pipeline.
/// - CPU motion detekcia (Farneback optical flow) na 1080p downscale (z 4K),
/// - regióny normalizované 0..1 — platia priamo pre 4K fram („regióny → 4K"),
/// - voliteľná person/object detekcia (S8-2, klasická CV) — spúšťa sa LEN
///   na framoch, kde motion našiel pohyb (pipeline reťazenie, FR-04),
/// - zápis detekcií do DETECTIONS (batch insert),
/// - lokálne video sa po analýze ZMAŽE (NVR je archivár originálov),
/// - ak job patrí requestu, po analýze sa enqueue ClipExtract job (S5-8).
/// Video sa stiahne z NVR, ak ešte nie je v media cache (self-contained).
/// </summary>
public sealed class AnalyzeJobHandler(
    IRecordingRepository recordingRepository,
    IDetectionRepository detectionRepository,
    INvrRepository nvrRepository,
    IJobRepository jobRepository,
    IClock clock,
    DownloadOptions downloadOptions,
    DetectionOptions detectionOptions,
    Func<DvripClientOptions, IDvripClient> clientFactory,
    IObjectDetector? objectDetector = null,
    IFaceRecognizer? faceRecognizer = null) : IJobHandler
{
    /// <summary>
    /// S22m: maximálna šírka framu pre analýzu — 1920 (1080p).
    /// PÔVODNÉ rozhodnutie (architektúra): 1080p = 4× zmenšenie z 4K (720p = 9× bolo
    /// zamietnuté ako nepresné). Pri analyze optimalizácii (S22g) to prepadlo na 1280
    /// (720p) — vrátené späť na 1080p. Metadáta sa mapujú na 4K súradnice (FR-03).
    /// </summary>
    public const int AnalysisMaxWidth = 1920;

    /// <summary>Vzorkovanie framov (1 fps) — dostatočné pre pohyb, šetrí CPU.</summary>
    public const int FrameIntervalMs = 1000;

    public const string AlgorithmVersion = "optical-flow-1";

    /// <summary>Verzia person detekcie (S8-2, HOG+SVM klasická CV).</summary>
    public const string PersonAlgorithmVersion = "hog-person-1";

    /// <summary>Verzia face rozpoznania (S8-3, LBP cascade + LBPH klasická CV).</summary>
    public const string FaceAlgorithmVersion = "lbph-face-1";

    public JobType HandlesType => JobType.Analyze;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        if (job.RecordingId is null)
            throw new InvalidOperationException("Analyze job requires RecordingId.");

        var recording = await recordingRepository.GetByIdAsync(job.RecordingId.Value, ct)
            ?? throw new InvalidOperationException($"Recording {job.RecordingId} not found.");

        // S22n: okno z payload (windowStart/windowEnd) — analýza LEN 15-min okna,
        // nie celého NVR segmentu (segmenty sú variabilné 5–60+ min). Ak job nemá okno
        // (requesty na požiadanie), analyzuje sa celý segment (bez trimu).
        var (windowStart, windowEnd) = ParseWindow(job.Payload);
        var trimOffset = TimeSpan.Zero;
        if (windowStart is not null && windowStart > recording.BeginTime)
            trimOffset = windowStart.Value - recording.BeginTime;
        var offsetMs = (long)trimOffset.TotalMilliseconds;

        // S22n: okno sa uloží na recording — UI ho potrebuje na sync udalostí
        // s prehrávaním trimnutého videa (player 0 = windowStart, nie begin_time)
        recording.WindowStartUtc = windowStart ?? recording.BeginTime;

        var videoPath = await EnsureVideoAsync(recording, trimOffset, ct);

        recording.AnalysisState = "analyzing";
        await recordingRepository.UpdateAsync(recording, ct);

        using var source = new FileVideoSource(videoPath, maxWidth: AnalysisMaxWidth);
        // S22o/POC: motion detektor — aktuálne Farneback optical flow (optical-flow-1).
        // Alternatíva MOG2Detector (background subtraction) bola implementovaná a
        // otestovaná (~15–40× rýchlejšia) — prepnutie po A/B validácii na reálnych
        // segmentoch; Farneback ostať ako fallback/citlivejšia konfigurácia.
        using var detector = new OpticalFlowDetector
        {
            IntensityThreshold = detectionOptions.IntensityThreshold,
            MinContourArea     = detectionOptions.MinContourArea,
        };
        detector.Reset();

        var events = new List<(long TimestampMs, IReadOnlyList<MotionRegion> Regions)>();
        var personDetections = new List<(long TimestampMs, IReadOnlyList<ObjectDetection> Objects)>();
        // S10-3: face detekcia + 4K crop z ORIGINÁLNEHO framu (nie 1080p downscale)
        var faceDetections = new List<(long TimestampMs, IReadOnlyList<FaceDetection> Faces, List<Mat> Crops)>();
        using var originalSource = faceRecognizer is not null
            ? new FileVideoSource(videoPath) // bez maxWidth → originálne rozlíšenie (4K)
            : null;

        await foreach (var frame in source.GetFramesAsync(FrameIntervalMs, ct))
        {
            using (frame)
            {
                var regions = await detector.DetectAsync(frame, ct);
                if (regions.Count > 0)
                {
                    events.Add((frame.TimestampMs, regions));

                    // FR-04: person/object detekcia sa spúšťa LEN pri nájdenom pohybe
                    if (objectDetector is not null)
                    {
                        var objects = await objectDetector.DetectAsync(frame, ct);
                        if (objects.Count > 0)
                        {
                            personDetections.Add((frame.TimestampMs, objects));

                            // FR-05: face rozpoznanie sa spúšťa LEN keď person identifikoval osobu
                            if (faceRecognizer is not null)
                            {
                                var faces = await faceRecognizer.DetectFacesAsync(frame, ct);
                                if (faces.Count > 0)
                                {
                                    // S10-3: 4K crop z originálneho framu (vysoké rozlíšenie pre embedding)
                                    var crops = new List<Mat>(faces.Count);
                                    if (originalSource is not null)
                                    {
                                        using var original = originalSource.ExtractFrameAt(frame.TimestampMs);
                                        if (original is not null)
                                        {
                                            foreach (var face in faces)
                                            {
                                                var x = (int)Math.Clamp(face.Region.X * original.Width, 0, original.Width - 1);
                                                var y = (int)Math.Clamp(face.Region.Y * original.Height, 0, original.Height - 1);
                                                var w = (int)Math.Clamp(face.Region.W * original.Width, 1, original.Width - x);
                                                var h = (int)Math.Clamp(face.Region.H * original.Height, 1, original.Height - y);
                                                crops.Add(new Mat(original, new Rect(x, y, w, h)));
                                            }
                                        }
                                    }
                                    faceDetections.Add((frame.TimestampMs, faces, crops));
                                }
                            }
                        }
                    }
                }
            }
        }

        var detections = new List<Detection>();
        foreach (var (timestampMs, regions) in events)
        {
            foreach (var region in regions)
            {
                detections.Add(new Detection
                {
                    RecordingId      = recording.RecordingId,
                    CameraId         = recording.CameraId,
                    DetectionType    = "motion",
                    TimestampMs      = (int)(timestampMs + offsetMs), // S22n: + trim offset (čas v segmente)
                    DurationMs       = FrameIntervalMs,
                    Confidence       = 0f,
                    AlgorithmVersion = AlgorithmVersion,
                    Region           = new NormalizedRegion(region.X, region.Y, region.Width, region.Height),
                    Intensity        = region.Intensity,
                });
            }
        }

        // S8-2: person/object detekcie — rovnaký batch insert, vlastný typ a trieda
        foreach (var (timestampMs, objects) in personDetections)
        {
            foreach (var obj in objects)
            {
                detections.Add(new Detection
                {
                    RecordingId      = recording.RecordingId,
                    CameraId         = recording.CameraId,
                    DetectionType    = "person",
                    TimestampMs      = (int)(timestampMs + offsetMs), // S22n: + trim offset
                    DurationMs       = FrameIntervalMs,
                    Confidence       = obj.Confidence,
                    AlgorithmVersion = PersonAlgorithmVersion,
                    Region           = obj.Region,
                    ObjectClass      = obj.ObjectClass,
                });
            }
        }

        // S8-3: face detekcie — typ "face", ObjectClass = identity meno (prázdne = neznáma)
        var faceDetectionsInList = new List<(Detection Detection, Mat? Crop, int? IdentityId)>();
        foreach (var (timestampMs, faces, crops) in faceDetections)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var detection = new Detection
                {
                    RecordingId      = recording.RecordingId,
                    CameraId         = recording.CameraId,
                    DetectionType    = "face",
                    TimestampMs      = (int)(timestampMs + offsetMs), // S22n: + trim offset
                    DurationMs       = FrameIntervalMs,
                    Confidence       = face.Confidence,
                    AlgorithmVersion = FaceAlgorithmVersion,
                    Region           = face.Region,
                    ObjectClass      = face.IdentityName, // identity meno alebo "" (neznáma)
                };
                detections.Add(detection);
                faceDetectionsInList.Add((detection, crops.Count > i ? crops[i] : null, face.IdentityId));
            }
        }

        if (detections.Count > 0)
            await detectionRepository.InsertManyAsync(detections, ct);

        // S10-2/3: perzistencia face embeddings (crop = embedding do FACES) + learn model
        if (faceRecognizer is not null)
        {
            foreach (var (detection, crop, identityId) in faceDetectionsInList)
            {
                if (crop is null) continue;
                try
                {
                    await faceRecognizer.LearnAsync(crop, identityId, detection.ObjectClass, detection.DetectionId, ct);
                }
                catch (Exception ex)
                {
                    // Neúspešné learn nesmie zabiť celý analyze job (log sa rieši vyššie)
                    Console.Error.WriteLine($"face learn failed: {ex.Message}");
                }
                finally
                {
                    crop.Dispose();
                }
            }
        }

        // S22l: ak sa našla OSOBA, nahrávka je chránená pred purge (skip pre cleanup),
        // kým užívateľ neoznačí „skontrolované" (UI/MCP). Detekcia osoby = personDetections.
        recording.PersonPending = personDetections.Count > 0;
        recording.AnalysisState = "completed";
        recording.AnalysisCompletedAt = clock.UtcNow;
        await recordingRepository.UpdateAsync(recording, ct);

        // S22l: lokálne video sa NEmazá hneď — drží sa pre rolling okná (60 min),
        // purge job maže staršie okná + .downloading zvyšky. (NVR je archivár originálov,
        // lokálna cache je dočasná na prehrávanie/analýzu.)

        // Ak job patrí interaktívnej požiadavke → enqueue ClipExtract (S5-8)
        if (job.RequestId is not null)
        {
            await jobRepository.EnqueueAsync(new Job
            {
                RecordingId = job.RecordingId,
                RequestId = job.RequestId,
                Type = JobType.ClipExtract,
                Priority = job.Priority,
                Source = job.Source,
                Status = JobStatus.Queued,
            }, ct);
        }

        job.Progress = 100;
        job.Error = $"events={events.Count}, detections={detections.Count}";
    }

    /// <summary>Nájde stiahnutý súbor v media cache podľa názvu nahrávky (ľubovoľná prípona).</summary>
    private string? FindDownloadedVideo(Recording recording)
    {
        if (!Directory.Exists(downloadOptions.TempDir)) return null;
        var baseName = Path.GetFileNameWithoutExtension(recording.NvrFilename);
        // IBA konvertované súbory (.mp4/.mkv) — .downloading je raw HEVC,
        // ktorý ffmpeg/dekódovanie nevie načítať. Stale raw sa vyčistí.
        foreach (var stale in Directory.GetFiles(downloadOptions.TempDir, baseName + ".downloading"))
        {
            try { File.Delete(stale); } catch { /* zamknutý — necháme */ }
        }
        return Directory.GetFiles(downloadOptions.TempDir, baseName + ".mp4").FirstOrDefault()
            ?? Directory.GetFiles(downloadOptions.TempDir, baseName + ".mkv").FirstOrDefault();
    }

    /// <summary>Video z media cache; ak chýba, stiahne ho z NVR (self-contained Analyze job).
    /// S22n: <paramref name="trimOffset"/> — oreže segment na 15-min okno (offset od začiatku).</summary>
    private async Task<string> EnsureVideoAsync(Recording recording, TimeSpan trimOffset, CancellationToken ct)
    {
        var existing = FindDownloadedVideo(recording);
        if (existing is not null) return existing;

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
        }, rawPath, "mp4", null, trimOffset == TimeSpan.Zero ? null : trimOffset, ct);
    }

    /// <summary>
    /// S22n: načíta 15-min okno z job payload (windowStart/windowEnd — AutoPlanner).
    /// null = žiadne okno (request na požiadanie → celý segment, bez trimu).
    /// </summary>
    private static (DateTime? Start, DateTime? End) ParseWindow(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("windowStart", out var s) && root.TryGetProperty("windowEnd", out var e)
                && s.TryGetDateTime(out var start) && e.TryGetDateTime(out var end) && end > start)
            {
                return (start, end);
            }
        }
        catch (JsonException) { /* fallback: bez okna */ }
        return (null, null);
    }
}
