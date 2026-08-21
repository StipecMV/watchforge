using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S20: ExportRangeJobHandler — export vybranej časovej úsečky (dôkazový klip):
/// vystrihne PRESNÝ interval [request.FromTime, request.ToTime] zo záznamu
/// (ffmpeg cut), uloží MP4 clip, request → completed. Vyžaduje ffmpeg.
/// </summary>
public class ExportRangeJobHandlerTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-export-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-export-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _clipsDir = Path.Combine(
        Path.GetTempPath(), "watchforge-export-out-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_EXPORT_PASSWORD_" + Guid.NewGuid().ToString("N");
    private SqliteConnection? _connection;

    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    public ExportRangeJobHandlerTests()
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
        var path = Path.Combine(Path.GetTempPath(), "watchforge-export-src-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc2=size=320x240:rate=10:duration=6");
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

    private async Task<SqliteConnection> DbAsync(byte[] videoBytes, DateTime from, DateTime to)
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', 34567, 'admin', '{_passwordEnvVar}');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1);
            INSERT INTO REQUESTS (source, requester, query, from_time, to_time, status, estimate)
            VALUES ('webui', 'tester', 'export test', '{from:O}', '{to:O}', 'queued', '~1 min');
            """;
        await cmd.ExecuteNonQueryAsync();

        var recordingRepo = new RecordingRepository(_connection);
        await recordingRepo.InsertAsync(new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment", NvrFilename = RecordingName,
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = videoBytes.LongLength, Codec = "hevc",
            Availability = "available", AnalysisState = "completed",
        });
        return _connection;
    }

    [Test]
    public async Task Export_SelectedRange_CreatesMp4ClipAndCompletesRequest()
    {
        if (!FfmpegAvailable()) return;

        // Given: 6s video na NVR, recording 15:00–15:15, request export 15:00:01 – 15:00:04
        var videoBytes = await GenerateTestVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var from = new DateTime(2026, 4, 5, 15, 0, 1, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 5, 15, 0, 4, DateTimeKind.Utc);
        var connection = await DbAsync(videoBytes, from, to);

        var handler = new ExportRangeJobHandler(
            new RecordingRepository(connection),
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
            Type = JobType.ExportRange, Priority = JobPriority.WebUi, Source = JobSource.WebUi,
            Status = JobStatus.Running,
        };

        // When
        var executor = new JobExecutor([handler], new StubJobRepository());
        await executor.ExecuteAsync(job, CancellationToken.None);

        // Then: job completed, request completed, 1 MP4 clip (video) s ~3s trvaním
        await Assert.That(job.Status).IsEqualTo(JobStatus.Completed);
        var request = await new RequestRepository(connection).GetByIdAsync(1);
        await Assert.That(request!.Status).IsEqualTo("completed");

        var clips = await new ClipRepository(connection).GetByRequestAsync(1);
        await Assert.That(clips).Count().IsEqualTo(1);
        var clip = clips[0];
        await Assert.That(clip.Kind).IsEqualTo("video");
        await Assert.That(File.Exists(clip.FilePath)).IsTrue();
        await Assert.That(new FileInfo(clip.FilePath).Length).IsGreaterThan(1000);
        await Assert.That(clip.RangeStart).IsEqualTo(from);
        await Assert.That(clip.RangeEnd).IsEqualTo(to);
        await Assert.That(clip.ExpiresAt).IsAfter(DateTime.UtcNow);

        // Trvanie klipu ≈ 3 s (±1 s vzhľadom na kľúčové snímky)
        var duration = await ProbeDurationAsync(clip.FilePath);
        await Assert.That(duration).IsGreaterThan(1.5);
        await Assert.That(duration).IsLessThan(4.5);
    }

    private static async Task<double> ProbeDurationAsync(string filePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format=duration");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("csv=p=0");
        psi.ArgumentList.Add(filePath);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return double.TryParse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        foreach (var dir in new[] { _tempDir, _clipsDir })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private sealed class StubJobRepository : IJobRepository
    {
        public Task<Job> EnqueueAsync(Job job, CancellationToken ct = default) => Task.FromResult(job);
        public Task<Job?> ClaimNextAsync(int maxPriority = int.MaxValue, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<Job?> ClaimNextByTypeAsync(JobType type, int maxPriority = int.MaxValue, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<Job?> PeekNextByTypeAsync(JobType type, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<Job?> GetByIdAsync(int jobId, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Job>> GetRunningAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Job>>([]);
        public Task RequeueInterruptedJobAsync(int jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasActiveJobAsync(JobType type, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DateTime?> GetLastCompletedAtAsync(JobType type, CancellationToken ct = default) => Task.FromResult<DateTime?>(null);

        public Task<JobStatusSummary> GetStatusSummaryAsync(CancellationToken ct = default) =>
            Task.FromResult(new JobStatusSummary(0, 0, 0, 0, 0, 0));
        public Task MarkInterruptedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RequeueInterruptedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
