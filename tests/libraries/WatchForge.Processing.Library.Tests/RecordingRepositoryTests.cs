using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-5: RecordingRepository — CRUD, deduplikácia (nvr_filename), availability, purge.
/// </summary>
public class RecordingRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-recordings-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private RecordingRepository? _repo;

    private async Task<RecordingRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new RecordingRepository(_connection);
        // Seed: NVR + kamera
        await using var seed = _connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 1, 'Dvor2');
            """;
        await seed.ExecuteNonQueryAsync();
        return _repo;
    }

    private static Recording NewRecording(string filename, DateTime begin, DateTime end, int cameraId = 1) => new()
    {
        NvrId = 1,
        CameraId = cameraId,
        SourceType = "segment",
        NvrFilename = filename,
        BeginTime = begin,
        EndTime = end,
        DurationSec = (int)(end - begin).TotalSeconds,
        SizeBytes = 1000,
        Codec = "hevc",
        Width = 3840,
        Height = 2160,
    };

    [Test]
    public async Task Insert_ThenGetById_ReturnsSame()
    {
        // Given nový záznam
        var repo = await RepoAsync();
        var rec = NewRecording("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc));

        // When vložíme a načítame
        var id = await repo.InsertAsync(rec);
        var loaded = await repo.GetByIdAsync(id);

        // Then rovnaké dáta
        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.NvrFilename).IsEqualTo(rec.NvrFilename);
        await Assert.That(loaded.CameraId).IsEqualTo(1);
        await Assert.That(loaded.BeginTime).IsEqualTo(rec.BeginTime);
        await Assert.That(loaded.DurationSec).IsEqualTo(900);
        await Assert.That(loaded.Availability).IsEqualTo("available");
        await Assert.That(loaded.AnalysisState).IsEqualTo("queued");
    }

    [Test]
    public async Task Insert_DuplicateNvrFilename_ReturnsExistingId()
    {
        // Given záznam vložený raz
        var repo = await RepoAsync();
        var rec = NewRecording("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc));
        var id1 = await repo.InsertAsync(rec);

        // When vložíme rovnaký názov (synchronizácia behne 2x)
        var id2 = await repo.InsertAsync(rec);

        // Then dostaneme existujúce id (idempotencia, žiadny duplicitný riadok)
        await Assert.That(id2).IsEqualTo(id1);
        var all = await repo.QueryAsync(null, null, null, null);
        await Assert.That(all).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Query_FilterByCameraAndTime_ReturnsMatching()
    {
        // Given dva záznamy na rôznych kamerách a časoch
        var repo = await RepoAsync();
        var cam0 = NewRecording("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc), cameraId: 1);
        var cam1 = NewRecording("[Ch1]_2026-04-05_16.00.00-16.15.mkv",
            new DateTime(2026, 4, 5, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 16, 15, 0, DateTimeKind.Utc), cameraId: 2);
        await repo.InsertAsync(cam0);
        await repo.InsertAsync(cam1);

        // When query na kameru 2 v čase 15:30–16:30
        var result = await repo.QueryAsync(cameraId: 2,
            from: new DateTime(2026, 4, 5, 15, 30, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 4, 5, 16, 30, 0, DateTimeKind.Utc),
            sourceType: null);

        // Then len záznam kamery 2
        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].CameraId).IsEqualTo(2);
    }

    [Test]
    public async Task GetByNvrFilename_FindsOrReturnsNull()
    {
        // Given záznam v DB
        var repo = await RepoAsync();
        var rec = NewRecording("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc));
        await repo.InsertAsync(rec);

        // When hľadáme podľa názvu
        var found = await repo.GetByNvrFilenameAsync(rec.NvrFilename);
        var missing = await repo.GetByNvrFilenameAsync("neexistuje.mkv");

        // Then nájde / null
        await Assert.That(found).IsNotNull();
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task MarkUnavailable_SetsStateAndTimestamp()
    {
        // Given záznam
        var repo = await RepoAsync();
        var rec = NewRecording("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc));
        var id = await repo.InsertAsync(rec);

        // When označíme ako nedostupný
        await repo.UpdateAsync(rec); // placeholder volanie pre konzistenciu rozhrania
        var loaded = await repo.GetByIdAsync(id);
        loaded!.Availability = "unavailable";
        loaded.UnavailableSince = new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc);
        await repo.UpdateAsync(loaded);

        // Then stav je v DB
        var after = await repo.GetByIdAsync(id);
        await Assert.That(after!.Availability).IsEqualTo("unavailable");
        await Assert.That(after.UnavailableSince).IsEqualTo(loaded.UnavailableSince);
    }

    [Test]
    public async Task GetExpiredForPurge_ReturnsOnlyExpired()
    {
        // Given jeden záznam s purge_at v minulosti a jeden v budúcnosti
        var repo = await RepoAsync();
        var expired = NewRecording("[Ch0]_2026-04-01_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 1, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 15, 15, 0, DateTimeKind.Utc));
        var future = NewRecording("[Ch0]_2026-04-02_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 2, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 2, 15, 15, 0, DateTimeKind.Utc));
        var id1 = await repo.InsertAsync(expired);
        var id2 = await repo.InsertAsync(future);

        var e1 = await repo.GetByIdAsync(id1);
        e1!.Availability = "unavailable";
        e1.PurgeAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc); // minulosť voči "now"
        await repo.UpdateAsync(e1);
        var e2 = await repo.GetByIdAsync(id2);
        e2!.Availability = "unavailable";
        e2.PurgeAt = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc); // budúcnosť
        await repo.UpdateAsync(e2);

        // When purge query s now = 2026-06-01
        var due = await repo.GetExpiredForPurgeAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        // Then len ten s purge_at v minulosti
        await Assert.That(due).Count().IsEqualTo(1);
        await Assert.That(due[0].RecordingId).IsEqualTo(id1);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
