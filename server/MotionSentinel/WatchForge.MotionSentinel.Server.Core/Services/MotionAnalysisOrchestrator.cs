using log4net;

namespace WatchForge.MotionSentinel.Server.Core.Services;

/// <summary>
/// Coordinates the full analysis pipeline: scan → analyse → write → cleanup.
/// Depends only on interfaces — no platform code, fully unit-testable.
/// </summary>
public sealed class MotionAnalysisOrchestrator
{
    private static readonly ILog Log =
        LogManager.GetLogger(typeof(MotionAnalysisOrchestrator));

    private readonly IFileAccessService _fileAccess;
    private readonly Func<string, IVideoSource> _videoSourceFactory;
    private readonly IMotionDetector _detector;
    private readonly DetectionJsonSerializer _serializer;
    private readonly IDateTimeProvider _clock;
    private readonly IAppInfoProvider _appInfo;

    public MotionAnalysisOrchestrator(
        IFileAccessService fileAccess,
        Func<string, IVideoSource> videoSourceFactory,
        IMotionDetector detector,
        DetectionJsonSerializer serializer,
        IDateTimeProvider clock,
        IAppInfoProvider appInfo)
    {
        _fileAccess         = fileAccess;
        _videoSourceFactory = videoSourceFactory;
        _detector           = detector;
        _serializer         = serializer;
        _clock              = clock;
        _appInfo            = appInfo;
    }

    /// <summary>
    /// Analyses one specific recording file.
    /// Called by MotionSentinelService when FileSystemWatcher detects a new MP4.
    /// </summary>
    public async Task RunForFileAsync(string fileName, CancellationToken ct)
    {
        if (_fileAccess.DetectionExists(fileName))
        {
            Log.Info($"Skipping (already analysed): {fileName}");
            return;
        }

        var localPath = _fileAccess.GetRecordingPath(fileName);

        Log.Info($"Starting: {fileName}");

        try
        {
            _detector.Reset();
            var result = await AnalyseAsync(fileName, localPath, ct);
            var json   = _serializer.Serialize(result);
            await _fileAccess.WriteDetectionAsync(json, fileName, ct);

            Log.Info($"Completed: {fileName} — {result.Events.Count} events");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Failed: {fileName}", ex);
            throw;
        }
    }

    /// <summary>
    /// Scans the recordings folder and processes any files not yet analysed.
    /// Called once at startup to catch files written while the service was down.
    /// </summary>
    public async Task RunBackfillAsync(CancellationToken ct)
    {
        var recordings = await _fileAccess.ListNewRecordingsAsync(ct);

        if (recordings.Count == 0)
        {
            Log.Info("Backfill: no unprocessed recordings found.");
            return;
        }

        Log.Info($"Backfill: {recordings.Count} unprocessed recording(s) found.");

        foreach (var fileName in recordings)
        {
            ct.ThrowIfCancellationRequested();
            await RunForFileAsync(fileName, ct);
        }
    }

    private async Task<DetectionResult> AnalyseAsync(
        string fileName, string localPath, CancellationToken ct)
    {
        using var source = _videoSourceFactory(localPath);
        var events       = new List<MotionEvent>();
        int frameCount   = 0;

        await foreach (var frame in source.GetFramesAsync(intervalMs: 500, ct))
        {
            using (frame)
            {
                var regions = await _detector.DetectAsync(frame, ct);
                frameCount++;

                if (regions.Count > 0)
                    events.Add(new MotionEvent
                    {
                        TimestampMs = frame.TimestampMs,
                        DurationMs  = 500,
                        Regions     = regions
                    });
            }
        }

        return new DetectionResult
        {
            VideoFile  = fileName,
            AnalyzedAt = _clock.UtcNow.ToString("O"),
            AppVersion = _appInfo.Version,
            Metadata   = new VideoMetadata
            {
                Width               = source.Width,
                Height              = source.Height,
                FrameRate           = source.FrameRate,
                TotalFramesAnalyzed = frameCount
            },
            Events = events
        };
    }
}
