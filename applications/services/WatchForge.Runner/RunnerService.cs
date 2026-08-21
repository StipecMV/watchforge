using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Worker loop (S4-1): polluje JOBS frontu, claimuje joby podľa typu do
/// typovo špecifických slotov (semafory) a dispatchuje na handlery.
///
/// Sloty:
/// - Analyze/ClipExtract → semafor MaxParallelAnalyses (CPU/GPU limit)
/// - Download           → semafor MaxParallelDownloads
/// - Ostatné typy (Sync, Purge) → jeden slot (sekvenčne, nie sú CPU náročné)
///
/// Priorita je riadená ClaimNextByTypeAsync (ORDER BY priority DESC).
/// </summary>
public sealed class RunnerService : BackgroundService
{
    private readonly IJobRepository _repository;
    private readonly IRecordingRepository _recordings;
    private readonly IJobExecutor _executor;
    private readonly RunnerOptions _options;
    private readonly ILogger<RunnerService> _logger;
    private readonly WatchForge.MotionSentinel.Library.Detection.IFaceRecognizer? _faceRecognizer;
    private readonly IFaceRepository? _faceRepository;
    private readonly IIdentityRepository? _identityRepository;
    private readonly NvrDiscovery? _nvrDiscovery;

    private SemaphoreSlim _analysisSemaphore = null!;
    private SemaphoreSlim _downloadSemaphore = null!;
    // S22e: vlastný slot pre Sync/Purge — bežia NEZÁVISLE od analyze slotov.
    // (Predtým sa claimovali len keď boli VŠETKY analysis sloty voľné, čo pri
    // neustálom analyze backlogu znamenalo, že CheckAvailability (unavailable/
    // purge_at) NIKDY nebežal a DB rástla o záznamy, ktoré NVR už nemá.)
    private SemaphoreSlim _maintenanceSemaphore = new(1, 1);

    // S20 (FR-08): bežiace joby s per-job CancellationTokenSource — umožňuje
    // preempciu (prerušenie najnižšie-prioritného background jobu prioritným).
    private readonly Dictionary<long, RunningJobContext> _running = new();
    private readonly object _runningLock = new();

    private sealed record RunningJobContext(Job Job, CancellationTokenSource Cts);

