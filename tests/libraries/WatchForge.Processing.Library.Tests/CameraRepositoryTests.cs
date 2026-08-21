using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S6-5: CameraRepository.UpdateAsync — Settings → Cameras (friendly name, ikona, active).
/// </summary>
public class CameraRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-cameras-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private CameraRepository? _repo;

    private async Task<CameraRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new CameraRepository(_connection);
        // Seed: NVR + 2 kamery (ako synchronizácia S4-2)
        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id) VALUES (1, 0, 'Dvor', 'garden');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id) VALUES (1, 1, 'Brana', 'gate');
            """;
        await seed.ExecuteNonQueryAsync();
        return _repo;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Test]
    public async Task Update_ChangesFriendlyNameIconAndActive()
    {
        // Given kamera so seednutými hodnotami
        var repo = await RepoAsync();
        var before = (await repo.GetAllAsync(null)).Single(c => c.Channel == 0);
        await Assert.That(before.FriendlyName).IsEqualTo("Dvor");
        await Assert.That(before.IconId).IsEqualTo("garden");

        // When upravíme profil kamery (Settings → Cameras)
        await repo.UpdateAsync(new Camera
        {
            CameraId = before.CameraId,
            NvrId = before.NvrId,
            Channel = before.Channel,
            FriendlyName = "Predný dvor",
            IconId = "yard",
            IsActive = false,
        });

        // Then friendly_name, icon_id aj is_active sú zmenené
        var after = (await repo.GetAllAsync(null)).Single(c => c.CameraId == before.CameraId);
        await Assert.That(after.FriendlyName).IsEqualTo("Predný dvor");
        await Assert.That(after.IconId).IsEqualTo("yard");
        await Assert.That(after.IsActive).IsFalse();
        // channel sa NESMIE zmeniť (system ID ostáva pre logy/API)
        await Assert.That(after.Channel).IsEqualTo(0);
    }

    [Test]
    public async Task Update_DoesNotAffectOtherCameras()
    {
        // Given dve kamery
        var repo = await RepoAsync();

        // When upravíme len prvú
        var first = (await repo.GetAllAsync(null)).Single(c => c.Channel == 0);
        await repo.UpdateAsync(new Camera
        {
            CameraId = first.CameraId,
            NvrId = first.NvrId,
            Channel = first.Channel,
            FriendlyName = "Zmenená",
            IconId = "pool",
            IsActive = first.IsActive,
        });

        // Then druhá ostala nedotknutá
        var second = (await repo.GetAllAsync(null)).Single(c => c.Channel == 1);
        await Assert.That(second.FriendlyName).IsEqualTo("Brana");
        await Assert.That(second.IconId).IsEqualTo("gate");
    }
}
