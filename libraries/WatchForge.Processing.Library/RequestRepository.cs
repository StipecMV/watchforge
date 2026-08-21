using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou REQUESTS (S5-4) — interaktívne požiadavky
/// („hľadaj pohyb 14:00–16:00"), ktoré generujú prioritné joby a clipy.
/// </summary>
public sealed class RequestRepository(SqliteConnection connection) : IRequestRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<Request> InsertAsync(Request request, CancellationToken ct = default)
    {
        request.CreatedAt = DateTime.UtcNow;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO REQUESTS (source, requester, query, from_time, to_time, camera_id,
                detection_type_filter, context_before_sec, context_after_sec, status, estimate, created_at)
            VALUES ($source, $requester, $query, $from, $to, $cameraId,
                $filter, $before, $after, $status, $estimate, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$source", request.Source);
        cmd.Parameters.AddWithValue("$requester", request.Requester);
        cmd.Parameters.AddWithValue("$query", request.Query);
        cmd.Parameters.AddWithValue("$from", request.FromTime.ToString("O"));
        cmd.Parameters.AddWithValue("$to", request.ToTime.ToString("O"));
        cmd.Parameters.AddWithValue("$cameraId", (object?)request.CameraId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$filter", (object?)request.DetectionTypeFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$before", request.ContextBeforeSec);
        cmd.Parameters.AddWithValue("$after", request.ContextAfterSec);
        cmd.Parameters.AddWithValue("$status", request.Status);
        cmd.Parameters.AddWithValue("$estimate", request.Estimate);
        cmd.Parameters.AddWithValue("$createdAt", request.CreatedAt.ToString("O"));
        request.RequestId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return request;
    }

    public async Task<Request?> GetByIdAsync(int requestId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM REQUESTS WHERE request_id = $id;";
        cmd.Parameters.AddWithValue("$id", requestId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task UpdateAsync(Request request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE REQUESTS
            SET status = $status, estimate = $estimate, completed_at = $completedAt
            WHERE request_id = $id;
            """;
        cmd.Parameters.AddWithValue("$status", request.Status);
        cmd.Parameters.AddWithValue("$estimate", request.Estimate);
        cmd.Parameters.AddWithValue("$completedAt", (object?)request.CompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", request.RequestId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Request Map(SqliteDataReader r) => new()
    {
        RequestId = r.GetInt32(r.GetOrdinal("request_id")),
        Source = r.GetString(r.GetOrdinal("source")),
        Requester = r.GetString(r.GetOrdinal("requester")),
        Query = r.GetString(r.GetOrdinal("query")),
        FromTime = DateTime.Parse(r.GetString(r.GetOrdinal("from_time")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        ToTime = DateTime.Parse(r.GetString(r.GetOrdinal("to_time")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        CameraId = r.IsDBNull(r.GetOrdinal("camera_id")) ? null : r.GetInt32(r.GetOrdinal("camera_id")),
        DetectionTypeFilter = r.IsDBNull(r.GetOrdinal("detection_type_filter"))
            ? null : r.GetString(r.GetOrdinal("detection_type_filter")),
        ContextBeforeSec = r.GetInt32(r.GetOrdinal("context_before_sec")),
        ContextAfterSec = r.GetInt32(r.GetOrdinal("context_after_sec")),
        Status = r.GetString(r.GetOrdinal("status")),
        Estimate = r.GetString(r.GetOrdinal("estimate")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        CompletedAt = r.IsDBNull(r.GetOrdinal("completed_at"))
            ? null : DateTime.Parse(r.GetString(r.GetOrdinal("completed_at")),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
