using Microsoft.Data.Sqlite;

namespace WatchForge.Processing.Library;

/// <summary>
/// SQLite databáza WatchForge — jediný zdroj pravdy (FR-07).
/// Schéma podľa ER diagramu v docs/architecture.md (sekcia 4).
/// WAL mode: viacero čitateľov + 1 zapisovateľ (service aj API pristupujú k tomu istému súboru).
/// Všetky časy v UTC. Koordináty regiónov normalizované 0..1 (4K fram).
/// </summary>
public static class WatchForgeDatabase
{
    public const string SchemaVersion = "0.1";

    /// <summary>Otvorí (a prípadne vytvorí) databázu s kompletnou schémou. Idempotentné.</summary>
    public static Task<SqliteConnection> OpenAsync(string dbPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        return OpenConnectionStringAsync($"Data Source={dbPath}", ct);
    }

    /// <summary>
    /// Otvorí spojenie z connection stringu so všetkými PRAGMA nastaveniami (WAL, FK, busy_timeout).
    /// Používa sa per-operation (thread-safe prístup k SQLite — zdieľaný SqliteConnection nie je
    /// thread-safe a Runner ho používa paralelne). Schéma sa aplikuje idempotentne (user_version).
    /// WAL sa nastavuje LEN pri vytvorení databázy (user_version=0) — PRAGMA journal_mode=WAL
    /// pri každom otvorení by počas súbežných transakcií vyžadovala exkluzívny zámok → „database is locked".
    /// </summary>
    public static async Task<SqliteConnection> OpenConnectionStringAsync(
        string connectionString, CancellationToken ct = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var meta = connection.CreateCommand())
        {
            meta.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt64(await meta.ExecuteScalarAsync(ct));
            if (version < 1)
            {
                await using (var wal = connection.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode=WAL;";
                    await wal.ExecuteNonQueryAsync(ct);
                }

                await ApplySchemaAsync(connection, ct);
            }
            else
            {
                // S22l: existujúca DB (version >= 1) — spustiť migrácie (idempotentné)
                await ApplySchemaAsync(connection, ct);
            }
        }

        await using (var fk = connection.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_keys=ON;";
            await fk.ExecuteNonQueryAsync(ct);
        }

