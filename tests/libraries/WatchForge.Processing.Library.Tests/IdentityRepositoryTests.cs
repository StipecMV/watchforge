using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S10-1: IdentityRepository — CRUD nad IDENTITIES (FR-05 databáza tvárí).
/// </summary>
public class IdentityRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-identities-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private IdentityRepository? _repo;
    private FaceRepository? _faceRepo;

    private async Task<IdentityRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new IdentityRepository(_connection);
        _faceRepo = new FaceRepository(_connection);

        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO USERS (username, role) VALUES ('admin', 'admin');
            """;
        await seed.ExecuteNonQueryAsync();
        return _repo;
    }

    [Test]
    public async Task Create_ThenGetAll_ReturnsIdentity()
    {
        var repo = await RepoAsync();

        var id = await repo.CreateAsync("admin", createdBy: 1);
        var all = await repo.GetAllAsync();

        await Assert.That(id).IsGreaterThan(0);
        await Assert.That(all).Count().IsEqualTo(1);
        await Assert.That(all[0].Name).IsEqualTo("admin");
        await Assert.That(all[0].CreatedBy).IsEqualTo(1);
    }

    [Test]
    public async Task GetById_ReturnsIdentity_OrNull()
    {
        var repo = await RepoAsync();
        var id = await repo.CreateAsync("user1", null);

        var found = await repo.GetByIdAsync(id);
        var missing = await repo.GetByIdAsync(999);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Name).IsEqualTo("user1");
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task Rename_UpdatesName()
    {
        var repo = await RepoAsync();
        var id = await repo.CreateAsync("staré meno", null);

        var renamed = await repo.RenameAsync(id, "nové meno");
        var found = await repo.GetByIdAsync(id);

        await Assert.That(renamed).IsTrue();
        await Assert.That(found!.Name).IsEqualTo("nové meno");
    }

    [Test]
    public async Task Delete_RemovesIdentity_AndItsFaces()
    {
        var repo = await RepoAsync();
        var id = await repo.CreateAsync("mazem", null);

        // Seed: detekcia + face naviazaná na identitu
        await using var seed = _connection!.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
            INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
            VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160);
            INSERT INTO DETECTIONS (recording_id, camera_id, detection_type, timestamp_ms, duration_ms, confidence, algorithm_version, region_x, region_y, region_w, region_h)
            VALUES (1, 1, 'face', 1000, 500, 0.9, 'lbph-face-1', 0.1, 0.2, 0.3, 0.4);
            SELECT last_insert_rowid();
            """;
        var detectionId = Convert.ToInt32(await seed.ExecuteScalarAsync());

        await _faceRepo!.InsertAsync(new FaceRecord
        {
            DetectionId = detectionId, IdentityId = id, Confidence = 0.85, TempCropPath = "/tmp/crop.jpg"
        });
        await _faceRepo.DeleteByIdentityAsync(id);
        var deleted = await repo.DeleteAsync(id);

        await Assert.That(deleted).IsTrue();
        await Assert.That(await repo.GetByIdAsync(id)).IsNull();
        await Assert.That(await _faceRepo.GetByIdentityAsync(id)).IsEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
