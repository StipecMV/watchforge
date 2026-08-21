using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>Repository nad tabuľkou CAMERAS (S4-2 synchronizácia).</summary>
public sealed class CameraRepository(SqliteConnection connection) : ICameraRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<IReadOnlyList<Camera>> GetAllAsync(int? nvrId = null, CancellationToken ct = default)
    {
        var result = new List<Camera>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = nvrId is null
            ? "SELECT * FROM CAMERAS ORDER BY camera_id;"
            : "SELECT * FROM CAMERAS WHERE nvr_id = $nvrId ORDER BY camera_id;";
        if (nvrId is not null)
            cmd.Parameters.AddWithValue("$nvrId", nvrId.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new Camera
            {
                CameraId = reader.GetInt32(reader.GetOrdinal("camera_id")),
                NvrId = reader.GetInt32(reader.GetOrdinal("nvr_id")),
                Channel = reader.GetInt32(reader.GetOrdinal("channel")),
                FriendlyName = reader.GetString(reader.GetOrdinal("friendly_name")),
                IconId = reader.GetString(reader.GetOrdinal("icon_id")),
                IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) != 0,
            });
        }
        return result;
    }

    /// <summary>Upraví profil kamery (friendly_name, icon_id, is_active). Vráti false, ak kamera neexistuje.</summary>
    public async Task<bool> UpdateAsync(Camera camera, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE CAMERAS
            SET friendly_name = $friendlyName, icon_id = $iconId, is_active = $isActive
            WHERE camera_id = $cameraId;
            """;
        cmd.Parameters.AddWithValue("$friendlyName", camera.FriendlyName);
        cmd.Parameters.AddWithValue("$iconId", camera.IconId);
        cmd.Parameters.AddWithValue("$isActive", camera.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$cameraId", camera.CameraId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
