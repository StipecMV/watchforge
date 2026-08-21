using Microsoft.Data.Sqlite;
using OpenCvSharp;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-4: AnalyzeJobHandler — motion pipeline na syntetickom videu (ffmpeg).
/// Ak ffmpeg nie je dostupný, testy sa preskočia (early return).
/// </summary>
public class AnalyzeJobHandlerTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-analyze-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-analyze-media-" + Guid.NewGuid().ToString("N"));
    private SqliteConnection? _connection;

    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    public AnalyzeJobHandlerTests()
    {
        Directory.CreateDirectory(_tempDir);
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

    private static async Task<Recording> InsertRecordingAsync(SqliteConnection connection, string analysisState = "queued")
    {
        var repo = new RecordingRepository(connection);
        var recording = new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment", NvrFilename = RecordingName,
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = 1024, Codec = "hevc",
            Availability = "available", AnalysisState = analysisState,
        };
        recording.RecordingId = await repo.InsertAsync(recording);
        return recording;
    }

    private AnalyzeJobHandler CreateHandler(SqliteConnection connection, string tempDir,
        IObjectDetector? objectDetector = null, IFaceRecognizer? faceRecognizer = null)
        => new(
            new RecordingRepository(connection),
            new DetectionRepository(connection),
            new NvrRepository(connection),
            new MockJobRepository(),
            new SystemClock(),
            new DownloadOptions { TempDir = tempDir },
            new DetectionOptions(),
            _ => throw new InvalidOperationException("No fake NVR configured for this test."),
            objectDetector,
            faceRecognizer);

    /// <summary>Vygeneruje testovacie video cez ffmpeg lavfi.</summary>
    private static async Task GenerateVideoAsync(string outputPath, string lavfiSource)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(lavfiSource);
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add(outputPath);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed to generate test video");
    }

    [Test]
    public async Task Analyze_MovingVideo_FindsDetectionsAndCleansUp()
    {
        if (!FfmpegAvailable()) return; // ffmpeg nie je na systéme — test sa preskočí

        // Given stiahnuté video s pohybom (testsrc2) v media cache
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "testsrc2=size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var handler = CreateHandler(connection, _tempDir);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When vykonáme analyze job
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then job je Completed, recording completed a detekcie sú v DB
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        var stored = await new RecordingRepository(connection).GetByIdAsync(recording.RecordingId);
        await Assert.That(stored!.AnalysisState).IsEqualTo("completed");
        await Assert.That(stored.AnalysisCompletedAt).IsNotNull();

        var detections = await new DetectionRepository(connection).QueryAsync(recording.CameraId, null, null, "motion", null);
        await Assert.That(detections).IsNotEmpty();
        await Assert.That(detections[0].AlgorithmVersion).IsEqualTo(AnalyzeJobHandler.AlgorithmVersion);
        await Assert.That(detections[0].Region.IsValid).IsTrue();

        // S22l: lokálne video sa NEmazá po analýze — drží sa pre rolling okná (60 min),
        // purge job maže staršie okná. A person_pending=false (žiadna osoba v testsrc2? žiadna).
        await Assert.That(File.Exists(videoPath)).IsTrue();
        await Assert.That(stored.PersonPending).IsFalse();
    }

    [Test]
    public async Task Analyze_WithWindowPayload_OffsetsTimestamps()
    {
        if (!FfmpegAvailable()) return;

        // Given lokálne video s pohybom (2 s) — FindDownloadedVideo ho nájde (bez trimu,
        // lebo súbor existuje), ale payload s oknom posunie timestampMs o offset
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "testsrc2=size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var handler = CreateHandler(connection, _tempDir);
        // S22n: okno začína 5 min po začiatku segmentu (recording begin = 2026-01-01T12:00:00)
        var windowStart = recording.BeginTime.AddMinutes(5);
        var windowEnd = windowStart.AddMinutes(15);
        var job = new Job
        {
            JobId = 1,
            RecordingId = recording.RecordingId,
            Type = JobType.Analyze,
            Priority = 10,
            Status = JobStatus.Running,
            Payload = $"{{\"windowStart\":\"{windowStart:O}\",\"windowEnd\":\"{windowEnd:O}\"}}",
        };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then detekcie majú timestampMs posunuté o +5 min (300 000 ms) — čas v segmente,
        // nie relatívne k oknu
        var detections = await new DetectionRepository(connection).QueryAsync(recording.CameraId, null, null, "motion", null);
        await Assert.That(detections).IsNotEmpty();
        await Assert.That(detections.All(d => d.TimestampMs >= 300_000)).IsTrue();
    }

    [Test]
    public async Task Analyze_StaticVideo_CompletesWithZeroDetections()
    {
        if (!FfmpegAvailable()) return;

        // Given statické video (čierna obrazovka — žiadny pohyb)
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "color=c=black:size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var handler = CreateHandler(connection, _tempDir);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then completed, žiadne detekcie, video ostáva (S22l — rolling okná)
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        var detections = await new DetectionRepository(connection).QueryAsync(recording.CameraId, null, null, "motion", null);
        await Assert.That(detections).IsEmpty();
        await Assert.That(File.Exists(videoPath)).IsTrue();
    }

    [Test]
    public async Task Analyze_StaticVideo_PersonDetector_NotCalled()
    {
        if (!FfmpegAvailable()) return;

        // Given statické video (žiadny pohyb) + stub person detektora
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "color=c=black:size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var personDetector = new StubObjectDetector();
        var handler = CreateHandler(connection, _tempDir, personDetector);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then FR-04: bez pohybu sa person detekcia NESPÚŠŤA a žiadne person detekcie nie sú v DB
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(personDetector.Calls).IsEqualTo(0);
        var personDetections = await new DetectionRepository(connection)
            .QueryAsync(recording.CameraId, null, null, "person", null);
        await Assert.That(personDetections).IsEmpty();
    }

    [Test]
    public async Task Analyze_MovingVideo_RunsPersonDetector_AndPersistsPersonDetections()
    {
        if (!FfmpegAvailable()) return;

        // Given video s pohybom + stub person detektora, ktorý vždy vráti osobu
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "testsrc2=size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var personDetector = new StubObjectDetector();
        var handler = CreateHandler(connection, _tempDir, personDetector);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then FR-04: person detektor bežal (pohyb bol) a person detekcie sú v DB
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(personDetector.Calls).IsGreaterThan(0);

        var personDetections = await new DetectionRepository(connection)
            .QueryAsync(recording.CameraId, null, null, "person", null);
        await Assert.That(personDetections).IsNotEmpty();
        await Assert.That(personDetections[0].ObjectClass).IsEqualTo("person");
        await Assert.That(personDetections[0].AlgorithmVersion).IsEqualTo(AnalyzeJobHandler.PersonAlgorithmVersion);
        await Assert.That(personDetections[0].Confidence).IsEqualTo(0.9f);
        await Assert.That(personDetections[0].Region.IsValid).IsTrue();

        // And motion detekcie stále existujú (person je pridaná vrstva)
        var motionDetections = await new DetectionRepository(connection)
            .QueryAsync(recording.CameraId, null, null, "motion", null);
        await Assert.That(motionDetections).IsNotEmpty();
    }

    [Test]
    public async Task Analyze_MovingVideo_RunsFaceRecognizer_OnlyAfterPerson_AndPersistsFaceDetections()
    {
        if (!FfmpegAvailable()) return;

        // Given video s pohybom + stub person detektora (vždy osoba) + stub face recognizera
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "testsrc2=size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var personDetector = new StubObjectDetector();
        var faceRecognizer = new StubFaceRecognizer();
        var handler = CreateHandler(connection, _tempDir, personDetector, faceRecognizer);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then FR-05: face recognizer bežal (person bola) a face detekcie sú v DB
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(faceRecognizer.Calls).IsGreaterThan(0);
        await Assert.That(faceRecognizer.Calls).IsEqualTo(personDetector.Calls); // face len po person

        var faceDetections = await new DetectionRepository(connection)
            .QueryAsync(recording.CameraId, null, null, "face", null);
        await Assert.That(faceDetections).IsNotEmpty();
        await Assert.That(faceDetections[0].ObjectClass).IsEqualTo("admin"); // identity meno
        await Assert.That(faceDetections[0].AlgorithmVersion).IsEqualTo(AnalyzeJobHandler.FaceAlgorithmVersion);
        await Assert.That(faceDetections[0].Confidence).IsEqualTo(0.85f);
        await Assert.That(faceDetections[0].Region.IsValid).IsTrue();
    }

    [Test]
    public async Task Analyze_StaticVideo_FaceRecognizer_NotCalled()
    {
        if (!FfmpegAvailable()) return;

        // Given statické video (žiadny pohyb → žiadna person → žiadna face)
        var videoPath = Path.Combine(_tempDir, RecordingName.Replace(".mkv", ".mp4"));
        await GenerateVideoAsync(videoPath, "color=c=black:size=320x240:rate=10:duration=2");

        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var personDetector = new StubObjectDetector();
        var faceRecognizer = new StubFaceRecognizer();
        var handler = CreateHandler(connection, _tempDir, personDetector, faceRecognizer);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When
        var executor = new JobExecutor([handler], new MockJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then FR-05: bez pohybu ani person, ani face recognizer nebežal
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        await Assert.That(personDetector.Calls).IsEqualTo(0);
        await Assert.That(faceRecognizer.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_MissingNvrPassword_Throws()
    {
        // Given recording bez stiahnutého videa a NVR bez hesla v env (analyza si video stiahne sama)
        var connection = await DbAsync();
        var recording = await InsertRecordingAsync(connection);
        var handler = CreateHandler(connection, _tempDir);
        var job = new Job { JobId = 1, RecordingId = recording.RecordingId, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        // When/Then chýbajúce heslo NVR → InvalidOperationException
        var ex = await Assert.That(() => handler.ExecuteAsync(job, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("Password env");
    }

    [Test]
    public async Task Analyze_MissingRecording_Throws()
    {
        var connection = await DbAsync();
        var handler = CreateHandler(connection, _tempDir);
        var job = new Job { JobId = 1, RecordingId = 999, Type = JobType.Analyze, Priority = 10, Status = JobStatus.Running };

        await Assert.That(() => handler.ExecuteAsync(job, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
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

    /// <summary>S8-2: stub person detektora — vždy vráti jednu osobu, počíta volania.</summary>
    private sealed class StubObjectDetector : IObjectDetector
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
            VideoFrame currentFrame, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ObjectDetection>>(
            [
                new ObjectDetection
                {
                    Region      = new NormalizedRegion(0.2f, 0.3f, 0.4f, 0.5f),
                    ObjectClass = "person",
                    Confidence  = 0.9f,
                }
            ]);
        }
    }

    /// <summary>S8-3: stub face recognizera — vždy vráti jednu tvár (identity "admin"), počíta volania.</summary>
    private sealed class StubFaceRecognizer : IFaceRecognizer
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(
            VideoFrame currentFrame, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<FaceDetection>>(
            [
                new FaceDetection
                {
                    Region       = new NormalizedRegion(0.25f, 0.35f, 0.2f, 0.2f),
                    IdentityId   = 1,
                    IdentityName = "admin",
                    Confidence   = 0.85f,
                }
            ]);
        }

        public Task<int> LearnAsync(Mat faceCrop, int? identityId, string identityName, int? detectionId = null, CancellationToken ct = default)
            => Task.FromResult(-1);

        public Task LoadStateAsync(IFaceRepository faceRepository, IIdentityRepository? identityRepository = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
