using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkami DETECTIONS + ANNOTATIONS (FR-15 query, FR-16 flag).
/// </summary>
public sealed class DetectionRepository(SqliteConnection connection) : IDetectionRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<int> InsertAsync(Detection detection, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var id = await InsertOneAsync(detection, ct, conn);
        detection.DetectionId = id;
        return id;
    }

    public async Task InsertManyAsync(IReadOnlyList<Detection> detections, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var det in detections)
            {
                var id = await InsertOneAsync(det, ct, conn, (SqliteTransaction)tx);
                det.DetectionId = id;
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<int> InsertOneAsync(
        Detection d, CancellationToken ct, SqliteConnection conn, SqliteTransaction? tx = null)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO DETECTIONS (recording_id, camera_id, detection_type, timestamp_ms, duration_ms,
                confidence, algorithm_version, config_version_id, region_x, region_y, region_w, region_h,
                intensity, object_class, flag)
            VALUES ($recordingId, $cameraId, $detectionType, $timestampMs, $durationMs,
                $confidence, $algorithmVersion, $configVersionId, $regionX, $regionY, $regionW, $regionH,
                $intensity, $objectClass, $flag);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$recordingId", d.RecordingId);
        cmd.Parameters.AddWithValue("$cameraId", d.CameraId);
        cmd.Parameters.AddWithValue("$detectionType", d.DetectionType);
        cmd.Parameters.AddWithValue("$timestampMs", d.TimestampMs);
        cmd.Parameters.AddWithValue("$durationMs", d.DurationMs);
        cmd.Parameters.AddWithValue("$confidence", d.Confidence);
        cmd.Parameters.AddWithValue("$algorithmVersion", d.AlgorithmVersion);
        cmd.Parameters.AddWithValue("$configVersionId", (object?)d.ConfigVersionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$regionX", d.Region.X);
        cmd.Parameters.AddWithValue("$regionY", d.Region.Y);
        cmd.Parameters.AddWithValue("$regionW", d.Region.W);
        cmd.Parameters.AddWithValue("$regionH", d.Region.H);
        cmd.Parameters.AddWithValue("$intensity", d.Intensity);
        cmd.Parameters.AddWithValue("$objectClass", d.ObjectClass);
        cmd.Parameters.AddWithValue("$flag", d.Flag);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<Detection>> QueryAsync(int? cameraId, DateTime? from, DateTime? to,
        string? detectionType, string? flag, CancellationToken ct = default)
    {
        var (whereSql, parameters) = BuildWhere(cameraId, from, to, detectionType, flag);
        var sql = $"""
            SELECT d.* FROM DETECTIONS d
            JOIN RECORDINGS r ON r.recording_id = d.recording_id
            {whereSql}
            ORDER BY r.begin_time ASC, d.timestamp_ms ASC
            LIMIT 50000;
            """;

        var result = new List<Detection>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    /// <summary>Celkový počet detekcií podľa filtra (pre UI štatistiku — UI nemôže dostať 400k záznamov).</summary>
    public async Task<int> CountAsync(int? cameraId, DateTime? from, DateTime? to,
        string? detectionType, string? flag, CancellationToken ct = default)
    {
        var (whereSql, parameters) = BuildWhere(cameraId, from, to, detectionType, flag);
        var sql = $"""
            SELECT COUNT(*) FROM DETECTIONS d
            JOIN RECORDINGS r ON r.recording_id = d.recording_id
            {whereSql};
            """;

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static (string WhereSql, List<(string, object?)> Parameters) BuildWhere(
        int? cameraId, DateTime? from, DateTime? to, string? detectionType, string? flag)
    {
        var sql = "WHERE 1=1";
        var parameters = new List<(string, object?)>();
        if (cameraId is not null)
        {
            sql += " AND d.camera_id = $cameraId";
            parameters.Add(("$cameraId", cameraId.Value));
        }
        if (from is not null)
        {
            sql += " AND r.begin_time >= $from";
            parameters.Add(("$from", from.Value.ToString("O")));
        }
        if (to is not null)
        {
            sql += " AND r.begin_time <= $to";
            parameters.Add(("$to", to.Value.ToString("O")));
        }
        if (detectionType is not null)
        {
            sql += " AND d.detection_type = $type";
            parameters.Add(("$type", detectionType));
        }
        if (flag is not null)
        {
            sql += " AND d.flag = $flag";
            parameters.Add(("$flag", flag));
        }
        return (sql, parameters);
    }

    public async Task<IReadOnlyList<Detection>> GetByRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        var result = new List<Detection>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM DETECTIONS WHERE recording_id = $id ORDER BY timestamp_ms;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task SetFlagAsync(int detectionId, string flag, int? userId, DateTime flaggedAt, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE DETECTIONS
            SET flag = $flag, flagged_by = $userId, flagged_at = $flaggedAt
            WHERE detection_id = $id;
            """;
        cmd.Parameters.AddWithValue("$flag", flag);
        cmd.Parameters.AddWithValue("$userId", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$flaggedAt", flaggedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$id", detectionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);

        // Najprv anotácie (FK na DETECTIONS), potom samotné detekcie
        await using (var annotations = conn.CreateCommand())
        {
            annotations.CommandText = """
                DELETE FROM ANNOTATIONS
                WHERE detection_id IN (SELECT detection_id FROM DETECTIONS WHERE recording_id = $id);
                """;
            annotations.Parameters.AddWithValue("$id", recordingId);
            await annotations.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DETECTIONS WHERE recording_id = $id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Detection Map(SqliteDataReader r)
    {
        static float GetFloat(SqliteDataReader reader, string column) =>
            reader.IsDBNull(reader.GetOrdinal(column)) ? 0f : reader.GetFloat(reader.GetOrdinal(column));

        return new Detection
        {
            DetectionId = r.GetInt32(r.GetOrdinal("detection_id")),
            RecordingId = r.GetInt32(r.GetOrdinal("recording_id")),
            CameraId = r.GetInt32(r.GetOrdinal("camera_id")),
            DetectionType = r.GetString(r.GetOrdinal("detection_type")),
            TimestampMs = r.GetInt32(r.GetOrdinal("timestamp_ms")),
            DurationMs = r.GetInt32(r.GetOrdinal("duration_ms")),
            Confidence = GetFloat(r, "confidence"),
            AlgorithmVersion = r.GetString(r.GetOrdinal("algorithm_version")),
            ConfigVersionId = r.IsDBNull(r.GetOrdinal("config_version_id"))
                ? null : r.GetInt32(r.GetOrdinal("config_version_id")),
            Region = new NormalizedRegion(
                GetFloat(r, "region_x"), GetFloat(r, "region_y"),
                GetFloat(r, "region_w"), GetFloat(r, "region_h")),
            Intensity = GetFloat(r, "intensity"),
            ObjectClass = r.GetString(r.GetOrdinal("object_class")),
            Flag = r.GetString(r.GetOrdinal("flag")),
            FlaggedBy = r.IsDBNull(r.GetOrdinal("flagged_by"))
                ? null : r.GetInt32(r.GetOrdinal("flagged_by")),
            FlaggedAt = r.IsDBNull(r.GetOrdinal("flagged_at"))
                ? null : DateTime.Parse(r.GetString(r.GetOrdinal("flagged_at")),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
        };
    }
}
