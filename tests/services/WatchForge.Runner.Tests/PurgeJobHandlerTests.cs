using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-5: PurgeJobHandler — retention/cleanup: expirované recordingy (s detekciami),
/// persist výnimka, expirované klipy, zvyšky lokálnych videí.
/// </summary>
public class PurgeJobHandlerTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-purge-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-purge-media-" + Guid.NewGuid().ToString("N"));
    private SqliteConnection? _connection;

    public PurgeJobHandlerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    private async Task<SqliteConnection> DbAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', 34567, 'admin', 'WF_TEST_NVR_PASSWORD');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1);
            """;
        await cmd.ExecuteNonQueryAsync();
        return _connection;
    }

    private static async Task<Recording> InsertRecordingAsync(SqliteConnection connection,
        bool persisted = false, DateTime? purgeAt = null, DateTime? beginTime = null, DateTime? endTime = null,
        bool personPending = false)
    {
        var repo = new RecordingRepository(connection);
        var begin = beginTime ?? new DateTime(2026, 4, 5, 15, 0, 0);
        var end = endTime ?? new DateTime(2026, 4, 5, 15, 15, 0);
        var recording = new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment",
            NvrFilename = $"[Ch0]_2026-04-05_15.00.00-15.15.mkv_{Guid.NewGuid():N}",
            BeginTime = begin,
            EndTime = end,
            DurationSec = 900, SizeBytes = 1024,
            Availability = "unavailable", UnavailableSince = new DateTime(2026, 3, 1),
            PurgeAt = purgeAt, Persisted = persisted, PersonPending = personPending, AnalysisState = "completed",
        };
        recording.RecordingId = await repo.InsertAsync(recording);
        return recording;
    }

    private static async Task InsertDetectionAsync(SqliteConnection connection, int recordingId)
    {
        var repo = new DetectionRepository(connection);
        await repo.InsertAsync(new Detection
        {
            RecordingId = recordingId, CameraId = 1, DetectionType = "motion",
            TimestampMs = 500, DurationMs = 500,
            Region = new NormalizedRegion(0.1f, 0.1f, 0.2f, 0.2f),
        });
    }

    private static PurgeJobHandler CreateHandler(SqliteConnection connection, string tempDir, DateTime now)
        => new(
            new RecordingRepository(connection),
            new DetectionRepository(connection),
            new ClipRepository(connection),
            new JobRepository(connection),
            new FixedClock(now),
            new DownloadOptions { TempDir = tempDir });

    [Test]
    public async Task Purge_ExpiredRecording_DeletedWithDetections()
    {
        // Given recording po retencii (purge_at minulý) s detekciou
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection,
            purgeAt: new DateTime(2026, 4, 1));
        await InsertDetectionAsync(connection, recording.RecordingId);
        var handler = CreateHandler(connection, _tempDir, new DateTime(2026, 4, 10));

        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        // When purge
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then recording aj detekcie sú preč
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        var repo = new RecordingRepository(connection);
        await Assert.That(await repo.GetByIdAsync(recording.RecordingId)).IsNull();
        var detections = await new DetectionRepository(connection).QueryAsync(1, null, null, "motion", null);
        await Assert.That(detections).IsEmpty();
    }

    [Test]
    public async Task Purge_PersistedRecording_IsKept()
    {
        // Given persistnutý recording (user označil zachovaj) — aj po purge_at
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection,
            persisted: true, purgeAt: new DateTime(2026, 4, 1));
        var handler = CreateHandler(connection, _tempDir, new DateTime(2026, 4, 10));

        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        // When purge
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then recording zostáva (persist výnimka)
        var repo = new RecordingRepository(connection);
        await Assert.That(await repo.GetByIdAsync(recording.RecordingId)).IsNotNull();
    }

    [Test]
    public async Task Purge_RecordingInsideRetentionWindow_IsKept()
    {
        // S22l: recording v rámci rolling okien (begin_time nedávno) zostáva
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection,
            beginTime: DateTime.UtcNow.AddMinutes(-10), endTime: DateTime.UtcNow.AddMinutes(-5));
        var handler = CreateHandler(connection, _tempDir, DateTime.UtcNow);

        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        await Assert.That(await new RecordingRepository(connection).GetByIdAsync(recording.RecordingId)).IsNotNull();
    }

    [Test]
    public async Task Purge_RecordingOutsideRetentionWindow_IsDeleted()
    {
        // S22l: recording mimo rolling okien (begin_time starší ako cutoff) sa maže
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection,
            beginTime: DateTime.UtcNow.AddMinutes(-120), endTime: DateTime.UtcNow.AddMinutes(-115));
        await InsertDetectionAsync(connection, recording.RecordingId);
        var handler = CreateHandler(connection, _tempDir, DateTime.UtcNow);

        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        await Assert.That(await new RecordingRepository(connection).GetByIdAsync(recording.RecordingId)).IsNull();
    }

    [Test]
    public async Task Purge_PersonPendingRecording_IsKept()
    {
        // S22l: person_pending (skip pre cleanup — našla sa osoba) sa NIKDY automaticky nemaže
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection,
            beginTime: DateTime.UtcNow.AddMinutes(-120), endTime: DateTime.UtcNow.AddMinutes(-115),
            personPending: true);
        var handler = CreateHandler(connection, _tempDir, DateTime.UtcNow);

        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        await Assert.That(await new RecordingRepository(connection).GetByIdAsync(recording.RecordingId)).IsNotNull();
    }

    [Test]
    public async Task Purge_ExpiredClip_DeletedWithFile()
    {
        // Given expirovaný klip so súborom
        var clipFile = Path.Combine(_tempDir, "clip-1.mp4");
        await File.WriteAllBytesAsync(clipFile, [1, 2, 3]);

        var connection = await DbAsync();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO REQUESTS (source, requester, query, from_time, to_time, status)
                VALUES ('test', 'tester', '', $start, $end, 'completed');
                INSERT INTO CLIPS (request_id, recording_id, range_start, range_end, file_path, size_bytes, kind, created_at, expires_at)
                VALUES (last_insert_rowid(), NULL, $start, $end, $path, 3, 'video', $created, $expires);
                """;
            cmd.Parameters.AddWithValue("$start", "2026-04-01T00:00:00Z");
            cmd.Parameters.AddWithValue("$end", "2026-04-01T00:01:00Z");
            cmd.Parameters.AddWithValue("$path", clipFile);
            cmd.Parameters.AddWithValue("$created", "2026-04-01T00:00:00Z");
            cmd.Parameters.AddWithValue("$expires", "2026-04-05T00:00:00Z");
            await cmd.ExecuteNonQueryAsync();
        }

        var handler = CreateHandler(connection, _tempDir, new DateTime(2026, 4, 10));
        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        // When purge
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then klip aj súbor sú preč
        var clips = await new ClipRepository(connection).GetExpiredAsync(new DateTime(2026, 4, 10));
        await Assert.That(clips).IsEmpty();
        await Assert.That(File.Exists(clipFile)).IsFalse();
    }

    [Test]
    public async Task Purge_NotYetExpiredClip_IsKept()
    {
        // Given klip, ktorý ešte neexpiroval (expires_at v budúcnosti — FR-13 cache 2–3 h)
        var clipFile = Path.Combine(_tempDir, "clip-cache.mp4");
        await File.WriteAllBytesAsync(clipFile, [1, 2, 3]);

        var connection = await DbAsync();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO REQUESTS (source, requester, query, from_time, to_time, status)
                VALUES ('test', 'tester', '', $start, $end, 'completed');
                INSERT INTO CLIPS (request_id, recording_id, range_start, range_end, file_path, size_bytes, kind, created_at, expires_at)
                VALUES (last_insert_rowid(), NULL, $start, $end, $path, 3, 'video', $created, $expires);
                """;
            cmd.Parameters.AddWithValue("$start", "2026-04-01T00:00:00Z");
            cmd.Parameters.AddWithValue("$end", "2026-04-01T00:01:00Z");
            cmd.Parameters.AddWithValue("$path", clipFile);
            cmd.Parameters.AddWithValue("$created", "2026-04-01T00:00:00Z");
            cmd.Parameters.AddWithValue("$expires", "2026-04-20T00:00:00Z"); // ešte platný
            await cmd.ExecuteNonQueryAsync();
        }

        var handler = CreateHandler(connection, _tempDir, new DateTime(2026, 4, 10));
        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        // When purge
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then klip aj súbor ostávajú (cache ešte platí)
        var clips = await new ClipRepository(connection).GetExpiredAsync(new DateTime(2026, 4, 10));
        await Assert.That(clips).IsEmpty();
        await Assert.That(File.Exists(clipFile)).IsTrue();
    }

    [Test]
    public async Task Purge_StaleVideoFile_Deleted()
    {
        // Given starý súbor v media cache (starší ako 24h)
        var staleFile = Path.Combine(_tempDir, "[Ch0]_old.downloading");
        await File.WriteAllBytesAsync(staleFile, [9]);
        File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-2));

        var connection = await DbAsync();
        var handler = CreateHandler(connection, _tempDir, DateTime.UtcNow);
        var job = new Job { JobId = 1, Type = JobType.Purge, Priority = JobPriority.System, Status = JobStatus.Running };

        // When purge
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then starý súbor je zmazaný
        await Assert.That(File.Exists(staleFile)).IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class MockJobRepository : IJobRepository
    {
        public Task<Job> EnqueueAsync(Job job, CancellationToken ct = default) => Task.FromResult(job);
        public Task<Job?> ClaimNextAsync(int maxPriority = int.MaxValue, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<Job?> ClaimNextByTypeAsync(JobType type, int maxPriority = int.MaxValue, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<Job?> GetByIdAsync(int jobId, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Job>> GetRunningAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Job>>([]);
        public Task<bool> HasActiveJobAsync(JobType type, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DateTime?> GetLastCompletedAtAsync(JobType type, CancellationToken ct = default) => Task.FromResult<DateTime?>(null);

        public Task<JobStatusSummary> GetStatusSummaryAsync(CancellationToken ct = default)
            => Task.FromResult(new JobStatusSummary(0, 0, 0, 0, 0, 0));
        public Task MarkInterruptedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RequeueInterruptedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Job?> PeekNextByTypeAsync(JobType type, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task RequeueInterruptedJobAsync(int jobId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
