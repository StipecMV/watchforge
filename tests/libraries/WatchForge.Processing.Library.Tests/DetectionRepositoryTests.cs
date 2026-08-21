using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-6: DetectionRepository — insert, query (čas, typ, flag), flag nastavenie.
/// </summary>
public class DetectionRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-detections-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private DetectionRepository? _repo;
    private int _recordingId;

    private async Task<DetectionRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new DetectionRepository(_connection);

        // Seed: NVR + kamera + recording + používateľ (pre flag FK)
        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
            INSERT INTO USERS (username, role) VALUES ('admin', 'admin');
            INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
            VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160);
            SELECT last_insert_rowid();
            """;
        _recordingId = Convert.ToInt32(await seed.ExecuteScalarAsync());
        return _repo;
    }

    private Detection NewDetection(int timestampMs, string type = "motion", string flag = "none") => new()
    {
        RecordingId = _recordingId,
        CameraId = 1,
        DetectionType = type,
        TimestampMs = timestampMs,
        DurationMs = 500,
        Confidence = 0.9f,
        AlgorithmVersion = "test-1.0",
        Region = new NormalizedRegion(0.1f, 0.2f, 0.3f, 0.4f),
        Intensity = 0.75f,
        ObjectClass = type == "person" ? "person" : "",
        Flag = flag,
    };

    [Test]
    public async Task InsertMany_ThenQueryByRecording_ReturnsAll()
    {
        // Given 3 detekcie
        var repo = await RepoAsync();
        var detections = new[]
        {
            NewDetection(1000), NewDetection(5000), NewDetection(9000)
        };

        // When vložíme
        await repo.InsertManyAsync(detections);

        // Then všetky sú v DB pre daný recording
        var loaded = await repo.GetByRecordingAsync(_recordingId);
        await Assert.That(loaded).Count().IsEqualTo(3);
        await Assert.That(loaded[0].TimestampMs).IsEqualTo(1000);
        await Assert.That(loaded[2].TimestampMs).IsEqualTo(9000);
    }

    [Test]
    public async Task Query_FilterByType_ReturnsOnlyMatching()
    {
        // Given motion + person detekcie
        var repo = await RepoAsync();
        await repo.InsertManyAsync(new[]
        {
            NewDetection(1000, "motion"), NewDetection(2000, "person"), NewDetection(3000, "motion")
        });

        // When query na person
        var persons = await repo.QueryAsync(cameraId: 1, from: null, to: null, detectionType: "person", flag: null);

        // Then len person
        await Assert.That(persons).Count().IsEqualTo(1);
        await Assert.That(persons[0].DetectionType).IsEqualTo("person");
    }

    [Test]
    public async Task Query_FilterByFlag_ReturnsFlagged()
    {
        // Given jedna flagged detekcia
        var repo = await RepoAsync();
        var flagged = NewDetection(1000);
        var normal = NewDetection(2000);
        await repo.InsertManyAsync(new[] { flagged, normal });

        // When niekto označí jednu (FR-16)
        await repo.SetFlagAsync(flagged.DetectionId, "false_positive", userId: 1, DateTime.UtcNow);

        // Then query flag false_positive vráti ju
        var flaggedOnes = await repo.QueryAsync(1, null, null, null, "false_positive");
        await Assert.That(flaggedOnes).Count().IsEqualTo(1);
        await Assert.That(flaggedOnes[0].DetectionId).IsEqualTo(flagged.DetectionId);
        await Assert.That(flaggedOnes[0].Flag).IsEqualTo("false_positive");
    }

    [Test]
    public async Task Insert_Single_ReturnsId_AndRegionSurvives()
    {
        // Given detekcia s normalizovaným regiónom
        var repo = await RepoAsync();
        var det = NewDetection(1500);

        // When vložíme
        var id = await repo.InsertAsync(det);

        // Then región prežije (koordináty 0..1)
        var loaded = await repo.GetByRecordingAsync(_recordingId);
        var single = loaded.Single(d => d.DetectionId == id);
        await Assert.That(single.Region.X).IsEqualTo(0.1f);
        await Assert.That(single.Region.Y).IsEqualTo(0.2f);
        await Assert.That(single.Region.W).IsEqualTo(0.3f);
        await Assert.That(single.Region.H).IsEqualTo(0.4f);
        await Assert.That(single.Intensity).IsEqualTo(0.75f);
    }

    [Test]
    public async Task DeleteForRecording_RemovesAll()
    {
        // Given detekcie v recording
        var repo = await RepoAsync();
        await repo.InsertManyAsync(new[] { NewDetection(1000), NewDetection(2000) });

        // When zmäžeme pre recording (retention cleanup)
        await repo.DeleteForRecordingAsync(_recordingId);

        // Then prázdne
        var loaded = await repo.GetByRecordingAsync(_recordingId);
        await Assert.That(loaded).IsEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