        await using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            await busy.ExecuteNonQueryAsync(ct);
        }

        return connection;
    }

    private static async Task ApplySchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        // ── Tabuľky (ER diagram, architektúra sekcia 4) ──────────────────────
        const string schema = """
            CREATE TABLE IF NOT EXISTS NVRS (
                nvr_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                site_id      TEXT    NOT NULL DEFAULT 'site-a',
                host         TEXT    NOT NULL,
                port         INTEGER NOT NULL,
                username     TEXT    NOT NULL,
                password_secret_env TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CAMERAS (
                camera_id     INTEGER PRIMARY KEY AUTOINCREMENT,
                nvr_id        INTEGER NOT NULL REFERENCES NVRS(nvr_id),
                channel       INTEGER NOT NULL,
                friendly_name TEXT    NOT NULL,
                icon_id       TEXT    NOT NULL DEFAULT 'camera',
                is_active     INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS RECORDINGS (
                recording_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                nvr_id             INTEGER NOT NULL REFERENCES NVRS(nvr_id),
                camera_id          INTEGER NOT NULL REFERENCES CAMERAS(camera_id),
                source_type        TEXT    NOT NULL DEFAULT 'segment',
                nvr_filename       TEXT    NOT NULL,
                begin_time         TEXT    NOT NULL,
                end_time           TEXT    NOT NULL,
                duration_sec       INTEGER NOT NULL,
                size_bytes         INTEGER NOT NULL DEFAULT 0,
                codec              TEXT    NOT NULL DEFAULT 'hevc',
                width              INTEGER NOT NULL DEFAULT 3840,
                height             INTEGER NOT NULL DEFAULT 2160,
                availability       TEXT    NOT NULL DEFAULT 'available',
                unavailable_since  TEXT,
                purge_at           TEXT,
                persisted          INTEGER NOT NULL DEFAULT 0,
                person_pending     INTEGER NOT NULL DEFAULT 0,
                window_start_utc   TEXT,
                config_version_id  INTEGER REFERENCES CONFIG_VERSIONS(config_version_id),
                analysis_state     TEXT    NOT NULL DEFAULT 'queued',
                analysis_started_at TEXT,
                analysis_completed_at TEXT,
                error              TEXT,
                created_at         TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                updated_at         TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );

            CREATE TABLE IF NOT EXISTS DETECTIONS (
                detection_id      INTEGER PRIMARY KEY AUTOINCREMENT,
                recording_id      INTEGER NOT NULL REFERENCES RECORDINGS(recording_id),
                camera_id         INTEGER NOT NULL REFERENCES CAMERAS(camera_id),
                detection_type    TEXT    NOT NULL DEFAULT 'motion',
                timestamp_ms      INTEGER NOT NULL,
                duration_ms       INTEGER NOT NULL DEFAULT 0,
                confidence        REAL    NOT NULL DEFAULT 0,
                algorithm_version TEXT    NOT NULL DEFAULT '',
                config_version_id INTEGER REFERENCES CONFIG_VERSIONS(config_version_id),
                region_x          REAL    NOT NULL,
                region_y          REAL    NOT NULL,
                region_w          REAL    NOT NULL,
                region_h          REAL    NOT NULL,
                intensity         REAL    NOT NULL DEFAULT 0,
                object_class      TEXT    NOT NULL DEFAULT '',
                flag              TEXT    NOT NULL DEFAULT 'none',
                flagged_by        INTEGER REFERENCES USERS(user_id),
                flagged_at        TEXT
            );

            CREATE TABLE IF NOT EXISTS ANNOTATIONS (
                annotation_id INTEGER PRIMARY KEY AUTOINCREMENT,
                detection_id  INTEGER NOT NULL REFERENCES DETECTIONS(detection_id),
                user_id       INTEGER NOT NULL REFERENCES USERS(user_id),
                region_x      REAL    NOT NULL,
                region_y      REAL    NOT NULL,
                region_w      REAL    NOT NULL,
                region_h      REAL    NOT NULL,
                label         TEXT    NOT NULL DEFAULT '',
                created_at    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );

            CREATE TABLE IF NOT EXISTS FACES (
                face_id        INTEGER PRIMARY KEY AUTOINCREMENT,
                detection_id   INTEGER NOT NULL REFERENCES DETECTIONS(detection_id),
                identity_id    INTEGER REFERENCES IDENTITIES(identity_id),
                embedding      BLOB,
                confidence     REAL NOT NULL DEFAULT 0,
                temp_crop_path TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS IDENTITIES (
                identity_id INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT NOT NULL,
                created_by  INTEGER REFERENCES USERS(user_id),
                created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );

            CREATE TABLE IF NOT EXISTS CONFIG_VERSIONS (
                config_version_id   INTEGER PRIMARY KEY AUTOINCREMENT,
                camera_id           INTEGER REFERENCES CAMERAS(camera_id),
                profile_name        TEXT NOT NULL DEFAULT 'default',
                profile_type        TEXT NOT NULL DEFAULT 'per_camera',
                sensitivity         REAL NOT NULL DEFAULT 0.5,
                intensity_threshold REAL NOT NULL DEFAULT 0.02,
                min_contour_area    REAL NOT NULL DEFAULT 0.001,
                ignore_zones_json   TEXT NOT NULL DEFAULT '[]',
                focus_zones_json    TEXT NOT NULL DEFAULT '[]',
                is_active           INTEGER NOT NULL DEFAULT 1,
                created_at          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );

            CREATE TABLE IF NOT EXISTS USERS (
                user_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                username      TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL DEFAULT '',
                role          TEXT NOT NULL DEFAULT 'standard',
                avatar_id     INTEGER NOT NULL DEFAULT 0,
                locale        TEXT NOT NULL DEFAULT 'sk',
                created_at    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );

            CREATE TABLE IF NOT EXISTS PERSIST_FLAGS (
                persist_id   INTEGER PRIMARY KEY AUTOINCREMENT,
                recording_id INTEGER NOT NULL REFERENCES RECORDINGS(recording_id),
                user_id      INTEGER NOT NULL REFERENCES USERS(user_id),
                scope        TEXT NOT NULL DEFAULT 'recording',
                range_start  TEXT,
                range_end    TEXT,
                note         TEXT NOT NULL DEFAULT '',
                created_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                removed_at   TEXT
            );

            CREATE TABLE IF NOT EXISTS JOBS (
                job_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                recording_id INTEGER REFERENCES RECORDINGS(recording_id),
                request_id   INTEGER REFERENCES REQUESTS(request_id),
                type         TEXT NOT NULL,
                priority     INTEGER NOT NULL DEFAULT 10,
                source       TEXT NOT NULL DEFAULT 'background',
                status       TEXT NOT NULL DEFAULT 'queued',
                payload      TEXT,
                progress     INTEGER NOT NULL DEFAULT 0,
                attempts     INTEGER NOT NULL DEFAULT 0,
                error        TEXT,
                created_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                started_at   TEXT,
                finished_at  TEXT
            );

            CREATE TABLE IF NOT EXISTS REQUESTS (
                request_id           INTEGER PRIMARY KEY AUTOINCREMENT,
                source               TEXT NOT NULL,
                requester            TEXT NOT NULL DEFAULT '',
                query                TEXT NOT NULL DEFAULT '',
                from_time            TEXT NOT NULL,
                to_time              TEXT NOT NULL,
                camera_id            INTEGER REFERENCES CAMERAS(camera_id),
                detection_type_filter TEXT,
                context_before_sec   INTEGER NOT NULL DEFAULT 15,
                context_after_sec    INTEGER NOT NULL DEFAULT 15,
                status               TEXT NOT NULL DEFAULT 'queued',
                estimate             TEXT NOT NULL DEFAULT '',
                created_at           TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                completed_at         TEXT
            );

            CREATE TABLE IF NOT EXISTS CLIPS (
                clip_id      INTEGER PRIMARY KEY AUTOINCREMENT,
                request_id   INTEGER NOT NULL REFERENCES REQUESTS(request_id),
                recording_id INTEGER REFERENCES RECORDINGS(recording_id),
                range_start  TEXT NOT NULL,
                range_end    TEXT NOT NULL,
                file_path    TEXT NOT NULL,
                size_bytes   INTEGER NOT NULL DEFAULT 0,
                kind         TEXT NOT NULL DEFAULT 'video',
                created_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                expires_at   TEXT NOT NULL
            );
            """;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = schema;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ── Indexy (výkon dotazov, architektúra sekcia 4) ────────────────────
        const string indexes = """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_RECORDINGS_NVR_FILENAME ON RECORDINGS(nvr_filename);
            CREATE INDEX IF NOT EXISTS IX_RECORDINGS_CAMERA_BEGIN ON RECORDINGS(camera_id, begin_time);
            CREATE INDEX IF NOT EXISTS IX_RECORDINGS_AVAILABILITY_PURGE ON RECORDINGS(availability, purge_at);
            CREATE INDEX IF NOT EXISTS IX_DETECTIONS_CAMERA_TYPE_TIME ON DETECTIONS(camera_id, detection_type, timestamp_ms);
            CREATE INDEX IF NOT EXISTS IX_DETECTIONS_RECORDING ON DETECTIONS(recording_id);
            CREATE INDEX IF NOT EXISTS IX_DETECTIONS_FLAG ON DETECTIONS(flag);
            CREATE INDEX IF NOT EXISTS IX_JOBS_STATUS_PRIORITY ON JOBS(status, priority);
            CREATE INDEX IF NOT EXISTS IX_REQUESTS_CREATED ON REQUESTS(created_at);
            CREATE INDEX IF NOT EXISTS IX_CLIPS_EXPIRES ON CLIPS(expires_at);
            """;

        await using (var idx = connection.CreateCommand())
        {
            idx.CommandText = indexes;
            await idx.ExecuteNonQueryAsync(ct);
        }

        // ── Migrácie (S22l: person_pending pre skip pre cleanup) ──────────────
        // Idempotentné: ak stĺpec už existuje (nová DB ho má z CREATE TABLE), nič sa nerobí.
        await using (var hasCol = connection.CreateCommand())
        {
            hasCol.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RECORDINGS') WHERE name = 'person_pending';";
            var exists = Convert.ToInt64(await hasCol.ExecuteScalarAsync(ct));
            if (exists == 0)
            {
                await using var mig = connection.CreateCommand();
                mig.CommandText = "ALTER TABLE RECORDINGS ADD COLUMN person_pending INTEGER NOT NULL DEFAULT 0;";
                await mig.ExecuteNonQueryAsync(ct);
            }
        }
        // S22n: window_start_utc — začiatok 15-min okna, na ktoré sa segment oreže
        // (UI ho potrebuje na sync udalostí s prehrávaním trimnutého videa).
        await using (var hasWin = connection.CreateCommand())
        {
            hasWin.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RECORDINGS') WHERE name = 'window_start_utc';";
            var winExists = Convert.ToInt64(await hasWin.ExecuteScalarAsync(ct));
            if (winExists == 0)
            {
                await using var migWin = connection.CreateCommand();
                migWin.CommandText = "ALTER TABLE RECORDINGS ADD COLUMN window_start_utc TEXT;";
                await migWin.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var version = connection.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version = 3;";
            await version.ExecuteNonQueryAsync(ct);
        }
    }
}
