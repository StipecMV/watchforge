using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S12-1: GET /api/v1/system/status — operatívny status (admin):
/// NVR + worker + sync/backlog (FR-11).
/// </summary>
[NotInParallel]
public class SystemStatusTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-status-" + Guid.NewGuid().ToString("N") + ".db");
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
                INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height, analysis_state)
                VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160, 'completed');
                INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height, analysis_state)
                VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.15.00-15.30.mkv', '2026-04-05 15:15:00', '2026-04-05 15:30:00', 900, 100, 'hevc', 3840, 2160, 'queued');
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        return dbPath;
    }

    private static WebApplicationFactory<WatchForge.Api.ApiEntryPoint> CreateFactory(string dbPath)
    {
        Environment.SetEnvironmentVariable(DbEnvKey, dbPath);
        try
        {
            var factory = new WebApplicationFactory<WatchForge.Api.ApiEntryPoint>();
            _ = factory.Services;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable(DbEnvKey, null);
        }
    }

    private static void CleanupDb(string dbPath)
    {
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Test]
    public async Task Status_IsPublic_ReturnsWorkerAndBacklog() // status je verejný (štatistiky analyz pre UI)
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var status = await client.GetFromJsonAsync<JsonObject>("/api/v1/system/status");

            await Assert.That(status).IsNotNull();
            // Worker (verejné štatistiky analyz)
            await Assert.That(status!["worker"]!["total"]!.GetValue<int>()).IsEqualTo(0);
            // Sync/backlog: 1 queued záznam (2. recording) — completed sa neráta
            await Assert.That(status["sync"]!["backlog"]!.GetValue<int>()).IsEqualTo(1);
            await Assert.That(status["sync"]!["totalRecordings"]!.GetValue<int>()).IsEqualTo(2);
            // LastSync = najnovší begin_time (15:15)
            await Assert.That(status["sync"]!["lastSyncUtc"]).IsNotNull();
            // NVR detaily NIE sú vo verejnom statuse (sú v /info, admin-only)
            await Assert.That(status["nvr"]).IsNull();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task Status_WithoutLogin_Returns200() // neprihlásený vidí štatistiky analyz
    {
        var dbPath = await CreateSeededDbAsync();
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var get = await client.GetAsync("/api/v1/system/status");

            await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally { CleanupDb(dbPath); }
    }
}
