using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-5: GET /api/v1/clips/{id} — doručenie klipu (video MP4 / fotka JPG),
/// auth ochrana, 404 pre chýbajúci súbor.
/// </summary>
[NotInParallel]
public class ClipDeliveryTests
{
    private const string DbEnvKey = "WatchForge__Api__DbPath";

    private static async Task<string> CreateSeededDbAsync(string filePath, string kind)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-clipapi-" + Guid.NewGuid().ToString("N") + ".db");
        await using (var connection = await WatchForgeDatabase.OpenAsync(dbPath))
        {
            await new UserRepository(connection).SeedDefaultUsersAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                VALUES (1, 0, 'Dvor', 'camera', 1);
                INSERT INTO REQUESTS (source, requester, query, from_time, to_time, status)
                VALUES ('webui', 'tester', 'test', '2026-04-05T15:00:00Z', '2026-04-05T15:15:00Z', 'completed');
                """;
            await cmd.ExecuteNonQueryAsync();

            var clips = new ClipRepository(connection);
            await clips.InsertAsync(new Clip
            {
                RequestId = 1, Kind = kind,
                RangeStart = new DateTime(2026, 4, 5, 15, 0, 0),
                RangeEnd = new DateTime(2026, 4, 5, 15, 0, 30),
                FilePath = filePath,
                SizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
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
    public async Task Get_VideoClip_ReturnsMp4File()
    {
        // Given existujúci video klip na disku
        var clipFile = Path.Combine(Path.GetTempPath(), "watchforge-clipapi-video-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(clipFile, [0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]); // ftyp mp4 hlavička
        var dbPath = await CreateSeededDbAsync(clipFile, "video");
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/clips/1");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("video/mp4");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            await Assert.That(bytes.Length).IsEqualTo(8);
        }
        finally { CleanupDb(dbPath); if (File.Exists(clipFile)) File.Delete(clipFile); }
    }

    [Test]
    public async Task Get_PhotoClip_ReturnsJpeg()
    {
        // Given existujúca fotka (JPG signatúra)
        var clipFile = Path.Combine(Path.GetTempPath(), "watchforge-clipapi-photo-" + Guid.NewGuid().ToString("N") + ".jpg");
        await File.WriteAllBytesAsync(clipFile, [0xFF, 0xD8, 0xFF, 0xE0]);
        var dbPath = await CreateSeededDbAsync(clipFile, "photo");
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/clips/1");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("image/jpeg");
        }
        finally { CleanupDb(dbPath); if (File.Exists(clipFile)) File.Delete(clipFile); }
    }

    [Test]
    public async Task Get_WithoutAuth_Returns200() // klipy sú verejné (prehrávanie bez prihlásenia)
    {
        var clipFile = Path.Combine(Path.GetTempPath(), "watchforge-clipapi-noauth-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(clipFile, [1, 2, 3]);
        var dbPath = await CreateSeededDbAsync(clipFile, "video");
        try
        {
            await using var factory = CreateFactory(dbPath);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/v1/clips/1");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally { CleanupDb(dbPath); if (File.Exists(clipFile)) File.Delete(clipFile); }
    }

    [Test]
    public async Task Get_MissingFile_Returns404()
    {
        // Given clip záznam, ktorého súbor neexistuje (expirovaný/zmazaný)
        var missingFile = Path.Combine(Path.GetTempPath(), "watchforge-clipapi-missing-" + Guid.NewGuid().ToString("N") + ".mp4");
        var dbPath = await CreateSeededDbAsync(missingFile, "video"); // súbor sa NEVYTVORÍ
        try
        {
            await using var factory = CreateFactory(dbPath, apiToken: "agent-secret");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Token", "agent-secret");

            var response = await client.GetAsync("/api/v1/clips/1");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally { CleanupDb(dbPath); }
    }
}
