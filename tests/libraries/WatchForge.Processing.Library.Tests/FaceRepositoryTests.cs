using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S10-1: FaceRepository — insert, query podľa detekcie/identity, assign,
/// embedding BLOB perzistencia (FR-05).
/// </summary>
public class FaceRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-faces-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private FaceRepository? _repo;

    private async Task<(FaceRepository repo, int detectionId)> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new FaceRepository(_connection);

        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
            INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
            VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160);
            INSERT INTO IDENTITIES (identity_id, name) VALUES (3, 'user1');
            INSERT INTO IDENTITIES (identity_id, name) VALUES (5, 'user3');
            INSERT INTO IDENTITIES (identity_id, name) VALUES (7, 'user2');
            INSERT INTO DETECTIONS (recording_id, camera_id, detection_type, timestamp_ms, duration_ms, confidence, algorithm_version, region_x, region_y, region_w, region_h)
            VALUES (1, 1, 'face', 1000, 500, 0.9, 'lbph-face-1', 0.1, 0.2, 0.3, 0.4);
            SELECT last_insert_rowid();
            """;
        var detectionId = Convert.ToInt32(await seed.ExecuteScalarAsync());
        return (_repo, detectionId);
    }

    [Test]
    public async Task Insert_ThenGetByDetection_ReturnsFace_WithEmbedding()
    {
        var (repo, detId) = await RepoAsync();
        var embedding = new byte[] { 1, 2, 3, 4, 5, 250 };

        var faceId = await repo.InsertAsync(new FaceRecord
        {
            DetectionId = detId, Confidence = 0.9, Embedding = embedding, TempCropPath = "/tmp/crop.jpg"
        });
        var faces = await repo.GetByDetectionAsync(detId);

        await Assert.That(faceId).IsGreaterThan(0);
        await Assert.That(faces).Count().IsEqualTo(1);
        await Assert.That(faces[0].Embedding!.SequenceEqual(embedding)).IsTrue();
        await Assert.That(faces[0].TempCropPath).IsEqualTo("/tmp/crop.jpg");
        await Assert.That(faces[0].IdentityId).IsNull();
    }

    [Test]
    public async Task AssignIdentity_UpdatesFace()
    {
        var (repo, detId) = await RepoAsync();
        var faceId = await repo.InsertAsync(new FaceRecord { DetectionId = detId, Confidence = 0.5 });

        var assigned = await repo.AssignIdentityAsync(faceId, 7);
        var faces = await repo.GetByDetectionAsync(detId);

        await Assert.That(assigned).IsTrue();
        await Assert.That(faces[0].IdentityId).IsEqualTo(7);
    }

    [Test]
    public async Task GetByIdentity_ReturnsOnlyAssigned()
    {
        var (repo, detId) = await RepoAsync();
        await repo.InsertAsync(new FaceRecord { DetectionId = detId, Confidence = 0.5 });              // nepriradená
        var faceId2 = await repo.InsertAsync(new FaceRecord { DetectionId = detId, Confidence = 0.6 }); // priradená
        await repo.AssignIdentityAsync(faceId2, 3);

        var assigned = await repo.GetByIdentityAsync(3);

        await Assert.That(assigned).Count().IsEqualTo(1);
        await Assert.That(assigned[0].FaceId).IsEqualTo(faceId2);
    }

    [Test]
    public async Task DeleteByIdentity_RemovesFaces()
    {
        var (repo, detId) = await RepoAsync();
        var faceId = await repo.InsertAsync(new FaceRecord { DetectionId = detId, Confidence = 0.5 });
        await repo.AssignIdentityAsync(faceId, 5);

        await repo.DeleteByIdentityAsync(5);

        await Assert.That(await repo.GetByIdentityAsync(5)).IsEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
