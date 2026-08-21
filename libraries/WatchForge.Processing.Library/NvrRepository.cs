using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>Repository nad tabuľkou NVRS (S4-2 synchronizácia).</summary>
public sealed class NvrRepository(SqliteConnection connection) : INvrRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<IReadOnlyList<Nvr>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<Nvr>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM NVRS ORDER BY nvr_id;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new Nvr
            {
                NvrId = reader.GetInt32(reader.GetOrdinal("nvr_id")),
                SiteId = reader.GetString(reader.GetOrdinal("site_id")),
                Host = reader.GetString(reader.GetOrdinal("host")),
                Port = reader.GetInt32(reader.GetOrdinal("port")),
                Username = reader.GetString(reader.GetOrdinal("username")),
                PasswordSecretEnv = reader.GetString(reader.GetOrdinal("password_secret_env")),
            });
        }
        return result;
    }

    /// <summary>S13-1: aktualizácia host adresy NVR (DHCP auto-discovery).</summary>
    public async Task<bool> UpdateHostAsync(int nvrId, string host, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE NVRS SET host = $host WHERE nvr_id = $id;";
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$id", nvrId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
