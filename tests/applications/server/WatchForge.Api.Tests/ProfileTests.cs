using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-7: detekčné profily (GET/PUT per-camera + shared, verziovanie)
/// a správa používateľov (GET /users admin).
/// </summary>
[NotInParallel]
public class ProfileTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-prof-" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task Profiles_GetWithoutProfile_Returns404()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/profiles?cameraId=1");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_SharedPutThenGet_FallbackWorks()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            // PUT shared profil
            var put = await client.PutAsJsonAsync("/api/v1/profiles/shared", new
            {
                sensitivity = 0.7f,
                intensityThreshold = 0.03f,
                minContourArea = 0.002f,
                ignoreZones = new[] { new { x = 0.1f, y = 0.1f, w = 0.2f, h = 0.2f } },
                focusZones = Array.Empty<object>(),
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var putBody = await put.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(putBody!["profileType"]?.GetValue<string>()).IsEqualTo("shared");

            // GET pre kameru 1 → shared fallback
            var get = await client.GetAsync("/api/v1/profiles?cameraId=1");
            await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await get.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["sensitivity"]?.GetValue<float>()).IsEqualTo(0.7f);
            await Assert.That(body!["cameraId"]).IsNull();
            var zones = body!["ignoreZones"] as JsonArray;
            await Assert.That(zones).Count().IsEqualTo(1);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_PerCameraPut_OverridesShared()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            await client.PutAsJsonAsync("/api/v1/profiles/shared", new { sensitivity = 0.5f });
            var put = await client.PutAsJsonAsync("/api/v1/profiles/1", new
            {
                sensitivity = 0.9f,
                intensityThreshold = 0.01f,
                minContourArea = 0.001f,
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var get = await client.GetAsync("/api/v1/profiles?cameraId=1");
            var body = await get.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["cameraId"]?.GetValue<int>()).IsEqualTo(1);
            await Assert.That(body!["profileType"]?.GetValue<string>()).IsEqualTo("per_camera");
            await Assert.That(body!["sensitivity"]?.GetValue<float>()).IsEqualTo(0.9f);

            // Druhý PUT → nová verzia (verziovanie)
            var put2 = await client.PutAsJsonAsync("/api/v1/profiles/1", new { sensitivity = 0.4f });
            var body2 = await put2.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body2!["configVersionId"]!.GetValue<int>())
                .IsGreaterThan(body!["configVersionId"]!.GetValue<int>());
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_ZoneWithNameShapePoints_RoundTrips()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var put = await client.PutAsJsonAsync("/api/v1/profiles/1", new
            {
                sensitivity = 0.5f,
                intensityThreshold = 0.02f,
                minContourArea = 0.001f,
                ignoreZones = new[]
                {
                    new
                    {
                        x = 0.1f, y = 0.1f, w = 0.2f, h = 0.3f,
                        name = "Mailbox area",
                        shape = "polygon",
                        points = new[] { new[] { 0.1f, 0.1f }, new[] { 0.3f, 0.1f }, new[] { 0.3f, 0.4f } },
                    },
                },
                focusZones = new[]
                {
                    new { x = 0.5f, y = 0.5f, w = 0.2f, h = 0.2f, name = "Driveway", shape = "rect" },
                },
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var get = await client.GetAsync("/api/v1/profiles?cameraId=1");
            var body = await get.Content.ReadFromJsonAsync<JsonObject>();
            var ignore = body!["ignoreZones"] as JsonArray;
            await Assert.That(ignore).Count().IsEqualTo(1);
            await Assert.That(ignore![0]!["name"]?.GetValue<string>()).IsEqualTo("Mailbox area");
            await Assert.That(ignore[0]!["shape"]?.GetValue<string>()).IsEqualTo("polygon");
            var points = ignore[0]!["points"] as JsonArray;
            await Assert.That(points).Count().IsEqualTo(3);
            await Assert.That(points![0]![0]?.GetValue<float>()).IsEqualTo(0.1f);

            var focus = body!["focusZones"] as JsonArray;
            await Assert.That(focus).Count().IsEqualTo(1);
            await Assert.That(focus![0]!["name"]?.GetValue<string>()).IsEqualTo("Driveway");
            await Assert.That(focus[0]!["shape"]?.GetValue<string>()).IsEqualTo("rect");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_ZoneWithInvalidShape_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PutAsJsonAsync("/api/v1/profiles/1", new
            {
                sensitivity = 0.5f,
                intensityThreshold = 0.02f,
                minContourArea = 0.001f,
                ignoreZones = new[] { new { x = 0.1f, y = 0.1f, w = 0.2f, h = 0.2f, shape = "circle" } },
            });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_ZoneWithInvalidPoints_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PutAsJsonAsync("/api/v1/profiles/1", new
            {
                sensitivity = 0.5f,
                intensityThreshold = 0.02f,
                minContourArea = 0.001f,
                ignoreZones = new[]
                {
                    new { x = 0.1f, y = 0.1f, w = 0.2f, h = 0.2f, shape = "polygon", points = new[] { new[] { 0.1f, 1.5f } } },
                },
            });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_InvalidValues_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PutAsJsonAsync("/api/v1/profiles/1", new { sensitivity = 1.5f });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Profiles_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/api/v1/profiles?cameraId=1");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Users_GetAll_AdminToken_ReturnsUsers()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/users");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonArray>();
            await Assert.That(body).Count().IsEqualTo(5); // seed: admin, user1, user2, user3, user4
            await Assert.That(body![0]!["username"]?.GetValue<string>()).IsEqualTo("admin");
            await Assert.That(body![0]!["role"]?.GetValue<string>()).IsEqualTo("admin");
            await Assert.That(body![0]!["hasPassword"]?.GetValue<bool>()).IsFalse();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Users_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/api/v1/users");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }
}
