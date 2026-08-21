using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-3 + S2-4: JobRepository — enqueue, claim (priorita), update, interrupted.
/// Integračné testy na reálnej SQLite (temp súbor).
/// </summary>
public class JobRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-jobs-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private JobRepository? _repo;

    private async Task<JobRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new JobRepository(_connection);
        return _repo;
    }

    [Test]
    public async Task Enqueue_ThenClaim_ReturnsJobInPriorityOrder()
    {
        // Given background job a whatsapp job (rôzna priorita)
        var repo = await RepoAsync();
        var bg = new Job { Type = JobType.Download, Priority = JobPriority.Background, Source = JobSource.Background };
        var wa = new Job { Type = JobType.Analyze, Priority = JobPriority.WhatsApp, Source = JobSource.WhatsApp };
        await repo.EnqueueAsync(bg);
        await repo.EnqueueAsync(wa);

        // When claimneme dva joby
        var first = await repo.ClaimNextAsync();
        var second = await repo.ClaimNextAsync();

        // Then najprv príde whatsapp (vyššia priorita)
        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Source).IsEqualTo(JobSource.WhatsApp);
        await Assert.That(first.Status).IsEqualTo(JobStatus.Running);
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Source).IsEqualTo(JobSource.Background);
    }

    [Test]
    public async Task Claim_EmptyQueue_ReturnsNull()
    {
        // Given prázdny front
        var repo = await RepoAsync();

        // When claimneme
        var job = await repo.ClaimNextAsync();

        // Then null
        await Assert.That(job).IsNull();
    }

    [Test]
    public async Task Claim_CompletedJob_IsNotClaimedAgain()
    {
        // Given job ktorý sa dokončil
        var repo = await RepoAsync();
        var job = new Job { Type = JobType.Sync, Priority = JobPriority.System, Source = JobSource.System };
        var created = await repo.EnqueueAsync(job);
        var id = created.JobId;
        var claimed = await repo.ClaimNextAsync();
        claimed!.Status = JobStatus.Completed;
        await repo.UpdateAsync(claimed);

        // When claimneme znova
        var next = await repo.ClaimNextAsync();

        // Then nič nepríde (completed sa neclaimuje)
        await Assert.That(next).IsNull();
        var stored = await repo.GetByIdAsync(id);
        await Assert.That(stored!.Status).IsEqualTo(JobStatus.Completed);
    }

    [Test]
    public async Task Claim_InterruptedJob_IsRequeued()
    {
        // Given job prerušený (reštart) — status interrupted
        var repo = await RepoAsync();
        var job = new Job { Type = JobType.Download, Priority = JobPriority.Background, Source = JobSource.Background };
        var created = await repo.EnqueueAsync(job);
        var id = created.JobId;
        var claimed = await repo.ClaimNextAsync();
        claimed!.Status = JobStatus.Interrupted;
        await repo.UpdateAsync(claimed);

        // When preplánujeme (interrupted → queued)
        var stored = await repo.GetByIdAsync(id);
        stored!.Status = JobStatus.Queued;
        await repo.UpdateAsync(stored);

        // Then je zase claimovateľný
        var again = await repo.ClaimNextAsync();
        await Assert.That(again).IsNotNull();
        await Assert.That(again!.Status).IsEqualTo(JobStatus.Running);
    }

    [Test]
    public async Task GetRunning_ReturnsOnlyRunningJobs()
    {
        // Given jeden running a jeden queued job
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 10, Source = JobSource.Background });
        var claimed = await repo.ClaimNextAsync();

        // When zoznam running
        var running = await repo.GetRunningAsync();

        // Then obsahuje len claimnutý
        await Assert.That(running).Count().IsEqualTo(1);
        await Assert.That(running[0].JobId).IsEqualTo(claimed!.JobId);
    }

    [Test]
    public async Task MarkInterrupted_RequeuesAllRunningJobs()
    {
        // Given dva running joby (simulácia reštartu)
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Download, Priority = 10, Source = JobSource.Background });
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 20, Source = JobSource.Background });
        await repo.ClaimNextAsync();
        await repo.ClaimNextAsync();

        // When reštart: mark interrupted a preplánujeme
        await repo.MarkInterruptedAsync();
        await repo.RequeueInterruptedAsync();

        // Then žiadny running nezostane a joby sú queued (preplánované)
        var running = await repo.GetRunningAsync();
        await Assert.That(running).IsEmpty();
        var next = await repo.ClaimNextAsync();
        await Assert.That(next).IsNotNull();
        await Assert.That(next!.Status).IsEqualTo(JobStatus.Running);
    }

    [Test]
    public async Task Update_Progress_IsPersisted()
    {
        // Given job v behu
        var repo = await RepoAsync();
        var created = await repo.EnqueueAsync(new Job { Type = JobType.Download, Priority = 10, Source = JobSource.Background });
        var id = created.JobId;
        var claimed = await repo.ClaimNextAsync();
        claimed!.Progress = 55;
        claimed.Error = "test error";
        await repo.UpdateAsync(claimed);

        // When načítame
        var stored = await repo.GetByIdAsync(id);

        // Then progress aj error prežili
        await Assert.That(stored!.Progress).IsEqualTo(55);
        await Assert.That(stored.Error).IsEqualTo("test error");
    }

    [Test]
    public async Task ClaimByType_ReturnsOnlyMatchingType()
    {
        // Given download job (nízka priorita) a analyze job (vysoká priorita)
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Download, Priority = JobPriority.Background, Source = JobSource.Background });
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = JobPriority.WhatsApp, Source = JobSource.WhatsApp });

        // When claimneme podľa typu Analyze
        var analyze = await repo.ClaimNextByTypeAsync(JobType.Analyze);

        // Then príde analyze job (nie download — ten má síce nižšiu prioritu, ale typ sa filtruje)
        await Assert.That(analyze).IsNotNull();
        await Assert.That(analyze!.Type).IsEqualTo(JobType.Analyze);
        await Assert.That(analyze.Status).IsEqualTo(JobStatus.Running);

        // A download job zostáva queued (nedotknutý)
        var download = await repo.ClaimNextByTypeAsync(JobType.Download);
        await Assert.That(download).IsNotNull();
        await Assert.That(download!.Type).IsEqualTo(JobType.Download);
    }

    [Test]
    public async Task ClaimByType_NoJobOfThatType_ReturnsNull()
    {
        // Given len download job
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Download, Priority = 10, Source = JobSource.Background });

        // When claimneme Analyze
        var job = await repo.ClaimNextByTypeAsync(JobType.Analyze);

        // Then null (analyze front je prázdny)
        await Assert.That(job).IsNull();
    }

    [Test]
    public async Task ClaimByType_RespectsMaxPriority()
    {
        // Given analyze job s prioritou 100 a druhy s prioritou 10
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 100, Source = JobSource.WhatsApp });
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 10, Source = JobSource.Background });

        // When claimneme s maxPriority = 50
        var job = await repo.ClaimNextByTypeAsync(JobType.Analyze, maxPriority: 50);

        // Then príde len ten s prioritou 10 (100 je nad limitom)
        await Assert.That(job).IsNotNull();
        await Assert.That(job!.Priority).IsEqualTo(10);
    }

    [Test]
    public async Task StatusSummary_CountsByStatus()
    {
        // Given joby v rôznych stavoch — S22m: summary počíta LEN Analyze joby
        // (purge/sync/download sú pozadie, nepatria do „analýz" stats-baru)
        var repo = await RepoAsync();
        await repo.EnqueueAsync(new Job { Type = JobType.Sync, Priority = 20, Source = JobSource.System });
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 10, Source = JobSource.Background });
        await repo.EnqueueAsync(new Job { Type = JobType.Analyze, Priority = 10, Source = JobSource.Background });

        var claimed1 = await repo.ClaimNextAsync();  // sync → running (mimo analyze)
        var claimed2 = await repo.ClaimNextAsync();  // analyze → running
        claimed1!.Status = JobStatus.Completed;
        await repo.UpdateAsync(claimed1);

        // When status summary
        var summary = await repo.GetStatusSummaryAsync();

        // Then počty (len analyze: 1 queued + 1 running; sync/download sú mimo)
        await Assert.That(summary.Queued).IsEqualTo(1);
        await Assert.That(summary.Running).IsEqualTo(1);     // analyze
        await Assert.That(summary.Completed).IsEqualTo(0);   // sync je mimo (nie analyze)
        await Assert.That(summary.Total).IsEqualTo(2);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
