using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repozitár nad IDENTITIES (FR-05 — databáza tvárí, správa identít).
/// Thread-safe: každá operácia si otvorí vlastné spojenie (zdieľaný
/// SqliteConnection nie je thread-safe).
/// </summary>
public sealed class IdentityRepository(SqliteConnection connection) : IIdentityRepository
{
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);

    public async Task<IReadOnlyList<Identity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var result = new List<Identity>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT identity_id, name, created_by, created_at
            FROM IDENTITIES
            ORDER BY name;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public async Task<Identity?> GetByIdAsync(int identityId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT identity_id, name, created_by, created_at
            FROM IDENTITIES
            WHERE identity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", identityId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> CreateAsync(string name, int? createdBy, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO IDENTITIES (name, created_by)
            VALUES ($name, $createdBy);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$createdBy", (object?)createdBy ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> RenameAsync(int identityId, string name, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE IDENTITIES SET name = $name WHERE identity_id = $id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$id", identityId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(int identityId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Faces sa vymažú cez FK cascade (faceRepo.DeleteByIdentityAsync volá volajúci)
        cmd.CommandText = "DELETE FROM IDENTITIES WHERE identity_id = $id;";
        cmd.Parameters.AddWithValue("$id", identityId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static Identity Map(SqliteDataReader reader) => new()
    {
        IdentityId = reader.GetInt32(0),
        Name       = reader.GetString(1),
        CreatedBy  = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        CreatedAt  = DateTime.Parse(reader.GetString(3)),
    };
}
