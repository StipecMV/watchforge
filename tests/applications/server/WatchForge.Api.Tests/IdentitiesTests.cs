using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S10-4: Identities — GET zoznam, POST create, POST learn (priradenie face),
/// DELETE (vymazanie identity aj embeddings).
/// </summary>
[NotInParallel]
public class IdentitiesTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";
    private const string TokenEnvKey = "WatchForge__Api__ApiToken";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-identities-api-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            var users = new UserRepository(connection);
            await users.SeedDefaultUsersAsync();
            var user = await users.GetByUsernameAsync("admin");
            await users.UpdatePasswordHashAsync(user!.UserId, UserRepository.HashPassword("heslo1234"));

            // Seed: NVR + kamera + recording + face detekcia (pre learn test)
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 0, 'Dvor', 'camera', 1);
                INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
                VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160);
                INSERT INTO DETECTIONS (recording_id, camera_id, detection_type, timestamp_ms, duration_ms, confidence, algorithm_version, region_x, region_y, region_w, region_h)
                VALUES (1, 1, 'face', 1000, 500, 0.9, 'lbph-face-1', 0.1, 0.2, 0.3, 0.4);
                SELECT last_insert_rowid();
                """;
            var detectionId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            // Face record (embedding = miniatúrny JPEG)
            await using var face = connection.CreateCommand();
            face.CommandText = """
                INSERT INTO FACES (detection_id, embedding, confidence, temp_crop_path)
                VALUES ($det, $emb, 0.8, '');
                """;
            face.Parameters.AddWithValue("$det", detectionId);
            face.Parameters.AddWithValue("$emb", CreateFakeJpeg());
            await face.ExecuteNonQueryAsync();
        }
        return dbPath;
    }

    /// <summary>Minimálny platný JPEG (1×1) — ImDecode ho zvládne.</summary>
    private static byte[] CreateFakeJpeg()
    {
        // 1x1 čierny JPEG (base64) — validný pre OpenCV ImDecode
        return Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==");
    }

    private static WebApplicationFactory<WatchForge.Api.ApiEntryPoint> CreateFactory(string dbPath, string? apiToken = null)
    {
        Environment.SetEnvironmentVariable(DbEnvKey, dbPath);
        if (apiToken is not null)
            Environment.SetEnvironmentVariable(TokenEnvKey, apiToken);
        try
        {
            var factory = new WebApplicationFactory<WatchForge.Api.ApiEntryPoint>();
            _ = factory.Services;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable(DbEnvKey, null);
            Environment.SetEnvironmentVariable(TokenEnvKey, null);
        }
    }

    private static void CleanupDb(string dbPath)
    {
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private static async Task LoginAsync(HttpClient client, string username, string password = "heslo1234")
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
    }

    [Test]
    public async Task GetAll_RequiresAuth()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var get = await client.GetAsync("/api/v1/identities");

            await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Create_ThenGetAll_ReturnsIdentityWithZeroFaces()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var post = await client.PostAsJsonAsync("/api/v1/identities", new { name = "Anicka" });
            await Assert.That(post.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var get = await client.GetFromJsonAsync<JsonArray>("/api/v1/identities");
            var identity = get!.Single(i => i!["name"]!.GetValue<string>() == "Anicka");
            await Assert.That(identity!["identityId"]!.GetValue<int>()).IsGreaterThan(0);
            await Assert.That(identity!["faceCount"]!.GetValue<int>()).IsEqualTo(0);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Learn_AssignsIdentity_ToFaceDetection()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            // Vytvor identitu
            var post = await client.PostAsJsonAsync("/api/v1/identities", new { name = "Admin" });
            var created = (await post.Content.ReadFromJsonAsync<JsonObject>())!;
            var identityId = created["identityId"]!.GetValue<int>();

            // Learn — priraď face detekciu (detection_id=1) k identite
            var learn = await client.PostAsJsonAsync("/api/v1/identities/learn", new
            {
                detectionId = 1,
                identityId,
            });
            await Assert.That(learn.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // Po learn má identita 1 tvár
            var get = await client.GetFromJsonAsync<JsonArray>("/api/v1/identities");
            var identity = get!.Single(i => i!["identityId"]!.GetValue<int>() == identityId);
            await Assert.That(identity!["faceCount"]!.GetValue<int>()).IsEqualTo(1);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Learn_UnknownIdentity_ReturnsNotFound()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var learn = await client.PostAsJsonAsync("/api/v1/identities/learn", new
            {
                detectionId = 1,
                identityId = 999,
            });

            await Assert.That(learn.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Delete_RemovesIdentity_AndItsFaces()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var post = await client.PostAsJsonAsync("/api/v1/identities", new { name = "Mazem" });
            var created = (await post.Content.ReadFromJsonAsync<JsonObject>())!;
            var identityId = created["identityId"]!.GetValue<int>();

            // Priraď face a potom vymaž
            await client.PostAsJsonAsync("/api/v1/identities/learn", new { detectionId = 1, identityId });
            var del = await client.DeleteAsync($"/api/v1/identities/{identityId}");
            await Assert.That(del.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var get = await client.GetFromJsonAsync<JsonArray>("/api/v1/identities");
            await Assert.That(get!.Any(i => i!["identityId"]!.GetValue<int>() == identityId)).IsFalse();
        }
        finally { CleanupDb(dbPath); }
    }
}
