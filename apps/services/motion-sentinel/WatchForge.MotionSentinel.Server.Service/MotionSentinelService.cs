namespace WatchForge.MotionSentinel.Server.Service;

/// <summary>
/// Hosted service entry point. Implements IHostedLifecycleService (.NET 8+) for
/// granular startup/shutdown control:
///   StartingAsync  — log config, validate paths
///   StartAsync     — backfill (files written while service was down)
///   StartedAsync   — activate FileSystemWatcher
///   StoppingAsync  — disable watcher, drain in-flight analyses
///   StopAsync      — (default IHostedService stop)
///   StoppedAsync   — final cleanup log
/// </summary>
public sealed class MotionSentinelService : IHostedLifecycleService
{
    private static readonly ILog Log =
        LogManager.GetLogger(typeof(MotionSentinelService));

    private readonly MotionAnalysisOrchestrator _orchestrator;
    private readonly LocalFileServiceOptions _fileOptions;

    // Serialises concurrent watcher events — only one analysis runs at a time
    private readonly SemaphoreSlim _analysisLock = new(1, 1);

    // Tracks filenames currently queued or running to prevent duplicate analysis
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource _cts = new();

    public MotionSentinelService(
        MotionAnalysisOrchestrator orchestrator,
        IOptions<LocalFileServiceOptions> fileOptions)
    {
        _orchestrator = orchestrator;
        _fileOptions  = fileOptions.Value;
    }

    /// <summary>Called before <see cref="StartAsync"/> — validates paths and logs the resolved configuration.</summary>
    public Task StartingAsync(CancellationToken ct)
    {
        Log.Info("MotionSentinel starting.");
        Log.Info($"Watch      : {_fileOptions.WatchDirectory}");
        Log.Info($"Output     : {_fileOptions.OutputDirectory}");
        Log.Info($"Extensions : {string.Join(", ", _fileOptions.FileExtensions)}");

        if (!Directory.Exists(_fileOptions.WatchDirectory))
            throw new DirectoryNotFoundException(
                $"WatchDirectory not found: {_fileOptions.WatchDirectory}");

        return Task.CompletedTask;
    }

    /// <summary>Called after <see cref="StartingAsync"/> — runs a backfill pass for files written while the service was down.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _orchestrator.RunBackfillAsync(_cts.Token);
    }

    /// <summary>Called after <see cref="StartAsync"/> — activates one <see cref="FileSystemWatcher"/> per configured extension to process new recordings in real time.</summary>
    public Task StartedAsync(CancellationToken ct)
    {
        foreach (var ext in _fileOptions.FileExtensions)
        {
            var watcher = new FileSystemWatcher(_fileOptions.WatchDirectory, ext)
            {
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents   = true,
            };

            watcher.Created += OnFileCreated;
            watcher.Renamed += OnFileRenamed;
            _watchers.Add(watcher);
        }

        Log.Info($"FileSystemWatcher active ({_watchers.Count} filter(s)) — waiting for new recordings...");
        return Task.CompletedTask;
    }

    /// <summary>Called before <see cref="StopAsync"/> — disables the watcher and waits for any in-flight analysis to finish.</summary>
    public async Task StoppingAsync(CancellationToken ct)
    {
        Log.Info("MotionSentinel stopping — draining in-flight analysis...");

        foreach (var watcher in _watchers)
            watcher.EnableRaisingEvents = false;

        await _cts.CancelAsync();

        // Acquire lock — blocks until any running analysis finishes
        await _analysisLock.WaitAsync(ct);
        _analysisLock.Release();
    }

    /// <summary>No-op — lifecycle is fully handled by the granular methods above.</summary>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Called after <see cref="StopAsync"/> — writes the final log entry and disposes the watcher.</summary>
    public Task StoppedAsync(CancellationToken ct)
    {
        Log.Info("MotionSentinel stopped.");
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        return Task.CompletedTask;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
        => EnqueueAnalysis(e.Name, e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e)
        => EnqueueAnalysis(e.Name, e.FullPath);

    private void EnqueueAnalysis(string? fileName, string fullPath)
    {
        if (string.IsNullOrEmpty(fileName)) return;

        // Skip if already queued or running for this file
        if (!_inFlight.TryAdd(fileName, 0))
        {
            Log.Debug($"Already in-flight, skipping duplicate event: {fileName}");
            return;
        }

        _ = Task.Run(async () =>
        {
            // Acquire lock — bail out if the service is stopping before we get in
            try
            {
                await _analysisLock.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // Semaphore not acquired; nothing to release
            }

            try
            {
                // Wait briefly — NVR may still be writing the file
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);

                if (!File.Exists(fullPath))
                {
                    Log.Warn($"File disappeared before analysis: {fileName}");
                    return;
                }

                await _orchestrator.RunForFileAsync(fileName, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Info($"Analysis cancelled for: {fileName}");
            }
            catch (Exception ex)
            {
                Log.Error($"Unhandled error analysing {fileName}", ex);
            }
            finally
            {
                _inFlight.TryRemove(fileName, out _);
                _analysisLock.Release();
            }
        });
    }
}
