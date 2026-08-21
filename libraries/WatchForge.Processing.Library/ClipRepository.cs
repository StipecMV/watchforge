using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou CLIPS (S4-5 retention) — vygenerované klipy
/// s expiráciou (expires_at), ktoré cleanup job maže.
/// </summary>
public sealed class ClipRepository(SqliteConnection connection) : IClipRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<IReadOnlyList<Clip>> GetExpiredAsync(DateTime now, CancellationToken ct = default)
    {
        var result = new List<Clip>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CLIPS WHERE expires_at <= $now ORDER BY expires_at;";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task<IReadOnlyList<Clip>> GetByRequestAsync(int requestId, CancellationToken ct = default)
    {
        var result = new List<Clip>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CLIPS WHERE request_id = $id ORDER BY clip_id;";
        cmd.Parameters.AddWithValue("$id", requestId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    /// <summary>S22l: clipy patriace záznamu (pre purge — FK mazanie pred recordings).</summary>
    public async Task<IReadOnlyList<Clip>> GetByRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        var result = new List<Clip>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CLIPS WHERE recording_id = $id ORDER BY clip_id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task<Clip?> GetByIdAsync(int clipId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CLIPS WHERE clip_id = $id;";
        cmd.Parameters.AddWithValue("$id", clipId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> InsertAsync(Clip clip, CancellationToken ct = default)
    {
        clip.CreatedAt = DateTime.UtcNow;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CLIPS (request_id, recording_id, range_start, range_end, file_path, size_bytes, kind, created_at, expires_at)
            VALUES ($requestId, $recordingId, $start, $end, $path, $size, $kind, $created, $expires);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$requestId", clip.RequestId);
        cmd.Parameters.AddWithValue("$recordingId", (object?)clip.RecordingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$start", clip.RangeStart.ToString("O"));
        cmd.Parameters.AddWithValue("$end", clip.RangeEnd.ToString("O"));
        cmd.Parameters.AddWithValue("$path", clip.FilePath);
        cmd.Parameters.AddWithValue("$size", clip.SizeBytes);
        cmd.Parameters.AddWithValue("$kind", clip.Kind);
        cmd.Parameters.AddWithValue("$created", clip.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", clip.ExpiresAt.ToString("O"));
        clip.ClipId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return clip.ClipId;
    }

    public async Task DeleteAsync(int clipId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CLIPS WHERE clip_id = $id;";
        cmd.Parameters.AddWithValue("$id", clipId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Clip Map(SqliteDataReader reader) => new()
    {
        ClipId = reader.GetInt32(reader.GetOrdinal("clip_id")),
        RequestId = reader.GetInt32(reader.GetOrdinal("request_id")),
        RecordingId = reader.IsDBNull(reader.GetOrdinal("recording_id"))
            ? null : reader.GetInt32(reader.GetOrdinal("recording_id")),
        RangeStart = DateTime.Parse(reader.GetString(reader.GetOrdinal("range_start")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        RangeEnd = DateTime.Parse(reader.GetString(reader.GetOrdinal("range_end")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
        SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
        Kind = reader.GetString(reader.GetOrdinal("kind")),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        ExpiresAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("expires_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
