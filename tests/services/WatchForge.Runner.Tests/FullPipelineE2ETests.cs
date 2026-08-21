using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-8: E2E integračný test — celý tok: sync (backlog z fake NVR) →
/// download (reálny MP4 payload) → analyze (motion detekcia) → detekcie v DB.
/// Vyžaduje ffmpeg (test sa preskočí, ak chýba).
/// </summary>
public class FullPipelineE2ETests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-e2e-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-e2e-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
    private SqliteConnection? _connection;

    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    public FullPipelineE2ETests()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, "secret");
        Directory.CreateDirectory(_tempDir);
    }

    private async Task<SqliteConnection> DbAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', 34567, 'admin', '{_passwordEnvVar}');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1);
            """;
        await cmd.ExecuteNonQueryAsync();
        return _connection;
    }

    private static async Task<byte[]> GenerateTestVideoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "watchforge-e2e-src-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc2=size=320x240:rate=10:duration=2");
            psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add(path);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed");
            return await File.ReadAllBytesAsync(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task SyncDownloadAnalyze_FullFlow_WritesDetections()
    {
        // Given ffmpeg dostupný
        if (!FfmpegAvailable()) return;

        // Given fake NVR s reálnym videom (testsrc2 — pohyb) ako nahrávka
        var videoBytes = await GenerateTestVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var connection = await DbAsync();
        var recordingRepo = new RecordingRepository(connection);
        var from = new DateTime(2026, 4, 5, 14, 0, 0);
        var to = new DateTime(2026, 4, 5, 16, 0, 0);
        Func<WatchForge.DVRIP.Library.DvripClientOptions, WatchForge.DVRIP.Library.IDvripClient> clientFactory =
            opts => server.CreateClient(opts.Username, opts.Password);

        // ── 1. Sync: backlog z NVR ──
        var synchronizer = new NvrSynchronizer(
            new NvrRepository(connection), new CameraRepository(connection), recordingRepo,
            new SystemClock(), clientFactory);
        var syncResult = await synchronizer.SyncBacklogAsync(from, to);
        await Assert.That(syncResult.Inserted).IsEqualTo(1);
        var recording = (await recordingRepo.QueryAsync(1, from, to, null)).Single();
        await Assert.That(recording.AnalysisState).IsEqualTo("queued");

        // ── 2. Download: z NVR do media cache ──
        var downloadOptions = new DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" };
        var downloadHandler = new DownloadJobHandler(
            recordingRepo, new NvrRepository(connection), new StubJobRepository(),
            new SystemClock(), downloadOptions, clientFactory);
        var downloadJob = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Download, Priority = 10, Status = JobStatus.Running };
        var executor = new JobExecutor([downloadHandler], new StubJobRepository());
        await executor.ExecuteAsync(downloadJob, CancellationToken.None);
        await Assert.That(downloadJob.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(Directory.GetFiles(_tempDir)).IsNotEmpty();

        // ── 3. Analyze: motion detekcia ──
        var analyzeHandler = new AnalyzeJobHandler(
            recordingRepo, new DetectionRepository(connection), new NvrRepository(connection),
            new StubJobRepository(), new SystemClock(),
            downloadOptions, new WatchForge.MotionSentinel.Library.Detection.DetectionOptions(),
            opts => server.CreateClient(opts.Username, opts.Password));
        var analyzeJob = new Job { JobId = 2, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };
        var analyzeExecutor = new JobExecutor([analyzeHandler], new StubJobRepository());
        await analyzeExecutor.ExecuteAsync(analyzeJob, CancellationToken.None);

        // Then job completed, recording completed, detekcie v DB (pohyb vo videu)
        await Assert.That(analyzeJob.Status).IsEqualTo(JobStatus.Completed);
        var stored = await recordingRepo.GetByIdAsync(recording.RecordingId);
        await Assert.That(stored!.AnalysisState).IsEqualTo("completed");
        await Assert.That(stored.AnalysisCompletedAt).IsNotNull();

        var detections = await new DetectionRepository(connection).QueryAsync(1, null, null, "motion", null);
        await Assert.That(detections).IsNotEmpty();
        await Assert.That(detections.All(d => d.Region.IsValid)).IsTrue();

        // S22l: lokálne video ostáva po analýze (rolling okná — purge maže staršie okná)
        await Assert.That(Directory.GetFiles(_tempDir)).IsNotEmpty();
    }

    private static bool FfmpegAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(dir, "ffmpeg"))) return true;
            if (File.Exists(Path.Combine(dir, "ffmpeg.exe"))) return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, null);
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class StubJobRepository : IJobRepository
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
