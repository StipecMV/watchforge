using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou PERSIST_FLAGS (S5-6) — „zachovaj záznam" výnimka
/// pre retention (PurgeJobHandler persisted=1 nikdy nemaže).
/// </summary>
public sealed class PersistRepository(SqliteConnection connection) : IPersistRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<int> InsertAsync(PersistFlag flag, CancellationToken ct = default)
    {
        flag.CreatedAt = DateTime.UtcNow;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PERSIST_FLAGS (recording_id, user_id, scope, range_start, range_end, note, created_at)
            VALUES ($recordingId, $userId, $scope, $rangeStart, $rangeEnd, $note, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$recordingId", flag.RecordingId);
        cmd.Parameters.AddWithValue("$userId", flag.UserId);
        cmd.Parameters.AddWithValue("$scope", flag.Scope);
        cmd.Parameters.AddWithValue("$rangeStart", (object?)flag.RangeStart?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rangeEnd", (object?)flag.RangeEnd?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", flag.Note);
        cmd.Parameters.AddWithValue("$createdAt", flag.CreatedAt.ToString("O"));
        flag.PersistId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return flag.PersistId;
    }

    public async Task RemoveAsync(int recordingId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PERSIST_FLAGS SET removed_at = $now WHERE recording_id = $id AND removed_at IS NULL;";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", recordingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PersistFlag>> GetActiveByRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        var result = new List<PersistFlag>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM PERSIST_FLAGS WHERE recording_id = $id AND removed_at IS NULL ORDER BY persist_id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new PersistFlag
            {
                PersistId = reader.GetInt32(reader.GetOrdinal("persist_id")),
                RecordingId = reader.GetInt32(reader.GetOrdinal("recording_id")),
                UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                Scope = reader.GetString(reader.GetOrdinal("scope")),
                RangeStart = reader.IsDBNull(reader.GetOrdinal("range_start"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("range_start")),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind),
                RangeEnd = reader.IsDBNull(reader.GetOrdinal("range_end"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("range_end")),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind),
                Note = reader.GetString(reader.GetOrdinal("note")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                RemovedAt = reader.IsDBNull(reader.GetOrdinal("removed_at"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("removed_at")),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind),
            });
        }
        return result;
    }
}
