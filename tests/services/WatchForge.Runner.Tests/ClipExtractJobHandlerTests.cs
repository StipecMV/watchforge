using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S5-5: ClipExtractJobHandler — generovanie klipov (ffmpeg cut) + fotky
/// s detekčným obdĺžnikom (OpenCV), CLIPS záznamy, request completed.
/// Vyžaduje ffmpeg (test sa preskočí, ak chýba).
/// </summary>
public class ClipExtractJobHandlerTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-clip-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-clip-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _clipsDir = Path.Combine(
        Path.GetTempPath(), "watchforge-clip-out-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
    private SqliteConnection? _connection;

    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    public ClipExtractJobHandlerTests()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, "secret");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_clipsDir);
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

    private static async Task<byte[]> GenerateTestVideoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "watchforge-clip-src-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc2=size=320x240:rate=10:duration=4");
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

    private async Task<SqliteConnection> DbAsync(byte[] videoBytes)
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', 34567, 'admin', '{_passwordEnvVar}');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1);
            INSERT INTO REQUESTS (source, requester, query, from_time, to_time, status, estimate)
            VALUES ('webui', 'tester', 'motion test', '2026-04-05T15:00:00Z', '2026-04-05T15:15:00Z', 'queued', '~1 min');
            """;
        await cmd.ExecuteNonQueryAsync();

        var recordingRepo = new RecordingRepository(_connection);
        var recording = await recordingRepo.InsertAsync(new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment", NvrFilename = RecordingName,
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = videoBytes.LongLength, Codec = "hevc",
            Availability = "available", AnalysisState = "completed",
        });

        var detectionRepo = new DetectionRepository(_connection);
        await detectionRepo.InsertAsync(new Detection
        {
            RecordingId = recording, CameraId = 1, DetectionType = "motion",
            TimestampMs = 1500, DurationMs = 500, AlgorithmVersion = "optical-flow-1",
            Region = new NormalizedRegion(0.2f, 0.3f, 0.4f, 0.3f), Intensity = 0.8f,
        });
        return _connection;
    }

    [Test]
    public async Task Extract_MotionDetection_CreatesVideoAndPhotoClips()
    {
        if (!FfmpegAvailable()) return;

        // Given fake NVR s reálnym videom + recording + detekcia + request
        var videoBytes = await GenerateTestVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var connection = await DbAsync(videoBytes);
        Func<WatchForge.DVRIP.Library.DvripClientOptions, WatchForge.DVRIP.Library.IDvripClient> clientFactory =
            opts => server.CreateClient(opts.Username, opts.Password);

        var handler = new ClipExtractJobHandler(
            new RecordingRepository(connection),
            new DetectionRepository(connection),
            new ClipRepository(connection),
            new RequestRepository(connection),
            new NvrRepository(connection),
            new SystemClock(),
            new DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
            new ClipOptions { ClipsDir = _clipsDir },
            clientFactory);

        var job = new Job
        {
            JobId = 1, RecordingId = 1, RequestId = 1,
            Type = JobType.ClipExtract, Priority = JobPriority.WebUi, Source = JobSource.WebUi,
            Status = JobStatus.Running,
        };

        // When vykonáme clip extract
        var executor = new JobExecutor([handler], new StubJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then job completed, request completed, 2 klipy (video + photo) v DB aj na disku
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(job.Error).Contains("clips=2");

        var request = await new RequestRepository(connection).GetByIdAsync(1);
        await Assert.That(request!.Status).IsEqualTo("completed");

        var clips = await new ClipRepository(connection).GetByRequestAsync(1);
        await Assert.That(clips).Count().IsEqualTo(2);
        var video = clips.First(c => c.Kind == "video");
        var photo = clips.First(c => c.Kind == "photo");
        await Assert.That(File.Exists(video.FilePath)).IsTrue();
        await Assert.That(File.Exists(photo.FilePath)).IsTrue();
        await Assert.That(new FileInfo(video.FilePath).Length).IsGreaterThan(1000);
        await Assert.That(new FileInfo(photo.FilePath).Length).IsGreaterThan(1000);
        await Assert.That(video.ExpiresAt).IsAfter(DateTime.UtcNow);
    }

    [Test]
    public async Task Extract_NoDetections_CompletesWithoutClips()
    {
        if (!FfmpegAvailable()) return;

        // Given fake NVR + recording BEZ detekcií + request
        var videoBytes = await GenerateTestVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var connection = await DbAsync(videoBytes);
        // Vymazať detekcie (žiadne udalosti)
        await new DetectionRepository(connection).DeleteForRecordingAsync(1);

        var handler = new ClipExtractJobHandler(
            new RecordingRepository(connection),
            new DetectionRepository(connection),
            new ClipRepository(connection),
            new RequestRepository(connection),
            new NvrRepository(connection),
            new SystemClock(),
            new DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
            new ClipOptions { ClipsDir = _clipsDir },
            opts => server.CreateClient(opts.Username, opts.Password));

        var job = new Job
        {
            JobId = 1, RecordingId = 1, RequestId = 1,
            Type = JobType.ClipExtract, Priority = JobPriority.WebUi, Source = JobSource.WebUi,
            Status = JobStatus.Running,
        };

        var executor = new JobExecutor([handler], new StubJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then completed bez klipov, request completed
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(job.Error).Contains("clips=0");
        var clips = await new ClipRepository(connection).GetByRequestAsync(1);
        await Assert.That(clips).IsEmpty();
    }

    [Test]
    public async Task ComputeRange_ClampsToRecordingBounds()
    {
        // Given detekcia na začiatku a na konci záznamu
        var detectionStart = new Detection { TimestampMs = 100, DurationMs = 500 };
        var detectionEnd = new Detection { TimestampMs = 890_000, DurationMs = 500 };

        // When range s kontextom ±15s na 900s zázname
        var (start1, len1) = ClipExtractJobHandler.ComputeRange(detectionStart, 900_000, 15, 15);
        var (start2, len2) = ClipExtractJobHandler.ComputeRange(detectionEnd, 900_000, 15, 15);

        // Then začiatok je orezený na 0, koniec na dĺžku záznamu
        await Assert.That(start1).IsEqualTo(0);
        await Assert.That(len1).IsEqualTo(15_600);
        await Assert.That(start2).IsEqualTo(875_000);
        await Assert.That(len2).IsEqualTo(25_000);
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, null);
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        if (Directory.Exists(_clipsDir)) Directory.Delete(_clipsDir, recursive: true);
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
