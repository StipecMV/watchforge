using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-4: POST /api/v1/requests + GET /api/v1/requests/{id} — požiadavky,
/// prioritné joby, odhad, stav.
/// </summary>
[NotInParallel]
public class RequestTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-req-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            await new UserRepository(connection).SeedDefaultUsersAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 0, 'Dvor', 'camera', 1);
                """;
            await cmd.ExecuteNonQueryAsync();

            var recordings = new RecordingRepository(connection);
            // Hotový záznam → ClipExtract job
            await recordings.InsertAsync(new Recording
            {
                NvrId = 1, CameraId = 1, SourceType = "segment",
                NvrFilename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
                EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
                DurationSec = 900, SizeBytes = 1024, Availability = "available",
                AnalysisState = "completed",
            });
            // Nespracovaný záznam → Analyze job
            await recordings.InsertAsync(new Recording
            {
                NvrId = 1, CameraId = 1, SourceType = "segment",
                NvrFilename = "[Ch0]_2026-04-05_16.00.00-16.15.mkv",
                BeginTime = new DateTime(2026, 4, 5, 16, 0, 0),
                EndTime = new DateTime(2026, 4, 5, 16, 15, 0),
                DurationSec = 900, SizeBytes = 1024, Availability = "available",
                AnalysisState = "queued",
            });
        }
        return dbPath;
    }

    private static WebApplicationFactory<WatchForge.Api.ApiEntryPoint> CreateFactory(string dbPath, string? apiToken = null)
    {
        Environment.SetEnvironmentVariable(DbEnvKey, dbPath);
        if (apiToken is not null)
            Environment.SetEnvironmentVariable("WatchForge__Api__ApiToken", apiToken);
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

    private static void CleanupDb(string dbPath)
    {
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Test]
    public async Task Create_ValidRequest_Returns202WithEstimateAndJobs()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PostAsJsonAsync("/api/v1/requests", new
            {
                fromTime = "2026-04-05T14:00:00Z",
                toTime = "2026-04-05T17:00:00Z",
                cameraId = 1,
                detectionTypeFilter = "motion",
            });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["requestId"]!.GetValue<int>()).IsGreaterThan(0);
            await Assert.That(body!["status"]?.GetValue<string>()).IsEqualTo("queued");
            await Assert.That(body!["estimate"]?.GetValue<string>()).Contains("2 záznamov");

            // S22m: GetStatusSummaryAsync teraz počíta LEN Analyze joby (purge/sync/clip sú pozadie).
            // Request vytvorí 1 Analyze (queued recording) + 1 ClipExtract (completed recording).
            await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);
            var jobs = await new JobRepository(connection).GetStatusSummaryAsync();
            await Assert.That(jobs.Queued).IsEqualTo(1);
            // ClipExtract job existuje (overí sa priamo — summary ho nepočíta)
            var clipCmd = connection.CreateCommand();
            clipCmd.CommandText = "SELECT COUNT(*) FROM JOBS WHERE type = 'clipextract';";
            var clipCount = Convert.ToInt32(await clipCmd.ExecuteScalarAsync());
            await Assert.That(clipCount).IsEqualTo(1);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Create_InvalidRange_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PostAsJsonAsync("/api/v1/requests", new
            {
                fromTime = "2026-04-05T17:00:00Z",
                toTime = "2026-04-05T14:00:00Z", // to pred from
            });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Create_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/requests", new
            {
                fromTime = "2026-04-05T14:00:00Z",
                toTime = "2026-04-05T17:00:00Z",
            });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task GetById_ReturnsStatusAndClipIds()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            // Vytvor request + klip preň
            var create = await client.PostAsJsonAsync("/api/v1/requests", new
            {
                fromTime = "2026-04-05T14:00:00Z",
                toTime = "2026-04-05T17:00:00Z",
            });
            var created = await create.Content.ReadFromJsonAsync<JsonObject>();
            var requestId = created!["requestId"]!.GetValue<int>();

            await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
            {
                var clips = new ClipRepository(connection);
                await clips.InsertAsync(new Clip
                {
                    RequestId = requestId, Kind = "video",
                    RangeStart = new DateTime(2026, 4, 5, 15, 0, 0),
                    RangeEnd = new DateTime(2026, 4, 5, 15, 0, 30),
                    FilePath = "/tmp/clip.mp4", SizeBytes = 123,
                    ExpiresAt = DateTime.UtcNow.AddDays(1),
                });
            }

            var response = await client.GetAsync($"/api/v1/requests/{requestId}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["requestId"]?.GetValue<int>()).IsEqualTo(requestId);
            await Assert.That(body!["status"]?.GetValue<string>()).IsEqualTo("queued");
            var clipIds = body!["clipIds"] as JsonArray;
            await Assert.That(clipIds).Count().IsEqualTo(1);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task GetById_UnknownRequest_Returns404()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/requests/999");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally { CleanupDb(dbPath); }
    }
}
