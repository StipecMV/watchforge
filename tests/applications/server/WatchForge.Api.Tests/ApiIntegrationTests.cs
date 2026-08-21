using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Runner;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-8: E2E integračný test — API + Runner handlery + fake NVR + SQLite:
/// sync (backlog) → POST /requests → Analyze job (stiahne video, detekuje) →
/// ClipExtract job (klip + fotka) → GET /requests/{id} (completed + clipIds) →
/// GET /clips/{id} (video súbor). Vyžaduje ffmpeg.
/// </summary>
[NotInParallel]
public class ApiIntegrationTests : IAsyncDisposable
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";
    private const string ApiToken = "agent-secret";
    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-api-e2e-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-api-e2e-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _clipsDir = Path.Combine(
        Path.GetTempPath(), "watchforge-api-e2e-clips-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
    private Microsoft.Data.Sqlite.SqliteConnection? _connection;

    public ApiIntegrationTests()
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

    private async Task<byte[]> GenerateVideoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "watchforge-api-e2e-src-" + Guid.NewGuid().ToString("N") + ".mp4");
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

    private async Task SeedDbAsync(int nvrPort)
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', {nvrPort}, 'admin', '{_passwordEnvVar}');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1);
            """;
        await cmd.ExecuteNonQueryAsync();
        await new UserRepository(_connection).SeedDefaultUsersAsync();
    }

    private static WebApplicationFactory<WatchForge.Api.ApiEntryPoint> CreateFactory(string dbPath)
    {
        Environment.SetEnvironmentVariable(DbEnvKey, dbPath);
        Environment.SetEnvironmentVariable("WatchForge__Api__ApiToken", ApiToken);
        try
        {
            var factory = new WebApplicationFactory<WatchForge.Api.ApiEntryPoint>();
            _ = factory.Services;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable(DbEnvKey, null);
            Environment.SetEnvironmentVariable("WatchForge__Api__ApiToken", null);
        }
    }

    [Test]
    public async Task FullFlow_RequestToClip_EndToEnd()
    {
        if (!FfmpegAvailable()) return;

        // Given fake NVR s reálnym videom (pohyb — testsrc2)
        var videoBytes = await GenerateVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        await SeedDbAsync(server.Port);
        Func<WatchForge.DVRIP.Library.DvripClientOptions, WatchForge.DVRIP.Library.IDvripClient> clientFactory =
            opts => server.CreateClient(opts.Username, opts.Password);

        // ── 1. Sync backlog → recording v DB ──
        var synchronizer = new NvrSynchronizer(
            new NvrRepository(_connection!), new CameraRepository(_connection!),
            new RecordingRepository(_connection!), new SystemClock(), clientFactory);
        var sync = await synchronizer.SyncBacklogAsync(
            new DateTime(2026, 4, 5, 14, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));
        await Assert.That(sync.Inserted).IsEqualTo(1);

        // ── 2. POST /api/v1/requests → Analyze job ──
        await using var factory = CreateFactory(_dbPath);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Token", ApiToken);

        var create = await client.PostAsJsonAsync("/api/v1/requests", new
        {
            fromTime = "2026-04-05T14:00:00Z",
            toTime = "2026-04-05T16:00:00Z",
            cameraId = 1,
        });
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = created!["requestId"]!.GetValue<int>();

        // ── 3. Runner mini-loop: spracovať Analyze → (enqueue ClipExtract) → ClipExtract ──
        var jobRepo = new JobRepository(_connection!);
        var handlers = new IJobHandler[]
        {
            new WatchForge.Runner.DownloadJobHandler(
                new RecordingRepository(_connection!), new NvrRepository(_connection!), jobRepo,
                new SystemClock(),
                new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
                clientFactory),
            new WatchForge.Runner.AnalyzeJobHandler(
                new RecordingRepository(_connection!), new DetectionRepository(_connection!),
                new NvrRepository(_connection!), jobRepo, new SystemClock(),
                new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
                new WatchForge.MotionSentinel.Library.Detection.DetectionOptions(),
                clientFactory),
            new WatchForge.Runner.ClipExtractJobHandler(
                new RecordingRepository(_connection!), new DetectionRepository(_connection!),
                new ClipRepository(_connection!), new RequestRepository(_connection!),
                new NvrRepository(_connection!), new SystemClock(),
                new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
                new WatchForge.Runner.ClipOptions { ClipsDir = _clipsDir },
                clientFactory),
        };
        var executor = new WatchForge.Runner.JobExecutor(handlers, jobRepo);

        for (int i = 0; i < 10; i++)
        {
            var job = await jobRepo.ClaimNextByTypeAsync(JobType.Analyze)
                      ?? await jobRepo.ClaimNextByTypeAsync(JobType.ClipExtract);
            if (job is null) break;
            await executor.ExecuteAsync(job, CancellationToken.None);
        }

        // ── 4. GET /requests/{id} → completed + clipIds ──
        var status = await client.GetAsync($"/api/v1/requests/{requestId}");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var statusBody = await status.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(statusBody!["status"]?.GetValue<string>()).IsEqualTo("completed");
        var clipIds = statusBody!["clipIds"] as JsonArray;
        await Assert.That(clipIds).IsNotNull();
        await Assert.That(clipIds!.Count).IsGreaterThanOrEqualTo(2); // video + photo

        // ── 5. GET /clips/{id} → video súbor ──
        var videoClipId = clipIds![0]!.GetValue<int>();
        var clipResponse = await client.GetAsync($"/api/v1/clips/{videoClipId}");
        await Assert.That(clipResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var bytes = await clipResponse.Content.ReadAsByteArrayAsync();
        await Assert.That(bytes.Length).IsGreaterThan(1000);
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

    [Test]
    public async Task Export_SelectedRange_ProducesClip() // S20
    {
        if (!FfmpegAvailable()) return;

        // Given fake NVR s reálnym videom + recording v DB
        var videoBytes = await GenerateVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        await SeedDbAsync(server.Port);
        Func<WatchForge.DVRIP.Library.DvripClientOptions, WatchForge.DVRIP.Library.IDvripClient> clientFactory =
            opts => server.CreateClient(opts.Username, opts.Password);

        // Recording priamo do DB (bez sync — export potrebuje existujúci záznam)
        var recordingId = await new RecordingRepository(_connection!).InsertAsync(new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment", NvrFilename = RecordingName,
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = videoBytes.LongLength, Codec = "hevc",
            Availability = "available", AnalysisState = "completed",
        });

        await using var factory = CreateFactory(_dbPath);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Token", ApiToken);

        // ── 1. POST /api/v1/exports → 202 + requestId ──
        var create = await client.PostAsJsonAsync("/api/v1/exports", new
        {
            fromTime = "2026-04-05T15:00:01Z",
            toTime = "2026-04-05T15:00:04Z",
            cameraId = 1,
        });
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = created!["requestId"]!.GetValue<int>();

        // ── 2. Runner: spracovať ExportRange job ──
        var jobRepo = new JobRepository(_connection!);
        var handler = new WatchForge.Runner.ExportRangeJobHandler(
            new RecordingRepository(_connection!), new ClipRepository(_connection!),
            new RequestRepository(_connection!), new NvrRepository(_connection!), new SystemClock(),
            new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
            new WatchForge.Runner.ClipOptions { ClipsDir = _clipsDir },
            clientFactory);
        var executor = new WatchForge.Runner.JobExecutor([handler], jobRepo);

        for (int i = 0; i < 3; i++)
        {
            var job = await jobRepo.ClaimNextByTypeAsync(JobType.ExportRange);
            if (job is null) break;
            await executor.ExecuteAsync(job, CancellationToken.None);
        }

        // ── 3. GET /requests/{id} → completed + 1 clip ──
        var status = await client.GetAsync($"/api/v1/requests/{requestId}");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var statusBody = await status.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(statusBody!["status"]?.GetValue<string>()).IsEqualTo("completed");
        var clipIds = statusBody!["clipIds"] as JsonArray;
        await Assert.That(clipIds).IsNotNull();
        await Assert.That(clipIds!.Count).IsEqualTo(1);

        // ── 4. GET /clips/{id} → video súbor ──
        var clipId = clipIds![0]!.GetValue<int>();
        var clipResponse = await client.GetAsync($"/api/v1/clips/{clipId}");
        await Assert.That(clipResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var bytes = await clipResponse.Content.ReadAsByteArrayAsync();
        await Assert.That(bytes.Length).IsGreaterThan(1000);
    }
}
