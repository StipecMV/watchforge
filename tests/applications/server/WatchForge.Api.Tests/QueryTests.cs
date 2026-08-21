using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-3: GET cameras / recordings / detections — query s filtrami, auth ochrana.
/// </summary>
[NotInParallel]
public class QueryTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-query-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            await new UserRepository(connection).SeedDefaultUsersAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 0, 'Dvor', 'camera', 1), (1, 2, 'Garaz', 'camera', 1), (1, 3, 'Zahrada', 'camera', 0);
                """;
            await cmd.ExecuteNonQueryAsync();

            var recordings = new RecordingRepository(connection);
            var rec = await recordings.InsertAsync(new Recording
            {
                NvrId = 1, CameraId = 1, SourceType = "segment",
                NvrFilename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
                EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
                DurationSec = 900, SizeBytes = 1024, Codec = "hevc",
                Width = 3840, Height = 2160,
                Availability = "available", AnalysisState = "completed",
            });

            var detections = new DetectionRepository(connection);
            await detections.InsertAsync(new Detection
            {
                RecordingId = rec, CameraId = 1, DetectionType = "motion",
                TimestampMs = 500, DurationMs = 500, AlgorithmVersion = "optical-flow-1",
                Region = new NormalizedRegion(0.1f, 0.2f, 0.3f, 0.4f), Intensity = 0.7f,
            });
            await detections.InsertAsync(new Detection
            {
                RecordingId = rec, CameraId = 1, DetectionType = "motion",
                TimestampMs = 2500, DurationMs = 500, AlgorithmVersion = "optical-flow-1",
                Region = new NormalizedRegion(0.5f, 0.1f, 0.2f, 0.2f), Intensity = 0.4f, Flag = "flagged",
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
    public async Task Cameras_WithoutAuth_Returns200() // kamery sú verejné (live view bez prihlásenia)
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/api/v1/cameras");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Cameras_WithApiToken_ReturnsAllCameras()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/cameras");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(3);
            await Assert.That(body![0]!["friendlyName"]?.GetValue<string>()).IsEqualTo("Dvor");
            await Assert.That(body![2]!["isActive"]?.GetValue<bool>()).IsFalse();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Recordings_Query_ReturnsFilteredByCameraAndTime()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            // Filter na kamery 1 a časový rozsah pokrývajúci záznam
            var response = await client.GetAsync(
                "/api/v1/recordings?cameraId=1&from=2026-04-05T14:00:00Z&to=2026-04-05T16:00:00Z");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(1);
            await Assert.That(body![0]!["nvrFilename"]?.GetValue<string>())
                .IsEqualTo("[Ch0]_2026-04-05_15.00.00-15.15.mkv");
            await Assert.That(body![0]!["analysisState"]?.GetValue<string>()).IsEqualTo("completed");

            // Rôzny rozsah → prázdne
            var empty = await client.GetAsync("/api/v1/recordings?from=2026-04-06T00:00:00Z&to=2026-04-06T23:00:00Z");
            var emptyBody = await empty.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(emptyBody).IsEmpty();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Detections_Query_ReturnsNormalizedRegions()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/detections?cameraId=1&detectionType=motion");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // Odpoveď je { total, items } (items max 2000) — nie holé pole.
            // Zoradenie je DESC (najnovšie prvé) — hľadáme item podľa regionX.
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["total"]!.GetValue<int>()).IsEqualTo(2);
            var items = body!["items"]!.AsArray();
            await Assert.That(items).Count().IsEqualTo(2);
            var regionItem = items.FirstOrDefault(i => i!["regionX"]?.GetValue<float>() == 0.1f);
            await Assert.That(regionItem).IsNotNull();
            await Assert.That(regionItem!["regionH"]?.GetValue<float>()).IsEqualTo(0.4f);
            await Assert.That(regionItem!["algorithmVersion"]?.GetValue<string>()).IsEqualTo("optical-flow-1");

            // Filter flag
            var flagged = await client.GetAsync("/api/v1/detections?cameraId=1&flag=flagged");
            var flaggedBody = await flagged.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(flaggedBody!["total"]!.GetValue<int>()).IsEqualTo(1);
            await Assert.That(flaggedBody!["items"]!.AsArray()[0]!["flag"]?.GetValue<string>()).IsEqualTo("flagged");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Recordings_WithoutAuth_Returns200() // záznamy sú verejné (analýzy bez prihlásenia)
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/api/v1/recordings");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally { CleanupDb(dbPath); }
    }
}
