using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using WatchForge.Api;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Runner;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Mcp.Tests;

/// <summary>
/// S7-3: E2E — MCP server (in-proc, HTTP transport) nad reálnym REST API
/// (WebApplicationFactory + SQLite + fake NVR): celý tok
/// sync → trigger_processing (MCP tool) → analyze → clip → search_motion → get_clip.
/// Vyžaduje ffmpeg.
/// </summary>
[NotInParallel]
public class McpServerIntegrationTests : IAsyncDisposable
{
    private const string ApiToken = "agent-secret";
    private const string RecordingName = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-mcp-e2e-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "watchforge-mcp-e2e-media-" + Guid.NewGuid().ToString("N"));
    private readonly string _clipsDir = Path.Combine(
        Path.GetTempPath(), "watchforge-mcp-e2e-clips-" + Guid.NewGuid().ToString("N"));
    private readonly string _passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
    private Microsoft.Data.Sqlite.SqliteConnection? _connection;
    private WebApplication? _mcpApp;

    public McpServerIntegrationTests()
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
        var path = Path.Combine(Path.GetTempPath(), "watchforge-mcp-src-" + Guid.NewGuid().ToString("N") + ".mp4");
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
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private async Task<string> CreateApiFactory(int nvrPort)
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
        return _dbPath;
    }

    /// <summary>Pošle raw JSON-RPC na MCP endpoint a vráti result (parsuje SSE data).</summary>
    private static async Task<JsonElement> RpcAsync(HttpClient client, int id, string method, object? parameters = null)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters ?? new { },
        };
        using var response = await client.PostAsJsonAsync("/mcp", request);
        var raw = await response.Content.ReadAsStringAsync();
        // Odpoveď môže byť SSE (event: message\ndata: {...}) alebo čistý JSON
        var payload = raw;
        if (raw.StartsWith("event:") && raw.Contains("data:"))
            payload = raw[(raw.IndexOf("data:") + 5)..].Trim();
        var element = JsonSerializer.Deserialize<JsonElement>(payload);
        if (!element.TryGetProperty("result", out _) && element.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"JSON-RPC error: {error.GetRawText()} (raw: {raw})");
        return element;
    }

    [Test]
    public async Task McpFlow_SearchMotion_TriggerProcessing_GetClip()
    {
        if (!FfmpegAvailable()) return;

        // ── Given: fake NVR + DB + API (WebApplicationFactory) ──
        var videoBytes = await GenerateVideoAsync();
        var entry = new FakeDvripServer.RecordingEntry(
            RecordingName,
            new DateTime(2026, 4, 5, 15, 0, 0),
            new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 0,
            Payload: videoBytes);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var dbPath = await CreateApiFactory(server.Port);
        Environment.SetEnvironmentVariable("WatchForge__Api__DbPath", dbPath);
        Environment.SetEnvironmentVariable("WatchForge__Api__ApiToken", ApiToken);
        WebApplicationFactory<ApiEntryPoint> factory;
        try { factory = new WebApplicationFactory<ApiEntryPoint>(); _ = factory.Services; }
        finally
        {
            Environment.SetEnvironmentVariable("WatchForge__Api__DbPath", null);
            Environment.SetEnvironmentVariable("WatchForge__Api__ApiToken", null);
        }
        await using var factoryHandle = factory;
        using var apiClient = factory.CreateClient();

        Func<WatchForge.DVRIP.Library.DvripClientOptions, WatchForge.DVRIP.Library.IDvripClient> clientFactory =
            opts => server.CreateClient(opts.Username, opts.Password);

        // ── Runner tok: sync → request (cez API) → analyze → clip ──
        var synchronizer = new NvrSynchronizer(
            new NvrRepository(_connection!), new CameraRepository(_connection!),
            new RecordingRepository(_connection!), new SystemClock(), clientFactory);
        await synchronizer.SyncBacklogAsync(new DateTime(2026, 4, 5, 14, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));

        apiClient.DefaultRequestHeaders.Add("X-Api-Token", ApiToken);
        var create = await apiClient.PostAsJsonAsync("/api/v1/requests", new
        {
            fromTime = "2026-04-05T14:00:00Z",
            toTime = "2026-04-05T16:00:00Z",
            cameraId = 1,
        });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = created.GetProperty("requestId").GetInt32();

        var jobRepo = new JobRepository(_connection!);
        var handlers = new IJobHandler[]
        {
            new WatchForge.Runner.AnalyzeJobHandler(
                new RecordingRepository(_connection!), new DetectionRepository(_connection!),
                new NvrRepository(_connection!), jobRepo, new SystemClock(),
                new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
                new WatchForge.MotionSentinel.Library.Detection.DetectionOptions(), clientFactory),
            new WatchForge.Runner.ClipExtractJobHandler(
                new RecordingRepository(_connection!), new DetectionRepository(_connection!),
                new ClipRepository(_connection!), new RequestRepository(_connection!),
                new NvrRepository(_connection!), new SystemClock(),
                new WatchForge.Runner.DownloadOptions { TempDir = _tempDir, OutputFormat = "mp4" },
                new WatchForge.Runner.ClipOptions { ClipsDir = _clipsDir }, clientFactory),
        };
        var executor = new WatchForge.Runner.JobExecutor(handlers, jobRepo);
        for (int i = 0; i < 10; i++)
        {
            var job = await jobRepo.ClaimNextByTypeAsync(JobType.Analyze)
                      ?? await jobRepo.ClaimNextByTypeAsync(JobType.ClipExtract);
            if (job is null) break;
            await executor.ExecuteAsync(job, CancellationToken.None);
        }

        // ── MCP server in-proc (HTTP transport) ──
        var mcpBuilder = WebApplication.CreateBuilder();
        mcpBuilder.Logging.ClearProviders();
        mcpBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var toolClient = new HttpClient(new NoRedirectHandler(apiClient))
        {
            BaseAddress = new Uri("http://api.test/"),
        };
        toolClient.DefaultRequestHeaders.Add("X-Api-Token", ApiToken);
        mcpBuilder.Services.AddSingleton(toolClient);
        mcpBuilder.Services.AddMcpServer().WithHttpTransport().WithTools<WatchForgeMcpTools>();
        _mcpApp = mcpBuilder.Build();
        _mcpApp.MapMcp("/mcp");
        await _mcpApp.StartAsync();
        var mcpBase = _mcpApp.Urls.First();

        using var rpcClient = new HttpClient { BaseAddress = new Uri(mcpBase) };
        rpcClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        rpcClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        // ── 1. initialize ──
        var init = await RpcAsync(rpcClient, 1, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" },
        });
        await Assert.That(init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString())
            .IsEqualTo(System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name);

        // Notifikácia initialized — MCP protokol vyžaduje pred tools/* volaniami
        _ = await rpcClient.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } });

        // ── 2. tools/list ──
        var tools = await RpcAsync(rpcClient, 2, "tools/list");
        var toolNames = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        await Assert.That(toolNames).Contains("search_motion");
        await Assert.That(toolNames).Contains("get_clip");
        await Assert.That(toolNames).Contains("trigger_processing");
        await Assert.That(toolNames).Contains("get_request_status");

        // ── 3. tools/call search_motion → detekcie (agent hľadá pohyb) ──
        var search = await RpcAsync(rpcClient, 3, "tools/call", new
        {
            name = "search_motion",
            arguments = new { fromTime = "2026-04-05T14:00:00Z", toTime = "2026-04-05T16:00:00Z" },
        });
        var searchText = search.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var searchJson = JsonSerializer.Deserialize<JsonElement>(searchText);
        await Assert.That(searchJson.GetProperty("count").GetInt32()).IsGreaterThanOrEqualTo(1);
        var detId = searchJson.GetProperty("detections")[0].GetProperty("d").GetInt32();
        await Assert.That(detId).IsGreaterThan(0);

        // ── 4. tools/call get_request_status → completed + clipIds ──
        var status = await RpcAsync(rpcClient, 4, "tools/call", new
        {
            name = "get_request_status",
            arguments = new { requestId },
        });
        var statusText = status.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var statusJson = JsonSerializer.Deserialize<JsonElement>(statusText);
        await Assert.That(statusJson.GetProperty("status").GetString()).IsEqualTo("completed");
        var clipIds = statusJson.GetProperty("clipIds").EnumerateArray().Select(e => e.GetInt32()).ToList();
        await Assert.That(clipIds.Count).IsGreaterThanOrEqualTo(2);

        // ── 5. tools/call get_clip → URL + metadáta ──
        var clip = await RpcAsync(rpcClient, 5, "tools/call", new
        {
            name = "get_clip",
            arguments = new { clipId = clipIds[0] },
        });
        var clipText = clip.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var clipJson = JsonSerializer.Deserialize<JsonElement>(clipText);
        await Assert.That(clipJson.GetProperty("kind").GetString()).IsEqualTo("video");
        await Assert.That(clipJson.GetProperty("url").GetString()).Contains("/api/v1/clips/");
    }

    /// <summary>Presmeruje požiadavky z MCP nástrojov na in-proc API factory klienta.</summary>
    private sealed class NoRedirectHandler(HttpClient apiClient) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var forwarded = new HttpRequestMessage(request.Method, request.RequestUri?.PathAndQuery)
            {
                Content = request.Content,
                Version = request.Version,
            };
            foreach (var header in request.Headers)
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return apiClient.SendAsync(forwarded, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(_passwordEnvVar, null);
        if (_mcpApp is not null) await _mcpApp.StopAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        if (Directory.Exists(_clipsDir)) Directory.Delete(_clipsDir, recursive: true);
    }
}
