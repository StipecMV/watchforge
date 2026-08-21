using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-6: persist (zachovaj záznam), flag/false positive, annotations endpointy.
/// </summary>
[NotInParallel]
public class InteractionsTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-int-" + Guid.NewGuid().ToString("N") + ".db");
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
            var recId = await recordings.InsertAsync(new Recording
            {
                NvrId = 1, CameraId = 1, SourceType = "segment",
                NvrFilename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
                EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
                DurationSec = 900, SizeBytes = 1024, Availability = "available",
                AnalysisState = "completed",
            });

            var detections = new DetectionRepository(connection);
            await detections.InsertAsync(new Detection
            {
                RecordingId = recId, CameraId = 1, DetectionType = "motion",
                TimestampMs = 500, DurationMs = 500,
                Region = new NormalizedRegion(0.1f, 0.2f, 0.3f, 0.4f),
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

    private static HttpClient AuthorizedClient(WebApplicationFactory<WatchForge.Api.ApiEntryPoint> factory, string token = "agent-secret")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Token", token);
        return client;
    }

    // ── persist ──────────────────────────────────────────────────────────

    [Test]
    public async Task Persist_SetsFlagAndRecordingPersisted()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var response = await client.PostAsJsonAsync("/api/v1/recordings/1/persist", new
            {
                scope = "recording",
                note = "Zdielať s policiou",
            });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["persisted"]?.GetValue<bool>()).IsTrue();

            // Recording má persisted=true (retention ho nebude mazať)
            await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);
            var recording = await new RecordingRepository(connection).GetByIdAsync(1);
            await Assert.That(recording!.Persisted).IsTrue();

            // Persist flag je aktívny
            var flags = await new PersistRepository(connection).GetActiveByRecordingAsync(1);
            await Assert.That(flags).Count().IsEqualTo(1);
            await Assert.That(flags[0].Note).IsEqualTo("Zdielať s policiou");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Persist_ThenUnpersist_ClearsFlag()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            await client.PostAsJsonAsync("/api/v1/recordings/1/persist", new { scope = "recording" });
            var response = await client.DeleteAsync("/api/v1/recordings/1/persist");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);
            var recording = await new RecordingRepository(connection).GetByIdAsync(1);
            await Assert.That(recording!.Persisted).IsFalse();
            var flags = await new PersistRepository(connection).GetActiveByRecordingAsync(1);
            await Assert.That(flags).IsEmpty();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Persist_Range_RequiresValidRange()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var bad = await client.PostAsJsonAsync("/api/v1/recordings/1/persist", new
            {
                scope = "range", // chýba rangeStart/rangeEnd
            });
            await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── flag ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Flag_FalsePositive_IsPersisted()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var response = await client.PostAsJsonAsync("/api/v1/detections/1/flag", new { flag = "false_positive" });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // Detekcia má flag v DB
            var query = await client.GetAsync("/api/v1/detections?flag=false_positive");
            var body = await query.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["total"]!.GetValue<int>()).IsEqualTo(1);
            await Assert.That(body!["items"]!.AsArray()[0]!["flag"]?.GetValue<string>()).IsEqualTo("false_positive");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Flag_InvalidValue_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var response = await client.PostAsJsonAsync("/api/v1/detections/1/flag", new { flag = "maybe" });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── annotations ──────────────────────────────────────────────────────

    [Test]
    public async Task Annotations_AddAndList()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var add = await client.PostAsJsonAsync("/api/v1/detections/1/annotations", new
            {
                regionX = 0.2f, regionY = 0.25f, regionW = 0.1f, regionH = 0.1f,
                label = "Človek pri bráne",
            });
            await Assert.That(add.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var added = await add.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(added!["label"]?.GetValue<string>()).IsEqualTo("Človek pri bráne");

            var list = await client.GetAsync("/api/v1/detections/1/annotations");
            var body = await list.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(1);
            await Assert.That(body![0]!["regionX"]?.GetValue<float>()).IsEqualTo(0.2f);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Annotations_InvalidRegion_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            // Región presahuje 0..1
            var response = await client.PostAsJsonAsync("/api/v1/detections/1/annotations", new
            {
                regionX = 0.9f, regionY = 0.9f, regionW = 0.5f, regionH = 0.5f,
            });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Flag_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/detections/1/flag", new { flag = "flagged" });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── annotations: clear my drawings (S6-7, FR-16) ───────────────────

    [Test]
    public async Task Annotations_ClearMyDrawings_DeletesOnlyMyOwnAnnotations()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            // Dve moje anotácie cez API
            await client.PostAsJsonAsync("/api/v1/detections/1/annotations", new
            {
                regionX = 0.1f, regionY = 0.1f, regionW = 0.2f, regionH = 0.2f, label = "Moja kresba 1",
            });
            await client.PostAsJsonAsync("/api/v1/detections/1/annotations", new
            {
                regionX = 0.5f, regionY = 0.5f, regionW = 0.1f, regionH = 0.1f, label = "Moja kresba 2",
            });

            // Cudzia anotácia (iný user_id priamo do DB — agent user je 0, seednutý user 2 = User One)
            await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
            {
                await new AnnotationRepository(connection).InsertAsync(new Annotation
                {
                    DetectionId = 1,
                    UserId = 2,
                    Region = new NormalizedRegion(0.3f, 0.3f, 0.1f, 0.1f),
                    Label = "Cudzia",
                });
            }

            var clear = await client.DeleteAsync("/api/v1/detections/1/annotations");
            await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var list = await client.GetAsync("/api/v1/detections/1/annotations");
            var body = await list.Content.ReadFromJsonAsync<JsonArray>();
            // Zmizli len moje dve; cudzia ostala
            await Assert.That(body).Count().IsEqualTo(1);
            await Assert.That(body![0]!["label"]?.GetValue<string>()).IsEqualTo("Cudzia");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Annotations_UpdateOwn_ChangesRegionAndLabel()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var add = await client.PostAsJsonAsync("/api/v1/detections/1/annotations", new
            {
                regionX = 0.1f, regionY = 0.1f, regionW = 0.2f, regionH = 0.2f, label = "Predtým",
            });
            var added = await add.Content.ReadFromJsonAsync<JsonObject>();
            var id = added!["annotationId"]!.GetValue<int>();

            var update = await client.PutAsJsonAsync($"/api/v1/detections/1/annotations/{id}", new
            {
                regionX = 0.4f, regionY = 0.45f, regionW = 0.15f, regionH = 0.1f, label = "Potom",
            });
            await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var list = await client.GetAsync("/api/v1/detections/1/annotations");
            var body = await list.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(1);
            await Assert.That(body![0]!["label"]?.GetValue<string>()).IsEqualTo("Potom");
            await Assert.That(body![0]!["regionX"]?.GetValue<float>()).IsEqualTo(0.4f);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Annotations_UpdateOrClearOtherUsers_Returns404()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            // Cudzia anotácia priamo v DB (user 2 = User One ≠ agent user 0)
            int foreignId;
            await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
            {
                foreignId = await new AnnotationRepository(connection).InsertAsync(new Annotation
                {
                    DetectionId = 1,
                    UserId = 2,
                    Region = new NormalizedRegion(0.3f, 0.3f, 0.1f, 0.1f),
                    Label = "Cudzia",
                });
            }

            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = AuthorizedClient(factory);

            var update = await client.PutAsJsonAsync($"/api/v1/detections/1/annotations/{foreignId}", new
            {
                regionX = 0.5f, regionY = 0.5f, regionW = 0.1f, regionH = 0.1f, label = "Hack",
            });
            await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            // Clear my drawings cudziu anotáciu nevymaže (overené v Clear test)
            var clear = await client.DeleteAsync("/api/v1/detections/1/annotations");
            await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var list = await client.GetAsync("/api/v1/detections/1/annotations");
            var body = await list.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(1);
        }
        finally { CleanupDb(dbPath); }
    }
}
