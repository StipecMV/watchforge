using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S6-5: Settings — Cameras PUT (friendly name/ikona/active), Profile PUT (avatar/locale),
/// Security change-password, System info (NVR + worker status, admin).
/// </summary>
[NotInParallel]
public class SettingsTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";
    private const string TokenEnvKey = "WatchForge__Api__ApiToken";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-settings-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            var users = new UserRepository(connection);
            await users.SeedDefaultUsersAsync();
            foreach (var name in new[] { "admin", "user1" })
            {
                var user = await users.GetByUsernameAsync(name);
                await users.UpdatePasswordHashAsync(user!.UserId, UserRepository.HashPassword("heslo1234"));
            }
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 0, 'Dvor', 'camera', 1);
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 1, 'Brana', 'camera', 1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        return dbPath;
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

    // ── Cameras (Settings → Cameras) ─────────────────────────────────────

    [Test]
    public async Task Cameras_Put_UpdatesFriendlyNameAndIcon()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var put = await client.PutAsJsonAsync("/api/v1/cameras/1", new
            {
                friendlyName = "Predný dvor",
                iconId = "yard",
                isActive = false,
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var get = await client.GetFromJsonAsync<JsonArray>("/api/v1/cameras");
            var cam1 = get!.First(c => c!["cameraId"]!.GetValue<int>() == 1);
            await Assert.That(cam1!["friendlyName"]!.GetValue<string>()).IsEqualTo("Predný dvor");
            await Assert.That(cam1!["iconId"]!.GetValue<string>()).IsEqualTo("yard");
            await Assert.That(cam1!["isActive"]!.GetValue<bool>()).IsFalse();
            // channel sa nesmie zmeniť
            await Assert.That(cam1!["channel"]!.GetValue<int>()).IsEqualTo(0);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Cameras_Put_UnknownCamera_Returns404()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var put = await client.PutAsJsonAsync("/api/v1/cameras/999", new
            {
                friendlyName = "X",
                iconId = "yard",
                isActive = true,
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Cameras_Put_EmptyFriendlyName_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var put = await client.PutAsJsonAsync("/api/v1/cameras/1", new
            {
                friendlyName = "  ",
                iconId = "yard",
                isActive = true,
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Cameras_Put_Unauthenticated_Returns401()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var put = await client.PutAsJsonAsync("/api/v1/cameras/1", new
            {
                friendlyName = "X",
                iconId = "yard",
                isActive = true,
            });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── Profile (Settings → Profile: avatar + locale) ────────────────────

    [Test]
    public async Task Auth_MePut_UpdatesAvatarAndLocale()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var put = await client.PutAsJsonAsync("/api/v1/auth/me", new { avatarId = 7, locale = "en" });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await put.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["avatarId"]!.GetValue<int>()).IsEqualTo(7);
            await Assert.That(body["locale"]!.GetValue<string>()).IsEqualTo("en");

            // Prežije reload (nový request, session cookie)
            var me = await client.GetFromJsonAsync<JsonObject>("/api/v1/auth/me");
            await Assert.That(me!["avatarId"]!.GetValue<int>()).IsEqualTo(7);
            await Assert.That(me["locale"]!.GetValue<string>()).IsEqualTo("en");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Auth_MePut_InvalidAvatar_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var put = await client.PutAsJsonAsync("/api/v1/auth/me", new { avatarId = 0, locale = "sk" });
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── Security (Settings → Security: change password) ──────────────────

    [Test]
    public async Task Auth_ChangePassword_ThenLoginWithNewPassword()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "user1");

            var change = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
            {
                currentPassword = "heslo1234",
                newPassword = "nove-heslo-123",
            });
            await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // logout + login s novým heslom funguje
            await client.PostAsync("/api/v1/auth/logout", null);
            var loginNew = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "user1", password = "nove-heslo-123" });
            await Assert.That(loginNew.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // staré heslo už nefunguje
            await client.PostAsync("/api/v1/auth/logout", null);
            var loginOld = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "user1", password = "heslo1234" });
            await Assert.That(loginOld.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Auth_ChangePassword_WrongCurrent_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "user1");

            var change = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
            {
                currentPassword = "zle-heslo",
                newPassword = "nove-heslo-123",
            });
            await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Auth_ChangePassword_ShortNew_Returns400()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "user1");

            var change = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
            {
                currentPassword = "heslo1234",
                newPassword = "kratke",
            });
            await Assert.That(change.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── System (Settings → System, admin) ────────────────────────────────

    [Test]
    public async Task System_Info_Admin_ReturnsNvrAndWorker()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin");

            var info = await client.GetFromJsonAsync<JsonObject>("/api/v1/system/info");
            await Assert.That(info).IsNotNull();

            var nvr = info!["nvr"] as JsonObject;
            await Assert.That(nvr).IsNotNull();
            await Assert.That(nvr!["host"]!.GetValue<string>()).IsEqualTo("192.168.68.10");
            await Assert.That(nvr["port"]!.GetValue<int>()).IsEqualTo(34567);
            await Assert.That(nvr["username"]!.GetValue<string>()).IsEqualTo("nvr-user");
            // heslo NIKDY nevracia API (secret je v env, nie v DB)
            await Assert.That(nvr.ContainsKey("password")).IsFalse();

            var worker = info["worker"] as JsonObject;
            await Assert.That(worker).IsNotNull();
            await Assert.That(worker!.ContainsKey("queued")).IsTrue();
            await Assert.That(worker.ContainsKey("running")).IsTrue();

            await Assert.That(info.ContainsKey("apiVersion")).IsTrue();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task System_Info_StandardUser_Returns403()
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();
            await LoginAsync(client, "user1");

            var response = await client.GetAsync("/api/v1/system/info");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        finally { CleanupDb(dbPath); }
    }
}
