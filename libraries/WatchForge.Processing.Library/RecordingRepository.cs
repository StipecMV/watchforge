using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou RECORDINGS (FR-01 sync, FR-13 retention).
/// Insert je idempotentný podľa nvr_filename (UNIQUE index) — synchronizácia môže bežať opakovane.
/// </summary>
public sealed class RecordingRepository(SqliteConnection connection) : IRecordingRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<Recording?> GetByIdAsync(int recordingId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE recording_id = $id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Recording?> GetByNvrFilenameAsync(string nvrFilename, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE nvr_filename = $name;";
        cmd.Parameters.AddWithValue("$name", nvrFilename);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Recording>> QueryAsync(int? cameraId, DateTime? from, DateTime? to,
        string? sourceType, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM RECORDINGS WHERE 1=1";
        var parameters = new List<(string, object?)>();
        if (cameraId is not null)
        {
            sql += " AND camera_id = $cameraId";
            parameters.Add(("$cameraId", cameraId.Value));
        }
        if (from is not null)
        {
            sql += " AND end_time >= $from";
            parameters.Add(("$from", from.Value.ToString("O")));
        }
        if (to is not null)
        {
            sql += " AND begin_time <= $to";
            parameters.Add(("$to", to.Value.ToString("O")));
        }
        if (sourceType is not null)
        {
            sql += " AND source_type = $sourceType";
            parameters.Add(("$sourceType", sourceType));
        }
        sql += " ORDER BY begin_time;";

        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task<int> InsertAsync(Recording recording, CancellationToken ct = default)
    {
        // Idempotencia: ak nvr_filename už existuje, vrátime existujúce id (žiadny duplicitný riadok)
        var existing = await GetByNvrFilenameAsync(recording.NvrFilename, ct);
        if (existing is not null)
            return existing.RecordingId;

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time,
                duration_sec, size_bytes, codec, width, height, availability, unavailable_since, purge_at,
                persisted, person_pending, window_start_utc, analysis_state)
            VALUES ($nvrId, $cameraId, $sourceType, $nvrFilename, $beginTime, $endTime,
                $durationSec, $sizeBytes, $codec, $width, $height, $availability, $unavailableSince, $purgeAt,
                $persisted, $personPending, $windowStartUtc, $analysisState);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$nvrId", recording.NvrId);
        cmd.Parameters.AddWithValue("$cameraId", recording.CameraId);
        cmd.Parameters.AddWithValue("$sourceType", recording.SourceType);
        cmd.Parameters.AddWithValue("$nvrFilename", recording.NvrFilename);
        cmd.Parameters.AddWithValue("$beginTime", recording.BeginTime.ToString("O"));
        cmd.Parameters.AddWithValue("$endTime", recording.EndTime.ToString("O"));
        cmd.Parameters.AddWithValue("$durationSec", recording.DurationSec);
        cmd.Parameters.AddWithValue("$sizeBytes", recording.SizeBytes);
        cmd.Parameters.AddWithValue("$codec", recording.Codec);
        cmd.Parameters.AddWithValue("$width", recording.Width);
        cmd.Parameters.AddWithValue("$height", recording.Height);
        cmd.Parameters.AddWithValue("$availability", recording.Availability);
        cmd.Parameters.AddWithValue("$unavailableSince", (object?)recording.UnavailableSince?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$purgeAt", (object?)recording.PurgeAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$persisted", recording.Persisted ? 1 : 0);
        cmd.Parameters.AddWithValue("$personPending", recording.PersonPending ? 1 : 0);
        cmd.Parameters.AddWithValue("$windowStartUtc", (object?)recording.WindowStartUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$analysisState", recording.AnalysisState);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdateAsync(Recording recording, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE RECORDINGS
            SET availability = $availability, unavailable_since = $unavailableSince, purge_at = $purgeAt,
                persisted = $persisted, person_pending = $personPending, window_start_utc = $windowStartUtc,
                analysis_state = $analysisState,
                analysis_started_at = $analysisStartedAt, analysis_completed_at = $analysisCompletedAt,
                error = $error, updated_at = $updatedAt
            WHERE recording_id = $id;
            """;
        cmd.Parameters.AddWithValue("$availability", recording.Availability);
        cmd.Parameters.AddWithValue("$unavailableSince", (object?)recording.UnavailableSince?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$purgeAt", (object?)recording.PurgeAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$persisted", recording.Persisted ? 1 : 0);
        cmd.Parameters.AddWithValue("$personPending", recording.PersonPending ? 1 : 0);
        cmd.Parameters.AddWithValue("$windowStartUtc", (object?)recording.WindowStartUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$analysisState", recording.AnalysisState);
        cmd.Parameters.AddWithValue("$analysisStartedAt", (object?)recording.AnalysisStartedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$analysisCompletedAt", (object?)recording.AnalysisCompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)recording.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", recording.RecordingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Recording>> GetByAvailabilityAsync(string availability, CancellationToken ct = default)
    {
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE availability = $availability ORDER BY begin_time;";
        cmd.Parameters.AddWithValue("$availability", availability);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task<IReadOnlyList<Recording>> GetExpiredForPurgeAsync(DateTime now, CancellationToken ct = default)
    {
        // Záznamy, ktorým už prešiel purge_at (retention: ~mesiac po unavailable).
        // persist výnimka: user označené (persisted=1) sa NIKDY automaticky nemažú.
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE purge_at IS NOT NULL AND purge_at <= $now AND persisted = 0 ORDER BY purge_at;";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    /// <summary>
    /// S22l: recordings mimo rolling okien (begin_time < cutoff) — lokálna retencia.
    /// Chránené (persisted/person_pending) sa filtrujú v PurgeJobHandler.
    /// </summary>
    public async Task<IReadOnlyList<Recording>> GetExpiredForWindowRetentionAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE begin_time < $cutoff ORDER BY begin_time;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task DeleteAsync(int recordingId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RECORDINGS WHERE recording_id = $id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>S12-1: počet záznamov čakajúcich na analýzu (backlog).</summary>
    public async Task<int> CountBacklogAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM RECORDINGS
            WHERE analysis_state != 'completed' AND analysis_state != 'failed';
            """;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>S12-1: celkový počet záznamov + počet dokončených analýz.</summary>
    public async Task<(int Total, int Completed)> CountAllAndCompletedAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), SUM(CASE WHEN analysis_state = 'completed' THEN 1 ELSE 0 END)
            FROM RECORDINGS;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var completed = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return (total, completed);
        }
        return (0, 0);
    }

    /// <summary>
    /// Záznamy čakajúce na analýzu (analysis_state='queued' alebo 'failed'), ktoré NEMAJÚ
    /// aktívny Analyze job (queued/running) — dedup pre auto-plánovač. Zoradené od najstarších.
    /// </summary>
    public async Task<IReadOnlyList<Recording>> GetPendingAnalysisAsync(int limit, CancellationToken ct = default)
    {
        // IBA záznamy, ktoré NVR STÁLE MÁ (availability='available') — unavailable/missed
        // sa neanalyzujú (video nie je k dispozícii, job by zlyhal a zahlcoval poradovník).
        var sql = """
            SELECT r.* FROM RECORDINGS r
            WHERE r.analysis_state IN ('queued', 'failed')
              AND r.availability = 'available'
              AND NOT EXISTS (
                SELECT 1 FROM JOBS j
                WHERE j.recording_id = r.recording_id
                  AND j.type = 'analyze'
                  AND j.status IN ('queued', 'running')
              )
            ORDER BY r.begin_time
            LIMIT $limit;
            """;
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    /// <summary>S12-1: čas posledného synchronizovaného záznamu (najnovší begin_time).</summary>
    public async Task<DateTime?> GetLastSyncAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(begin_time) FROM RECORDINGS;";
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is DBNull or null ? null : DateTime.Parse((string)raw);
    }

    /// <summary>
    /// S22l: záznamy najnovšieho AKTÍVNEHO okna (15-min kvartál), naprieč kamerami.
    /// Okno = [start, end); aktívne = má aspoň 1 záznam s analysis_state != 'completed'.
    /// Iba DOKONČENÉ segmenty (end_time <= now — NVR ešte nahráva aktuálny kvartál).
    /// Keď aktívne okno má všetky kamery s jobom (spracúva sa), vracia prázdne (čaká sa) —
    /// staršie okná sa NEdorábajú (nestíhali by sme ani novšie). Kamera offline = nemá záznam.
    /// </summary>
    public async Task<IReadOnlyList<Recording>> GetLatestWindowAsync(int windowMinutes, DateTime now,
        int maxWindows, CancellationToken ct = default)
    {
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        // Najnovší uzavretý kvartál: zaokrúhliť nadol na windowMinutes od začiatku epochy
        var latestEnd = FloorToWindow(now, windowMinutes);
        for (var i = 0; i < maxWindows; i++)
        {
            var end = latestEnd.AddMinutes(-windowMinutes * i);
            var start = end.AddMinutes(-windowMinutes);

            // Má okno vôbec čo analyzovať? (ne-completed available záznam — bez job filtru)
            await using var hasCmd = conn.CreateCommand();
            hasCmd.CommandText = """
                SELECT COUNT(*) FROM RECORDINGS r
                WHERE r.begin_time < $end AND r.end_time > $start
                  AND r.end_time <= $now
                  AND r.availability = 'available'
                  AND r.analysis_state != 'completed';
                """;
            hasCmd.Parameters.AddWithValue("$start", start.ToString("O"));
            hasCmd.Parameters.AddWithValue("$end", end.ToString("O"));
            hasCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            var pending = Convert.ToInt32(await hasCmd.ExecuteScalarAsync(ct));
            if (pending == 0)
                continue; // okno hotové/prázdne → starší kvartál

            // Aktívne okno — záznamy bez aktívneho analyze jobu (enqueue; 0 = všetky bežia → čakať)
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT r.* FROM RECORDINGS r
                WHERE r.begin_time < $end AND r.end_time > $start
                  AND r.end_time <= $now
                  AND r.availability = 'available'
                  AND r.analysis_state != 'completed'
                  AND NOT EXISTS (
                    SELECT 1 FROM JOBS j
                    WHERE j.recording_id = r.recording_id
                      AND j.type = 'analyze'
                      AND j.status IN ('queued', 'running')
                  )
                ORDER BY r.camera_id;
                """;
            cmd.Parameters.AddWithValue("$start", start.ToString("O"));
            cmd.Parameters.AddWithValue("$end", end.ToString("O"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(Map(reader));
            return result; // prvé aktívne okno (najnovšie) — staršie sa nedorábajú
        }
        return result;
    }

    /// <summary>
    /// S22l: analyzované okná — záznamy s completed analýzou zoskupené po kvartáloch,
    /// s počtom kamier a detekcií (pre MCP/UI „čo sa dá prehrať"). Iba posledných maxWindows;
    /// OKREM chránených nahrávok (person_pending/persisted), ktoré purge necháva — tie sú vždy viditeľné.
    /// </summary>
    public async Task<IReadOnlyList<AnalyzedWindow>> GetAnalyzedWindowsAsync(int windowMinutes, DateTime now,
        int maxWindows, CancellationToken ct = default)
    {
        var result = new List<AnalyzedWindow>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                (CAST(strftime('%s', r.begin_time) AS INTEGER) / $windowSec) * $windowSec AS window_start_sec,
                COUNT(DISTINCT r.camera_id) AS cameras
            FROM RECORDINGS r
            WHERE r.analysis_state = 'completed'
              AND (r.begin_time >= $minTime OR r.person_pending = 1 OR r.persisted = 1)
            GROUP BY window_start_sec
            ORDER BY window_start_sec DESC
            LIMIT $maxWindows;
            """;
        var windowSec = windowMinutes * 60;
        cmd.Parameters.AddWithValue("$windowSec", windowSec);
        cmd.Parameters.AddWithValue("$minTime", now.AddMinutes(-windowMinutes * maxWindows).ToString("O"));
        cmd.Parameters.AddWithValue("$maxWindows", maxWindows);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var startSec = reader.GetInt64(0);
            var start = DateTimeOffset.FromUnixTimeSeconds(startSec).UtcDateTime;
            var cameras = reader.GetInt32(1);
            var dets = await CountDetectionsForWindowAsync(start, start.AddMinutes(windowMinutes), ct);
            var persons = await CountPersonsForWindowAsync(start, start.AddMinutes(windowMinutes), ct);
            result.Add(new AnalyzedWindow(start, start.AddMinutes(windowMinutes), cameras, dets, persons > 0));
        }
        return result;
    }

    private async Task<int> CountPersonsForWindowAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM DETECTIONS d
            JOIN RECORDINGS r ON d.recording_id = r.recording_id
            WHERE d.detection_type = 'person'
              AND r.begin_time < $end AND r.end_time > $start;
            """;
        cmd.Parameters.AddWithValue("$start", start.ToString("O"));
        cmd.Parameters.AddWithValue("$end", end.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<int> CountDetectionsForWindowAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM DETECTIONS d
            JOIN RECORDINGS r ON d.recording_id = r.recording_id
            WHERE r.begin_time < $end AND r.end_time > $start;
            """;
        cmd.Parameters.AddWithValue("$start", start.ToString("O"));
        cmd.Parameters.AddWithValue("$end", end.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>S22l: nahrávky s nájdenou osobou čakajúce na potvrdenie (skip pre cleanup).</summary>
    public async Task<IReadOnlyList<Recording>> GetPersonPendingAsync(CancellationToken ct = default)
    {
        var result = new List<Recording>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RECORDINGS WHERE person_pending = 1 AND availability = 'available' ORDER BY begin_time;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    private static DateTime FloorToWindow(DateTime time, int windowMinutes)
    {
        var utc = time.ToUniversalTime();
        var minutes = (long)(utc - DateTime.UnixEpoch).TotalMinutes;
        minutes -= minutes % windowMinutes;
        return DateTime.UnixEpoch.AddMinutes(minutes);
    }

    private static Recording Map(SqliteDataReader r)
    {
        static DateTime? GetDate(SqliteDataReader reader, string column) =>
            reader.IsDBNull(reader.GetOrdinal(column))
                ? null
                : DateTime.Parse(reader.GetString(reader.GetOrdinal(column)),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind);

        return new Recording
        {
            RecordingId = r.GetInt32(r.GetOrdinal("recording_id")),
            NvrId = r.GetInt32(r.GetOrdinal("nvr_id")),
            CameraId = r.GetInt32(r.GetOrdinal("camera_id")),
            SourceType = r.GetString(r.GetOrdinal("source_type")),
            NvrFilename = r.GetString(r.GetOrdinal("nvr_filename")),
            BeginTime = DateTime.Parse(r.GetString(r.GetOrdinal("begin_time")),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            EndTime = DateTime.Parse(r.GetString(r.GetOrdinal("end_time")),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            DurationSec = r.GetInt32(r.GetOrdinal("duration_sec")),
            SizeBytes = r.GetInt64(r.GetOrdinal("size_bytes")),
            Codec = r.GetString(r.GetOrdinal("codec")),
            Width = r.GetInt32(r.GetOrdinal("width")),
            Height = r.GetInt32(r.GetOrdinal("height")),
            Availability = r.GetString(r.GetOrdinal("availability")),
            UnavailableSince = GetDate(r, "unavailable_since"),
            PurgeAt = GetDate(r, "purge_at"),
            Persisted = r.GetInt32(r.GetOrdinal("persisted")) != 0,
            PersonPending = r.GetInt32(r.GetOrdinal("person_pending")) != 0,
            WindowStartUtc = GetDate(r, "window_start_utc"),
            AnalysisState = r.GetString(r.GetOrdinal("analysis_state")),
            AnalysisStartedAt = GetDate(r, "analysis_started_at"),
            AnalysisCompletedAt = GetDate(r, "analysis_completed_at"),
            Error = r.IsDBNull(r.GetOrdinal("error")) ? null : r.GetString(r.GetOrdinal("error")),
        };
    }
}