    public RunnerService(
        IJobRepository repository,
        IRecordingRepository recordings,
        IJobExecutor executor,
        RunnerOptions options,
        ILogger<RunnerService> logger,
        WatchForge.MotionSentinel.Library.Detection.IFaceRecognizer? faceRecognizer = null,
        IFaceRepository? faceRepository = null,
        IIdentityRepository? identityRepository = null,
        NvrDiscovery? nvrDiscovery = null)
    {
        _repository = repository;
        _recordings = recordings;
        _executor = executor;
        _options = options;
        _logger = logger;
        _faceRecognizer = faceRecognizer;
        _faceRepository = faceRepository;
        _identityRepository = identityRepository;
        _nvrDiscovery = nvrDiscovery;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    /// <summary>Worker loop (verejná pre testy).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _analysisSemaphore = new SemaphoreSlim(_options.MaxParallelAnalyses);
        _downloadSemaphore = new SemaphoreSlim(_options.MaxParallelDownloads);
        var pollDelay = TimeSpan.FromMilliseconds(_options.PollIntervalMs);

        // Recovery po reštarte (S4-7): running → interrupted → queued (preplánovanie)
        await RecoverInterruptedJobsAsync(stoppingToken);

        // S13-1: NVR discovery (DHCP) — ak host nedosiahnuteľný, sken LAN + login, auto-update DB
        if (_nvrDiscovery is not null)
        {
            try
            {
                var (found, newHost) = await _nvrDiscovery.EnsureHostAsync(stoppingToken);
                if (found && newHost is not null)
                {
                    _logger.LogInformation("NVR discovery: NVR nájdený na {Host}", newHost);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NVR discovery failed — pokračujem s nakonfigurovaným hostom");
            }
        }

        // S10-2: načítanie face embeddings z DB pri štarte (perzistencia modelu)
        if (_faceRecognizer is not null && _faceRepository is not null)
        {
            try
            {
                await _faceRecognizer.LoadStateAsync(_faceRepository, _identityRepository, stoppingToken);
                _logger.LogInformation("Face recognizer state loaded from DB");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Face recognizer state load failed — continuing with empty model");
            }
        }

        _logger.LogInformation("Runner started (analyses={Analyses}, downloads={Downloads}, poll={Poll}ms)",
            _options.MaxParallelAnalyses, _options.MaxParallelDownloads, _options.PollIntervalMs);

        // Auto-plánovač (S22-1): periodicky enqueue Sync (nové nahrávky z NVR) +
        // Analyze pre záznamy čakajúce na analýzu. Pozadie: video sa analyzuje
        // automaticky pre VŠETKY dostupné nahrávky; klip/video sa sťahuje až na
        // trigger (interaktívny request). Beží nezávisle od worker loopu.
        using var autoPlanner = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var plannerTask = Task.Run(() => AutoPlannerLoopAsync(autoPlanner, stoppingToken), CancellationToken.None);

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedAny = false;

            // Analyze sloty — claimni najvyššiu prioritu Analyze/ClipExtract jobu
            // (ClipExtract je výpočtovo náročný — ffmpeg cut + OpenCV render, beží v analysis slotoch;
            //  ExportRange (S20) — ffmpeg cut dôkazového klipu, tiež analysis slot)
            if (_analysisSemaphore.CurrentCount > 0)
            {
                var job = await _repository.ClaimNextByTypeAsync(JobType.Analyze, ct: stoppingToken)
                          ?? await _repository.ClaimNextByTypeAsync(JobType.ClipExtract, ct: stoppingToken)
                          ?? await _repository.ClaimNextByTypeAsync(JobType.ExportRange, ct: stoppingToken);
                if (job is not null)
                {
                    claimedAny = true;
                    _ = RunInSlotAsync(job, _analysisSemaphore, stoppingToken);
                }
            }
            else
            {
                // S20 (FR-08): analysis sloty plné — ak čaká job s vyššou prioritou
                // ako najnižšie-prioritný bežiaci background job, preruší ho
                // (preempcia) a uvoľní slot. Prerušený job sa requeue.
                var pending = await _repository.PeekNextByTypeAsync(JobType.Analyze, stoppingToken)
                              ?? await _repository.PeekNextByTypeAsync(JobType.ClipExtract, stoppingToken)
                              ?? await _repository.PeekNextByTypeAsync(JobType.ExportRange, stoppingToken);
                if (pending is not null)
                {
                    var victim = FindLowestPriorityRunning();
                    if (victim is not null && pending.Priority > victim.Job.Priority)
                    {
                        _logger.LogWarning(
                            "Preempt: job {PendingId} (prio {PendingPrio}) prerušuje job {VictimId} (prio {VictimPrio})",
                            pending.JobId, pending.Priority, victim.Job.JobId, victim.Job.Priority);
                        await PreemptAsync(victim, stoppingToken);
                        claimedAny = true; // slot sa čoskoro uvoľní — nečakáme na poll
                    }
                }
            }

            // Download sloty
            if (_downloadSemaphore.CurrentCount > 0)
            {
                var job = await _repository.ClaimNextByTypeAsync(JobType.Download, ct: stoppingToken);
                if (job is not null)
                {
                    claimedAny = true;
                    _ = RunInSlotAsync(job, _downloadSemaphore, stoppingToken);
                }
            }

            // Ostatné typy (Sync, Purge) — vlastný maintenance slot, beží nezávisle
            // od analyze/download slotov (S22e). Max 1× za hodinu cez AutoPlanner
            // throttle; Purge čistí expirované unavailable záznamy (retention 30 dní).
            if (_maintenanceSemaphore.CurrentCount > 0)
            {
                var job = await _repository.ClaimNextByTypeAsync(JobType.Sync, ct: stoppingToken)
                          ?? await _repository.ClaimNextByTypeAsync(JobType.Purge, ct: stoppingToken);
                if (job is not null)
                {
                    claimedAny = true;
                    _ = RunInSlotAsync(job, _maintenanceSemaphore, stoppingToken);
                }
            }

            if (!claimedAny && !stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(pollDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Runner stopped.");
    }

    /// <summary>
    /// Recovery po reštarte (S4-7): joby, ktoré boli running pri páde procesu,
    /// sa označia interrupted (traceability) a preplánujú (interrupted → queued).
    /// Priorita jobov zostáva zachovaná — ClaimNext ich vyzdvihne podľa priority.
    /// </summary>
    public async Task RecoverInterruptedJobsAsync(CancellationToken ct)
    {
        var running = await _repository.GetRunningAsync(ct);
        if (running.Count == 0) return;

        _logger.LogWarning("Recovery: {Count} running job(s) z predchádzajúceho behu → preplánované.", running.Count);
        await _repository.MarkInterruptedAsync(ct);
        await _repository.RequeueInterruptedAsync(ct);
    }

    private async Task RunInSlotAsync(Job job, SemaphoreSlim? semaphore, CancellationToken ct)
    {
        if (semaphore is not null)
            await semaphore.WaitAsync(ct);

        // S20: per-job CTS — preempcia môže zrušiť LEN tento job (nie shutdown)
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_runningLock) _running[job.JobId] = new RunningJobContext(job, jobCts);

        try
        {
            await _executor.ExecuteAsync(job, jobCts.Token);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // S20 (FR-08): job prerušený preempciou — requeue na neskoršie spracovanie
            _logger.LogWarning("Job {JobId} preempted — requeue.", job.JobId);
            try
            {
                job.Status = JobStatus.Interrupted;
                await _repository.RequeueInterruptedJobAsync(job.JobId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to requeue preempted job {JobId}.", job.JobId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — job zostane running a po reštarte sa označí interrupted (S4-7).
            _logger.LogWarning("Job {JobId} interrupted by shutdown.", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} ({Type}) failed with unhandled exception.", job.JobId, job.Type);
            try
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                job.FinishedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(job, CancellationToken.None);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to persist failure for job {JobId}.", job.JobId);
            }
        }
        finally
        {
            lock (_runningLock) _running.Remove(job.JobId);
            semaphore?.Release();
        }
    }

    /// <summary>
    /// S20 (FR-08): nájde bežiaci job s najnižšou prioritou (obeť preempcie).
    /// Vracia null, ak nič nebeží. S22e: obeťou môžu byť LEN joby v analysis
    /// slotoch (Analyze/ClipExtract/ExportRange) — preempcia uvoľňuje ANALYSIS
    /// slot. Sync/Purge bežia vo vlastnom maintenance slote a NESMÚ byť
    /// prerušované (inak sa nikdy nedokončia a CheckAvailability nebeží).
    /// </summary>
    private RunningJobContext? FindLowestPriorityRunning()
    {
        lock (_runningLock)
        {
            return _running.Values
                .Where(r => r.Job.Type is JobType.Analyze or JobType.ClipExtract or JobType.ExportRange)
                .OrderBy(r => r.Job.Priority)
                .ThenBy(r => r.Job.JobId)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// S20 (FR-08): preruší bežiaci job (per-job CTS) a počká, kým sa uvoľní slot.
    /// Prerušený job sa v RunInSlotAsync requeue (interrupted → queued).
    /// </summary>
    private async Task PreemptAsync(RunningJobContext victim, CancellationToken ct)
    {
        victim.Cts.Cancel();
        // Počkáme na dokončenie prerušeného jobu (uvoľní semafor + requeue)
        // — krátke čakanie; handler by mal ct rešpektovať promptne.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            lock (_runningLock)
            {
                if (!_running.ContainsKey(victim.Job.JobId)) return;
            }
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { return; }
        }
        _logger.LogWarning("Preempt timeout for job {JobId} — pokračujem.", victim.Job.JobId);
    }

    // ── auto-plánovač (S22-1) ─────────────────────────────────────────────

    /// <summary>
    /// Periodicky plánuje background prácu: Sync (nové nahrávky z NVR) a Analyze
    /// pre záznamy čakajúce na analýzu (analysis_state queued/failed bez aktívneho
    /// Analyze jobu). Background priorita (JobSource.Background) — interaktívne
    /// requesty majú vždy prednosť (preempcia S20). Video/klip sa sťahuje len na
    /// trigger (interaktívny request), NIE v background pláne.
    /// </summary>
    private async Task AutoPlannerLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await AutoPlanAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoPlanner tick failed");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    /// <summary>Jeden plánovací tick: sync + purge (každých 15 min) + analyze z najnovšieho okna.</summary>
    public async Task AutoPlanAsync(CancellationToken ct)
    {
        // 1) Sync — len ak žiadny nebeží A posledný dokončený je starší ako SyncIntervalMinutes.
        //    S22l: 15 min (NVR uzatvára segment na kvartál); sync rieši len posledné okná.
        if (!await _repository.HasActiveJobAsync(JobType.Sync, ct))
        {
            var lastSync = await _repository.GetLastCompletedAtAsync(JobType.Sync, ct);
            if (lastSync is null || DateTime.UtcNow - lastSync.Value >= TimeSpan.FromMinutes(_options.SyncIntervalMinutes))
            {
                await _repository.EnqueueAsync(new Job
                {
                    Type = JobType.Sync,
                    Status = JobStatus.Queued,
                    Priority = JobPriority.Background,
                    Source = JobSource.Background,
                }, ct);
                _logger.LogInformation("AutoPlanner: Sync enqueue (background, posledný pred {Last})",
                    lastSync is null ? "nikdy" : lastSync.Value.ToString("HH:mm"));
            }
        }

        // 2) Purge — retention okien (rovnaký interval ako sync: 15 min)
        if (!await _repository.HasActiveJobAsync(JobType.Purge, ct))
        {
            var lastPurge = await _repository.GetLastCompletedAtAsync(JobType.Purge, ct);
            if (lastPurge is null || DateTime.UtcNow - lastPurge.Value >= TimeSpan.FromMinutes(_options.SyncIntervalMinutes))
            {
                await _repository.EnqueueAsync(new Job
                {
                    Type = JobType.Purge,
                    Status = JobStatus.Queued,
                    Priority = JobPriority.Background,
                    Source = JobSource.Background,
                }, ct);
                _logger.LogInformation("AutoPlanner: Purge enqueue (background, retention okien)");
            }
        }

        // 3) Analyze — S22l: NÁJNOVŠIE DOKONČENÉ okno naprieč kamerami (kamery 1→8).
        //    S22j: ak sú analýzy vypnuté (WatchForge__Runner__AnalysesEnabled=false),
        //    žiadne nové joby sa nevytvárajú.
        //    Staršie okná sa nedorábajú (nestíhali by sme ani novšie) — rolling 60 min.
        if (_options.AnalysesEnabled)
        {
            var window = await _recordings.GetLatestWindowAsync(_options.WindowMinutes, DateTime.UtcNow,
                _options.RetentionWindows, ct);
            if (window.Count > 0)
            {
                var windowStart = window.Min(r => r.BeginTime);
                var windowEnd = window.Max(r => r.EndTime);
                // Priorita = vek okna (novšie = vyššia) — ClaimNextByTypeAsync triedi priority DESC.
                // Clamp: nikdy nepresiahne WebUi (okno v budúcnosti by dalo negatívny vek).
                var windowAge = Math.Max(0, (DateTime.UtcNow - windowEnd).TotalMinutes);
                var priority = Math.Clamp(JobPriority.WebUi - (int)windowAge, JobPriority.Background, JobPriority.WebUi);
                var enqueued = 0;
                foreach (var recording in window)
                {
                    // Iba záznamy bez aktívneho analyze jobu a bez dokončenej analýzy
                    if (recording.AnalysisState is "completed") continue;
                    await _repository.EnqueueAsync(new Job
                    {
                        RecordingId = recording.RecordingId,
                        Type = JobType.Analyze,
                        Status = JobStatus.Queued,
                        Priority = priority,
                        Source = JobSource.Background,
                        // S22n: okno v payload — AnalyzeJobHandler oreže segment na presných 15 min
                        Payload = $"{{\"windowStart\":\"{windowStart:O}\",\"windowEnd\":\"{windowEnd:O}\"}}",
                    }, ct);
                    enqueued++;
                }
                if (enqueued > 0)
                    _logger.LogInformation("AutoPlanner: Analyze enqueue {Count} pre okno {Start:HH:mm}-{End:HH:mm} (priorita {Priority})",
                        enqueued, windowStart, windowEnd, priority);
            }
        }
    }
}
