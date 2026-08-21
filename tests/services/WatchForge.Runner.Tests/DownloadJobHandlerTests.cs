using Microsoft.Data.Sqlite;
using Moq;
using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-3: DownloadJobHandler — download z NVR, .downloading cleanup, retry,
/// max retries, analysis_state prechod.
/// </summary>
public class DownloadJobHandlerTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-dl-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-dl-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
    private SqliteConnection? _connection;

    public DownloadJobHandlerTests()
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

    private static DownloadOptions TestOptions(string tempDir) => new()
    {
        TempDir = tempDir,
        OutputFormat = "mkv",
        MaxRetries = 3,
        StaleDownloadingAge = TimeSpan.FromHours(1),
    };

    private static async Task<Recording> InsertRecordingAsync(SqliteConnection connection, string filename, long sizeBytes = 32 * 1024)
    {
        var repo = new RecordingRepository(connection);
        var recording = new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment", NvrFilename = filename,
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = sizeBytes, Codec = "hevc",
            Availability = "available", AnalysisState = "queued",
        };
        recording.RecordingId = await repo.InsertAsync(recording);
        return recording;
    }

    [Test]
    public async Task Download_Ok_MarksRecordingDownloadedAndCreatesFile()
    {
        // Given fake NVR s nahrávkou a recording v DB
        var entry = new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 15, 15, 0), 32);
        using var server = new FakeDvripServer(recordings: [entry], fillByte: 0xCC);
        await server.StartAsync();

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection, entry.FileName);
        var handler = new DownloadJobHandler(
            new RecordingRepository(connection),
            new NvrRepository(connection),
            new Mock<IJobRepository>().Object,
            new SystemClock(),
            TestOptions(_tempDir),
            opts => server.CreateClient(opts.Username, opts.Password));

        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Download, Priority = 10, Status = JobStatus.Running };

        // When vykonáme download job cez executor (ten nastavuje Completed)
        var executor = new JobExecutor([handler], new Mock<IJobRepository>().Object);
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then job je Completed, recording je downloaded a súbor existuje
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(job.Progress).IsEqualTo(100);
        var stored = await new RecordingRepository(connection).GetByIdAsync(recording.RecordingId);
        await Assert.That(stored!.AnalysisState).IsEqualTo("downloaded");

        // Výstup: ak ffmpeg nie je dostupný alebo zlyhá na syntetických dátach,
        // zostane raw súbor (.downloading); cieľom je, že sa niečo stiahlo.
        var output = Directory.GetFiles(_tempDir).SingleOrDefault();
        await Assert.That(output).IsNotNull();
        var bytes = await File.ReadAllBytesAsync(output!);
        await Assert.That(bytes.Length).IsEqualTo(32 * 1024);
    }

    [Test]
    public async Task Download_Failure_RequeuesJobWithAttempt()
    {
        // Given NVR, ktorý pri downloade zlyhá (mock client)
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection, "[Ch0]_2026-04-05_15.00.00-15.15.mkv");

        var failingClient = new Mock<IDvripClient>();
        failingClient.Setup(c => c.LoginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LoginResult());
        failingClient.Setup(c => c.DownloadFileAsync(It.IsAny<NvrFile>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IProgress<long>?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("NVR unreachable"));

        var handler = new DownloadJobHandler(
            new RecordingRepository(connection),
            new NvrRepository(connection),
            new Mock<IJobRepository>().Object,
            new SystemClock(),
            TestOptions(_tempDir),
            _ => failingClient.Object);

        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Download, Priority = 10, Status = JobStatus.Running };

        // When download zlyhá (prvý pokus)
        await handler.ExecuteAsync(job, CancellationToken.None);

        // Then job sa vráti do frontu s attempts=1 a chybovou správou
        await Assert.That(job.Status).IsEqualTo(JobStatus.Queued);
        await Assert.That(job.Attempts).IsEqualTo(1);
        await Assert.That(job.Error).Contains("attempt 1/3");
    }

    [Test]
    public async Task Download_MaxRetriesExceeded_Throws()
    {
        // Given job, ktorý už mal 2 pokusy (max 3)
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection, "[Ch0]_2026-04-05_15.00.00-15.15.mkv");

        var failingClient = new Mock<IDvripClient>();
        failingClient.Setup(c => c.LoginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LoginResult());
        failingClient.Setup(c => c.DownloadFileAsync(It.IsAny<NvrFile>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IProgress<long>?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("NVR unreachable"));

        var handler = new DownloadJobHandler(
            new RecordingRepository(connection),
            new NvrRepository(connection),
            new Mock<IJobRepository>().Object,
            new SystemClock(),
            TestOptions(_tempDir),
            _ => failingClient.Object);

        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Download, Priority = 10, Attempts = 2 };

        // When download zlyhá na poslednom pokuse
        // Then výnimka preletí von (executor ju zmení na Failed)
        var ex = await Assert.That(() => handler.ExecuteAsync(job, CancellationToken.None))
            .Throws<IOException>();
        await Assert.That(ex!.Message).Contains("NVR unreachable");
    }

    [Test]
    public async Task Download_MissingRecording_Throws()
    {
        // Given DB bez záznamu a job na neexistujúci recording
        var connection = await DbAsync();
        var handler = new DownloadJobHandler(
            new RecordingRepository(connection),
            new NvrRepository(connection),
            new Mock<IJobRepository>().Object,
            new SystemClock(),
            TestOptions(_tempDir),
            _ => new Mock<IDvripClient>().Object);

        var job = new Job { JobId = 1, RecordingId = 999, Type = JobType.Download, Priority = 10 };

        // When/Then chýbajúci recording → InvalidOperationException
        await Assert.That(() => handler.ExecuteAsync(job, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Cleanup_StaleDownloadingFile_IsDeleted()
    {
        // Given starý .downloading súbor v tempDir (starší ako 1h)
        var staleFile = Path.Combine(_tempDir, "[Ch0]_2026-04-05_15.00.00-15.15.mkv.downloading");
        await File.WriteAllBytesAsync(staleFile, [1, 2, 3]);
        File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddHours(-2));

        var entry = new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_16.00.00-16.15.mkv",
            new DateTime(2026, 4, 5, 16, 0, 0), new DateTime(2026, 4, 5, 16, 15, 0), 16);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection, entry.FileName, sizeBytes: 16 * 1024);
        var handler = new DownloadJobHandler(
            new RecordingRepository(connection),
            new NvrRepository(connection),
            new Mock<IJobRepository>().Object,
            new SystemClock(),
            TestOptions(_tempDir),
            opts => server.CreateClient(opts.Username, opts.Password));

        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Download, Priority = 10, Status = JobStatus.Running };

        // When vykonáme download (handler čistí stale .downloading)
        await handler.ExecuteAsync(job, CancellationToken.None);

        // Then starý .downloading je preč
        await Assert.That(File.Exists(staleFile)).IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, null);
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
