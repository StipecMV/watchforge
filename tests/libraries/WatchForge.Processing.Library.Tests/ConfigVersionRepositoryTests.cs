using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-7: ConfigVersionRepository — verziovanie detekčných profilov (FR-06).
/// </summary>
public class ConfigVersionRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-config-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private ConfigVersionRepository? _repo;

    private async Task<ConfigVersionRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new ConfigVersionRepository(_connection);
        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
            """;
        await seed.ExecuteNonQueryAsync();
        return _repo;
    }

    [Test]
    public async Task Insert_ThenGetActiveForCamera_ReturnsLatestVersion()
    {
        // Given dve verzie konfigurácie pre kameru 1
        var repo = await RepoAsync();
        var v1 = new ConfigVersion
        {
            CameraId = 1,
            ProfileName = "den",
            ProfileType = "per_camera",
            Sensitivity = 0.5f,
            CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var v2 = new ConfigVersion
        {
            CameraId = 1,
            ProfileName = "noc",
            ProfileType = "per_camera",
            Sensitivity = 0.8f,
            CreatedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        var id1 = await repo.InsertAsync(v1);
        var id2 = await repo.InsertAsync(v2);

        // When získame aktívnu
        var active = await repo.GetActiveForCameraAsync(1);

        // Then je to najnovšia verzia (v2, id2)
        await Assert.That(active).IsNotNull();
        await Assert.That(active!.ConfigVersionId).IsEqualTo(id2);
        await Assert.That(active.ProfileName).IsEqualTo("noc");
        await Assert.That(active.Sensitivity).IsEqualTo(0.8f);
    }

    [Test]
    public async Task Deactivate_MakesOlderVersionInactive()
    {
        // Given aktívna verzia
        var repo = await RepoAsync();
        var v1 = new ConfigVersion { CameraId = 1, ProfileName = "den", CreatedAt = DateTime.UtcNow };
        var id1 = await repo.InsertAsync(v1);

        // When deaktivujeme
        await repo.DeactivateAsync(id1);

        // Then aktívna už nie je
        var active = await repo.GetActiveForCameraAsync(1);
        await Assert.That(active).IsNull();
    }

    [Test]
    public async Task Insert_NewVersion_DeactivatesOldAutomatically()
    {
        // Given aktívna verzia v1
        var repo = await RepoAsync();
        var v1 = new ConfigVersion { CameraId = 1, ProfileName = "den", CreatedAt = DateTime.UtcNow };
        var id1 = await repo.InsertAsync(v1);

        // When vložíme v2 (novšia)
        var v2 = new ConfigVersion
        {
            CameraId = 1,
            ProfileName = "noc",
            CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await repo.InsertAsync(v2);

        // Then aktívna je v2, v1 už nie
        var active = await repo.GetActiveForCameraAsync(1);
        await Assert.That(active!.ProfileName).IsEqualTo("noc");
        var all = await GetAllAsync();
        var v1stored = all.Single(c => c.ConfigVersionId == id1);
        await Assert.That(v1stored.IsActive).IsFalse();
    }

    [Test]
    public async Task GetActiveShared_ReturnsSharedProfile()
    {
        // Given zdieľaný profil (camera_id = null)
        var repo = await RepoAsync();
        var shared = new ConfigVersion
        {
            CameraId = null,
            ProfileName = "shared",
            ProfileType = "shared",
            IgnoreZonesJson = """[{"x":0,"y":0,"w":0.5,"h":0.5,"shape":"rect"}]""",
            CreatedAt = DateTime.UtcNow
        };
        await repo.InsertAsync(shared);

        // When získame aktívny zdieľaný
        var active = await repo.GetActiveSharedAsync();

        // Then nájde ho
        await Assert.That(active).IsNotNull();
        await Assert.That(active!.ProfileType).IsEqualTo("shared");
        await Assert.That(active.IgnoreZonesJson).Contains("0.5");
    }

    private async Task<IReadOnlyList<ConfigVersion>> GetAllAsync()
    {
        var result = new List<ConfigVersion>();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM CONFIG_VERSIONS;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ConfigVersion
            {
                ConfigVersionId = reader.GetInt32(reader.GetOrdinal("config_version_id")),
                CameraId = reader.IsDBNull(reader.GetOrdinal("camera_id"))
                    ? null : reader.GetInt32(reader.GetOrdinal("camera_id")),
                ProfileName = reader.GetString(reader.GetOrdinal("profile_name")),
                ProfileType = reader.GetString(reader.GetOrdinal("profile_type")),
                Sensitivity = reader.GetFloat(reader.GetOrdinal("sensitivity")),
                IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) != 0,
            });
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
