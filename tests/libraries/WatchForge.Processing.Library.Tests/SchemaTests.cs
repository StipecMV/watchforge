using Microsoft.Data.Sqlite;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-2: overuje SQLite schému (tabuľky, indexy, WAL) podľa ER diagramu v docs/architecture.md.
/// Každý test beží na dočasnom DB súbore (reálna SQLite, nie in-memory mock).
/// </summary>
public class SchemaTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-schema-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;

    private async Task<SqliteConnection> OpenAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        return _connection;
    }

    [Test]
    public async Task OpenAsync_CreatesAllTables()
    {
        // Given fresh DB
        var conn = await OpenAsync();

        // When we list tables
        var tables = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        // Then all expected tables exist (ER diagram, architektúra sekcia 4)
        foreach (var expected in new[]
                 {
                     "NVRS", "CAMERAS", "RECORDINGS", "DETECTIONS", "ANNOTATIONS",
                     "FACES", "IDENTITIES", "CONFIG_VERSIONS", "USERS",
                     "PERSIST_FLAGS", "JOBS", "REQUESTS", "CLIPS"
                 })
        {
            await Assert.That(tables).Contains(expected);
        }
    }

    [Test]
    public async Task OpenAsync_EnablesWalMode()
    {
        // Given fresh DB
        var conn = await OpenAsync();

        // When we check journal mode
        string journalMode;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode";
            journalMode = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
        }

        // Then WAL is active (viacero čitateľov+zapisovateľov, NFR robustnosť)
        await Assert.That(journalMode.ToUpperInvariant()).IsEqualTo("WAL");
    }

    [Test]
    public async Task OpenAsync_CreatesRequiredIndexes()
    {
        // Given fresh DB
        var conn = await OpenAsync();

        // When we list indexes
        var indexes = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        }

        // Then key indexes exist (výkon dotazov)
        foreach (var expected in new[]
                 {
                     "IX_RECORDINGS_CAMERA_BEGIN", "IX_RECORDINGS_NVR_FILENAME",
                     "IX_RECORDINGS_AVAILABILITY_PURGE", "IX_DETECTIONS_CAMERA_TYPE_TIME",
                     "IX_DETECTIONS_RECORDING", "IX_DETECTIONS_FLAG",
                     "IX_JOBS_STATUS_PRIORITY", "IX_REQUESTS_CREATED", "IX_CLIPS_EXPIRES"
                 })
        {
            await Assert.That(indexes).Contains(expected);
        }
    }

    [Test]
    public async Task OpenAsync_CreatesUniqueIndexOnNvrFilename()
    {
        // Given fresh DB s NVR a kamerou (FK závislosť)
        var conn = await OpenAsync();
        await using (var seed = conn.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO NVRS (host, port, username, password_secret_env) VALUES ('192.168.68.10', 34567, 'nvr-user', 'WATCHFORGE_NVR_AUTH_FILE');
                INSERT INTO CAMERAS (nvr_id, channel, friendly_name) VALUES (1, 0, 'Dvor');
                """;
            await seed.ExecuteNonQueryAsync();
        }

        // When we insert the same nvr_filename twice
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
                VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160)
                """;
            await insert.ExecuteNonQueryAsync();
        }

        // Then duplicate insert fails (deduplikácia synchronizácie)
        var duplicateThrew = false;
        try
        {
            await using var dup = conn.CreateCommand();
            dup.CommandText = """
                INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
                VALUES (1, 1, 'segment', '[Ch0]_2026-04-05_15.00.00-15.15.mkv', '2026-04-05 15:00:00', '2026-04-05 15:15:00', 900, 100, 'hevc', 3840, 2160)
                """;
            await dup.ExecuteNonQueryAsync();
        }
        catch (SqliteException)
        {
            duplicateThrew = true;
        }
        await Assert.That(duplicateThrew).IsTrue();
    }

    [Test]
    public async Task OpenAsync_IsIdempotent()
    {
        // Given DB opened once
        await OpenAsync();

        // When opened again (reštart služby)
        await OpenAsync();

        // Then no error — schema is idempotent (CREATE IF NOT EXISTS)
        await Assert.That(true).IsTrue();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
            File.Delete(_dbPath + "-wal");
            File.Delete(_dbPath + "-shm");
        }
    }
}
