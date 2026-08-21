using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-2: auth — login (PBKDF2), prvé nastavenie hesla, admin reset, session cookie,
/// API token pre agenta. Testy bežia sekvenčne ([NotInParallel] — nastavujú globálne
/// env vars pre konfiguráciu factory).
/// </summary>
[NotInParallel]
public class AuthTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";
    private const string TokenEnvKey = "WatchForge__Api__ApiToken";

    /// <summary>Vytvorí temp DB so seednutým userom (admin: heslo "heslo1234", alebo bez hesla).</summary>
    private static async Task<string> CreateSeededDbAsync(bool withPassword)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-api-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            var repo = new UserRepository(connection);
            await repo.SeedDefaultUsersAsync();
            var admin = await repo.GetByUsernameAsync("admin");
            if (withPassword)
                await repo.UpdatePasswordHashAsync(admin!.UserId, UserRepository.HashPassword("heslo1234"));
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
            _ = factory.Services; // build hostu kým sú env vars nastavené
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

    // ── login ────────────────────────────────────────────────────────────

    [Test]
    public async Task Login_ValidCredentials_SetsSessionCookie()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "heslo1234" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["username"]?.GetValue<string>()).IsEqualTo("admin");
            await Assert.That(body!["role"]?.GetValue<string>()).IsEqualTo("admin");

            // Session cookie nastavená (HttpOnly, path=/)
            var cookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
                ? cookies.FirstOrDefault(c => c.StartsWith("wf_session="))
                : null;
            await Assert.That(cookie).IsNotNull();
            await Assert.That(cookie!.Contains("httponly", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Login_WrongPassword_Returns401()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "zle-heslo" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Login_UnknownUser_Returns401()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "nikto", password = "heslo1234" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Login_PasswordNotSet_Returns428()
    {
        // Given user bez nastaveného hesla (prvý login)
        var dbPath = await CreateSeededDbAsync(withPassword: false);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "hocijake" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PreconditionRequired);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── set-password ─────────────────────────────────────────────────────

    [Test]
    public async Task SetPassword_FirstTime_SetsHashAndLogsIn()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: false);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/set-password", new { username = "admin", newPassword = "nove-heslo-123" });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // Potom sa dá prihlásiť s novým heslom
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "nove-heslo-123" });
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task SetPassword_AlreadySet_Returns409()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/set-password", new { username = "admin", newPassword = "nove-heslo-123" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── reset-password (admin) ───────────────────────────────────────────

    [Test]
    public async Task ResetPassword_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { username = "user2" });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ResetPassword_WithApiToken_ClearsHash()
    {
        // Given agent token (admin identita)
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { username = "user2" });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // User Two už nemá heslo → login vráti 428
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "user2", password = "heslo1234" });
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.PreconditionRequired);
        }
        finally { CleanupDb(dbPath); }
    }

    // ── me (session + token) ─────────────────────────────────────────────

    [Test]
    public async Task Me_WithoutAuth_Returns401()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/v1/auth/me");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Me_WithSessionCookie_ReturnsUser()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "heslo1234" });
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // HttpClient automaticky posiela Set-Cookie späť
            var response = await client.GetAsync("/api/v1/auth/me");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            await Assert.That(body!["username"]?.GetValue<string>()).IsEqualTo("admin");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Me_WithApiToken_ReturnsAdminIdentity()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/auth/me");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            // API token vystupuje ako prvý admin používateľ (admin) — reálny UserId pre FK
            await Assert.That(body!["username"]?.GetValue<string>()).IsEqualTo("admin");
            await Assert.That(body!["role"]?.GetValue<string>()).IsEqualTo("admin");
        }
        finally { CleanupDb(dbPath); }
    }

    // ── logout ───────────────────────────────────────────────────────────

    [Test]
    public async Task Logout_ClearsSessionCookie()
    {
        var dbPath = await CreateSeededDbAsync(withPassword: true);
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "heslo1234" });
            var logout = await client.PostAsync("/api/v1/auth/logout", null);
            await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.OK);

            // Session je zrušená → /me vráti 401
            var me = await client.GetAsync("/api/v1/auth/me");
            await Assert.That(me.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        finally { CleanupDb(dbPath); }
    }
}
