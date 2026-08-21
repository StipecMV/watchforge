using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Runner;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-1: Runner worker loop — claim jobov, semafory (S16: defaults MaxParallelAnalyses=4,
/// MaxParallelDownloads=4), dispatch na handlery, failed pri chýbajúcom handleri, idle pri prázdnom fronte.
/// </summary>
public class RunnerServiceTests
{
    private static RunnerOptions TestOptions(int pollMs = 50) => new()
    {
        MaxParallelAnalyses = 2,
        MaxParallelDownloads = 2,
        PollIntervalMs = pollMs,
        DbPath = "test.db"
    };

    private static Mock<IJobRepository> EmptyQueueRepo()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.IsAny<JobType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        return repo;
    }

    [Test]
    public async Task Runner_ClaimsAndExecutesAnalyzeJob()
    {
        // Given analyze job vo fronte a handler, ktorý job dokončí
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var job = new Job { JobId = 1, Type = JobType.Analyze, Priority = JobPriority.WhatsApp, Status = JobStatus.Queued };
        repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job)
            .ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Mock<IJobHandler>();
        handler.SetupGet(h => h.HandlesType).Returns(JobType.Analyze);
        handler.Setup(h => h.ExecuteAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((j, _) =>
            {
                j.Status = JobStatus.Completed;
                j.FinishedAt = DateTime.UtcNow;
                executed.SetResult();
            })
            .Returns(Task.CompletedTask);

        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([handler.Object], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        // When spustíme worker loop
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then handler vykonal job (Completed) a service sa dá čisto zastaviť
        await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        handler.Verify(h => h.ExecuteAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Runner_ClaimsAndExecutesClipExtractJob()
    {
        // S9-4 regresný test: ClipExtract joby sa claimujú v analysis slotoch
        // (bug: RunnerService claimoval len Analyze → ClipExtract ostal navždy queued)
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var clipJob = new Job { JobId = 2, Type = JobType.ClipExtract, Priority = JobPriority.WebUi, Status = JobStatus.Queued };
        repo.Setup(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.ClipExtract, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clipJob)
            .ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze && t != JobType.ClipExtract), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Mock<IJobHandler>();
        handler.SetupGet(h => h.HandlesType).Returns(JobType.ClipExtract);
        handler.Setup(h => h.ExecuteAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((j, _) =>
            {
                j.Status = JobStatus.Completed;
                j.FinishedAt = DateTime.UtcNow;
                executed.SetResult();
            })
            .Returns(Task.CompletedTask);

        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([handler.Object], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(clipJob.Status).IsEqualTo(JobStatus.Completed);
        handler.Verify(h => h.ExecuteAsync(clipJob, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Runner_MaxParallelAnalyses_LimitsToTwo()
    {
        // Given 3 analyze joby a handler, ktorý drží joby na gate
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var jobs = Enumerable.Range(1, 3)
            .Select(i => new Job { JobId = i, Type = JobType.Analyze, Priority = 100 - i, Status = JobStatus.Queued })
            .ToList();
        var sequence = repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var j in jobs) sequence.ReturnsAsync(j);
        sequence.ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var gate = new SemaphoreSlim(0, 3);
        var gatedHandler = new GatedAnalyzeHandler(gate);
        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([gatedHandler], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        // When spustíme worker loop
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then začnú len 2 joby (semafor) — tretí čaká
        await gatedHandler.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300); // šanca, že by service nesprávne spustil tretí
        await Assert.That(gatedHandler.MaxConcurrent).IsEqualTo(2);

        // When uvoľníme gate
        gate.Release(3);

        // Then všetky 3 joby dokončia
        await gatedHandler.AllDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(gatedHandler.CompletedCount).IsEqualTo(3);
    }

    [Test]
    public async Task Defaults_AreFourParallel_S16()
    {
        // S16: po reálnom benchmarku (S15 v2) sú defaults 4 — analýza je bottleneck,
        // 8 jadier zvládne 4 paralelné analýzy (81 % CPU), NVR drží 5 Mbit/s per stream.
        var options = new RunnerOptions();
        await Assert.That(RunnerOptions.DefaultMaxParallelAnalyses).IsEqualTo(4);
        await Assert.That(RunnerOptions.DefaultMaxParallelDownloads).IsEqualTo(4);
        await Assert.That(options.MaxParallelAnalyses).IsEqualTo(4);
        await Assert.That(options.MaxParallelDownloads).IsEqualTo(4);
    }

    [Test]
    public async Task Runner_MaxParallelAnalyses_AllowsFour()
    {
        // Given 5 analyze joby a handler, ktorý drží joby na gate (semafor 4)
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var jobs = Enumerable.Range(1, 5)
            .Select(i => new Job { JobId = i, Type = JobType.Analyze, Priority = 100 - i, Status = JobStatus.Queued })
            .ToList();
        var sequence = repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var j in jobs) sequence.ReturnsAsync(j);
        sequence.ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var gate = new SemaphoreSlim(0, 5);
        var gatedHandler = new GatedAnalyzeHandler(gate, startedThreshold: 4, completedThreshold: 5);
        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([gatedHandler], repo.Object),
            new RunnerOptions { MaxParallelAnalyses = 4, MaxParallelDownloads = 4, PollIntervalMs = 50, DbPath = "test.db" },
            NullLogger<RunnerService>.Instance);

        // When spustíme worker loop
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then začnú 4 joby (semafor 4) — piaty čaká
        await gatedHandler.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300); // šanca, že by service nesprávne spustil piaty
        await Assert.That(gatedHandler.MaxConcurrent).IsEqualTo(4);

        // When uvoľníme gate
        gate.Release(5);

        // Then všetkých 5 jobov dokončí
        await gatedHandler.AllDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(gatedHandler.CompletedCount).IsEqualTo(5);
    }

    [Test]
    public async Task Runner_HighPriorityJob_PreemptsLowestPriorityBackground()
    {
        // S20 (FR-08): analysis sloty plné (4× background) + príde WhatsApp job (prio 100)
        // → preruší najnižšie-prioritný bežiaci job (prio 10), ten sa requeue,
        // prioritný job sa claimne a spustí.
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var jobs = Enumerable.Range(1, 4)
            .Select(i => new Job { JobId = i, Type = JobType.Analyze, Priority = JobPriority.Background, Status = JobStatus.Queued })
            .Append(new Job { JobId = 5, Type = JobType.Analyze, Priority = JobPriority.WhatsApp, Status = JobStatus.Queued })
            .ToList();
        // Queue-based: po vyčerpaní vráti null (Task), nie null referenciu (await null = NRE)
        var claimQueue = new Queue<Job?>(jobs.Append(null).Append(null).Append(null).Append(null).Append(null));
        repo.Setup(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Job?>(claimQueue.Count > 0 ? claimQueue.Dequeue() : null));
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        // Peek (slot plný) vráti prioritný job 5
        repo.Setup(r => r.PeekNextByTypeAsync(JobType.Analyze, It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs[4]);
        repo.Setup(r => r.RequeueInterruptedJobAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var gate = new SemaphoreSlim(0, 5);
        var gatedHandler = new GatedAnalyzeHandler(gate, startedThreshold: 4, completedThreshold: 5);
        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([gatedHandler], repo.Object),
            new RunnerOptions { MaxParallelAnalyses = 4, MaxParallelDownloads = 4, PollIntervalMs = 50, DbPath = "test.db" },
            NullLogger<RunnerService>.Instance);

        // When spustíme worker loop
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then 4 background joby bežia (sloty plné)
        await gatedHandler.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300); // šanca pre preempt logiku

        // Then najnižšie-prioritný bežiaci job (1) bol preemptnutý → requeue
        repo.Verify(r => r.RequeueInterruptedJobAsync(1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        // A prioritný job (5) bol claimnutý
        repo.Verify(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeast(5));

        // Upraceme: preemptovaný job skončil, zvyšné čakajú na gate — ukončíme cez shutdown
        cts.Cancel();
        await runTask;
    }

    [Test]
    public async Task Runner_LowerPriorityJob_DoesNotPreempt()
    {
        // S20 (FR-08): sloty plné, ale čakajúci job má NIŽŠIU prioritu (Background 10)
        // ako bežiaci (System 50) → žiadna preempcia, žiadny requeue.
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var running = Enumerable.Range(1, 4)
            .Select(i => new Job { JobId = i, Type = JobType.Analyze, Priority = JobPriority.System, Status = JobStatus.Queued })
            .ToList();
        var sequence = repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Analyze, It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var j in running) sequence.ReturnsAsync(j);
        sequence.ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Analyze), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        // Peek vráti job s nižšou prioritou (Background 10 < System 50)
        repo.Setup(r => r.PeekNextByTypeAsync(JobType.Analyze, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { JobId = 9, Type = JobType.Analyze, Priority = JobPriority.Background, Status = JobStatus.Queued });

        var gate = new SemaphoreSlim(0, 5);
        var gatedHandler = new GatedAnalyzeHandler(gate, startedThreshold: 4, completedThreshold: 5);
        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([gatedHandler], repo.Object),
            new RunnerOptions { MaxParallelAnalyses = 4, MaxParallelDownloads = 4, PollIntervalMs = 50, DbPath = "test.db" },
            NullLogger<RunnerService>.Instance);

        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then 4 joby bežia a NIKTO nebol preemptnutý
        await gatedHandler.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300); // šanca, že by service nesprávne preemptol
        repo.Verify(r => r.RequeueInterruptedJobAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        cts.Cancel();
        await runTask;
    }

    [Test]
    public async Task Runner_NoHandler_MarksJobFailed()
    {
        // Given Sync job, ale žiadny handler pre Sync (JobExecutor bez handlerov)
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var job = new Job { JobId = 1, Type = JobType.Sync, Priority = JobPriority.System, Status = JobStatus.Queued };
        repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Sync, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job)
            .ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Sync), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((_, _) => updated.TrySetResult())
            .Returns(Task.CompletedTask);

        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        // When spustíme worker loop
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        // Then job je Failed s jasným dôvodom
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(job.Status).IsEqualTo(JobStatus.Failed);
        await Assert.That(job.Error).Contains("No handler");
        repo.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Runner_DownloadJob_ExecutedInDownloadSlot()
    {
        // Given download job a handler
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.GetRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Job>)[]);
        var job = new Job { JobId = 7, Type = JobType.Download, Priority = JobPriority.Background, Status = JobStatus.Queued };
        repo.SetupSequence(r => r.ClaimNextByTypeAsync(JobType.Download, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job)
            .ReturnsAsync((Job?)null);
        repo.Setup(r => r.ClaimNextByTypeAsync(It.Is<JobType>(t => t != JobType.Download), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Mock<IJobHandler>();
        handler.SetupGet(h => h.HandlesType).Returns(JobType.Download);
        handler.Setup(h => h.ExecuteAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((j, _) => { j.Status = JobStatus.Completed; executed.SetResult(); })
            .Returns(Task.CompletedTask);

        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([handler.Object], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);

        await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await runTask;
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
    }

    [Test]
    public async Task Runner_Idle_WhenQueueEmpty()
    {
        // Given prázdny front
        var repo = EmptyQueueRepo();
        var service = new RunnerService(repo.Object, new Mock<IRecordingRepository>().Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        // When worker loop beží chvíľu a potom sa zastaví
        using var cts = new CancellationTokenSource();
        var runTask = service.RunAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();

        // Then skončí čisto bez výnimky
        await runTask;
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Recovery_RequeuesRunningJobs_KeepingPriority()
    {
        // Given DB s running jobom (simulácia pádu počas behu) a queued jobom
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-recovery-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);
            var repo = new JobRepository(connection);
            var runningJob = await repo.EnqueueAsync(new Job
            {
                Type = JobType.Analyze, Priority = JobPriority.WhatsApp, Source = JobSource.WhatsApp,
                Status = JobStatus.Running,
            });
            await repo.EnqueueAsync(new Job { Type = JobType.Download, Priority = JobPriority.Background, Source = JobSource.Background });

            var service = new RunnerService(repo, new Mock<IRecordingRepository>().Object, new Mock<IJobExecutor>().Object,
                TestOptions(), NullLogger<RunnerService>.Instance);

            // When recovery po reštarte
            await service.RecoverInterruptedJobsAsync(CancellationToken.None);

            // Then running job je preplánovaný (queued) s pôvodnou prioritou
            var stored = await repo.GetByIdAsync(runningJob.JobId);
            await Assert.That(stored!.Status).IsEqualTo(JobStatus.Queued);
            await Assert.That(stored.Priority).IsEqualTo(JobPriority.WhatsApp);
            await Assert.That(stored.Source).IsEqualTo(JobSource.WhatsApp);

            // And je zase claimovateľný (najvyššia priorita)
            var claimed = await repo.ClaimNextAsync();
            await Assert.That(claimed).IsNotNull();
            await Assert.That(claimed!.JobId).IsEqualTo(runningJob.JobId);
        }
        finally
        {
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Test]
    public async Task Recovery_NoRunningJobs_DoesNotChangeQueued()
    {
        // Given DB bez running jobov (len queued)
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-recovery2-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);
            var repo = new JobRepository(connection);
            var queued = await repo.EnqueueAsync(new Job { Type = JobType.Sync, Priority = 50, Source = JobSource.System });

            var service = new RunnerService(repo, new Mock<IRecordingRepository>().Object, new Mock<IJobExecutor>().Object,
                TestOptions(), NullLogger<RunnerService>.Instance);

            // When recovery
            await service.RecoverInterruptedJobsAsync(CancellationToken.None);

            // Then queued job zostáva nedotknutý
            var stored = await repo.GetByIdAsync(queued.JobId);
            await Assert.That(stored!.Status).IsEqualTo(JobStatus.Queued);
        }
        finally
        {
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    private sealed class GatedAnalyzeHandler(SemaphoreSlim gate, int startedThreshold = 2, int completedThreshold = 3) : IJobHandler
    {
        public JobType HandlesType => JobType.Analyze;
        public TaskCompletionSource TwoStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FourStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public int CompletedCount => Volatile.Read(ref _completedCount);
        private int _concurrent;
        private int _maxConcurrent;
        private int _completedCount;

        public async Task ExecuteAsync(Job job, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _concurrent);
            InterlockedMax(ref _maxConcurrent, current);
            if (current >= 2) TwoStarted.TrySetResult();
            if (current >= startedThreshold) FourStarted.TrySetResult();

            await gate.WaitAsync(ct);

            Interlocked.Decrement(ref _concurrent);
            job.Status = JobStatus.Completed;
            var completed = Interlocked.Increment(ref _completedCount);
            if (completed >= completedThreshold) AllDone.TrySetResult();
        }

        private static void InterlockedMax(ref int target, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
                    return;
            }
        }
    }

    // ── auto-plánovač (S22-1) ─────────────────────────────────────────────

    [Test]
    public async Task AutoPlan_EnqueuesSync_WhenNoneActive_AndNoPreviousSync()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.GetLastCompletedAtAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Sync), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AutoPlan_DoesNotEnqueueSync_WhenLastSyncWasRecent_Under60Minutes()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.HasActiveJobAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        // Posledný sync pred 5 minútami — do 60-min okna → sync sa NEspúšťa
        repo.Setup(r => r.GetLastCompletedAtAsync(JobType.Sync, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5));
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AutoPlan_EnqueuesSync_WhenLastSyncWasMoreThan60MinutesAgo()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.GetLastCompletedAtAsync(JobType.Sync, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-90));
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Sync), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AutoPlan_DoesNotEnqueueSync_WhenOneIsActive()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.HasActiveJobAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AutoPlan_EnqueuesAnalyzeForPendingRecordings()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.HasActiveJobAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var window = new List<Recording>
        {
            new() { RecordingId = 11, BeginTime = DateTime.UtcNow.AddMinutes(-10), EndTime = DateTime.UtcNow.AddMinutes(-5), AnalysisState = "queued" },
            new() { RecordingId = 12, BeginTime = DateTime.UtcNow.AddMinutes(-10), EndTime = DateTime.UtcNow.AddMinutes(-5), AnalysisState = "queued" },
        };
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(15, It.IsAny<DateTime>(), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(window);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Analyze && j.RecordingId == 11), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Analyze && j.RecordingId == 12), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AutoPlan_EnqueuesPurge_WhenNoActivePurge_AndNoneRecently()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.HasActiveJobAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.GetLastCompletedAtAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Purge), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AutoPlan_DoesNotEnqueuePurge_WhenLastPurgeWasRecent()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.HasActiveJobAsync(JobType.Sync, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.HasActiveJobAsync(JobType.Purge, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.GetLastCompletedAtAsync(JobType.Purge, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5));
        var recordings = new Mock<IRecordingRepository>();
        recordings.Setup(r => r.GetLatestWindowAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Recording>)[]);

        var service = new RunnerService(repo.Object, recordings.Object, new JobExecutor([], repo.Object),
            TestOptions(), NullLogger<RunnerService>.Instance);

        await service.AutoPlanAsync(CancellationToken.None);

        repo.Verify(r => r.EnqueueAsync(It.Is<Job>(j => j.Type == JobType.Purge), It.IsAny<CancellationToken>()), Times.Never);
    }
}
