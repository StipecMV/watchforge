using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repozitár nad FACES (FR-05 — embeddings perzistencia: uloženie/načítanie
/// face embeddings, priradenie identity, mazanie).
/// Thread-safe: každá operácia si otvorí vlastné spojenie.
/// </summary>
public sealed class FaceRepository(SqliteConnection connection) : IFaceRepository
{
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);

    public async Task<int> InsertAsync(FaceRecord face, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FACES (detection_id, identity_id, embedding, confidence, temp_crop_path)
            VALUES ($det, $identity, $embedding, $confidence, $crop);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$det", face.DetectionId);
        cmd.Parameters.AddWithValue("$identity", (object?)face.IdentityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding", (object?)face.Embedding ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$confidence", face.Confidence);
        cmd.Parameters.AddWithValue("$crop", face.TempCropPath);
        face.FaceId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return face.FaceId;
    }

    public async Task<IReadOnlyList<FaceRecord>> GetByDetectionAsync(int detectionId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var result = new List<FaceRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT face_id, detection_id, identity_id, embedding, confidence, temp_crop_path
            FROM FACES
            WHERE detection_id = $det
            ORDER BY face_id;
            """;
        cmd.Parameters.AddWithValue("$det", detectionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public async Task<IReadOnlyList<FaceRecord>> GetByIdentityAsync(int identityId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var result = new List<FaceRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT face_id, detection_id, identity_id, embedding, confidence, temp_crop_path
            FROM FACES
            WHERE identity_id = $id
            ORDER BY face_id;
            """;
        cmd.Parameters.AddWithValue("$id", identityId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public async Task<IReadOnlyList<FaceRecord>> GetAllWithIdentityAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var result = new List<FaceRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT face_id, detection_id, identity_id, embedding, confidence, temp_crop_path
            FROM FACES
            WHERE identity_id IS NOT NULL
            ORDER BY face_id;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public async Task<bool> AssignIdentityAsync(int faceId, int? identityId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE FACES SET identity_id = $id WHERE face_id = $face;";
        cmd.Parameters.AddWithValue("$id", (object?)identityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$face", faceId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task DeleteByIdentityAsync(int identityId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FACES WHERE identity_id = $id;";
        cmd.Parameters.AddWithValue("$id", identityId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static FaceRecord Map(SqliteDataReader reader) => new()
    {
        FaceId        = reader.GetInt32(0),
        DetectionId   = reader.GetInt32(1),
        IdentityId    = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        Embedding     = reader.IsDBNull(3) ? null : (byte[])reader[3],
        Confidence    = reader.GetDouble(4),
        TempCropPath  = reader.GetString(5),
    };
}
