using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou ANNOTATIONS (S5-6) — user anotácie detekcií
/// (poznámka/oprava regiónu).
/// </summary>
public sealed class AnnotationRepository(SqliteConnection connection) : IAnnotationRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<int> InsertAsync(Annotation annotation, CancellationToken ct = default)
    {
        annotation.CreatedAt = DateTime.UtcNow;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ANNOTATIONS (detection_id, user_id, region_x, region_y, region_w, region_h, label, created_at)
            VALUES ($detectionId, $userId, $x, $y, $w, $h, $label, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$detectionId", annotation.DetectionId);
        cmd.Parameters.AddWithValue("$userId", annotation.UserId);
        cmd.Parameters.AddWithValue("$x", annotation.Region.X);
        cmd.Parameters.AddWithValue("$y", annotation.Region.Y);
        cmd.Parameters.AddWithValue("$w", annotation.Region.W);
        cmd.Parameters.AddWithValue("$h", annotation.Region.H);
        cmd.Parameters.AddWithValue("$label", annotation.Label);
        cmd.Parameters.AddWithValue("$createdAt", annotation.CreatedAt.ToString("O"));
        annotation.AnnotationId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return annotation.AnnotationId;
    }

    public async Task<IReadOnlyList<Annotation>> GetByDetectionAsync(int detectionId, CancellationToken ct = default)
    {
        var result = new List<Annotation>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ANNOTATIONS WHERE detection_id = $id ORDER BY annotation_id;";
        cmd.Parameters.AddWithValue("$id", detectionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Read(reader));
        }
        return result;
    }

    public async Task<Annotation?> GetByIdAsync(int annotationId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ANNOTATIONS WHERE annotation_id = $id;";
        cmd.Parameters.AddWithValue("$id", annotationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>UPDATE región + label. Vráti false, ak anotácia neexistuje.</summary>
    public async Task<bool> UpdateAsync(Annotation annotation, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ANNOTATIONS
            SET region_x = $x, region_y = $y, region_w = $w, region_h = $h, label = $label
            WHERE annotation_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", annotation.AnnotationId);
        cmd.Parameters.AddWithValue("$x", annotation.Region.X);
        cmd.Parameters.AddWithValue("$y", annotation.Region.Y);
        cmd.Parameters.AddWithValue("$w", annotation.Region.W);
        cmd.Parameters.AddWithValue("$h", annotation.Region.H);
        cmd.Parameters.AddWithValue("$label", annotation.Label);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Zmäže anotácie používateľa pre detekciu („Clear my drawings", FR-16).</summary>
    public async Task<int> DeleteByDetectionAndUserAsync(int detectionId, int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ANNOTATIONS WHERE detection_id = $detectionId AND user_id = $userId;";
        cmd.Parameters.AddWithValue("$detectionId", detectionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Annotation Read(SqliteDataReader reader) => new()
    {
        AnnotationId = reader.GetInt32(reader.GetOrdinal("annotation_id")),
        DetectionId = reader.GetInt32(reader.GetOrdinal("detection_id")),
        UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
        Region = new NormalizedRegion(
            reader.GetFloat(reader.GetOrdinal("region_x")),
            reader.GetFloat(reader.GetOrdinal("region_y")),
            reader.GetFloat(reader.GetOrdinal("region_w")),
            reader.GetFloat(reader.GetOrdinal("region_h"))),
        Label = reader.GetString(reader.GetOrdinal("label")),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
