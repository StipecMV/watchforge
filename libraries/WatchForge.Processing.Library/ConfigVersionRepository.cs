using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou CONFIG_VERSIONS (FR-06).
/// Konfigurácia je verziovaná: nová verzia pre kameru automaticky deaktivuje starú aktívnu.
/// Zmena konfigurácie platí len pre nové analýzy (staré záznamy majú config_version_id odkaz).
/// </summary>
public sealed class ConfigVersionRepository(SqliteConnection connection) : IConfigVersionRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<ConfigVersion?> GetActiveForCameraAsync(int cameraId, CancellationToken ct = default)
    {
        // Aktívny per-camera profil; ak nie je, fallback na zdieľaný (KISS)
        var perCamera = await GetActiveWhereAsync("camera_id = $cameraId", ("$cameraId", cameraId), ct);
        return perCamera ?? await GetActiveSharedAsync(ct);
    }

    public async Task<ConfigVersion?> GetActiveSharedAsync(CancellationToken ct = default)
        => await GetActiveWhereAsync("camera_id IS NULL", null, ct);

    private async Task<ConfigVersion?> GetActiveWhereAsync(string where, (string, object)? param, CancellationToken ct)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM CONFIG_VERSIONS WHERE is_active = 1 AND {where} ORDER BY created_at DESC LIMIT 1;";
        if (param is not null) cmd.Parameters.AddWithValue(param.Value.Item1, param.Value.Item2);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> InsertAsync(ConfigVersion version, CancellationToken ct = default)
    {
        // Nová verzia pre danú kameru (alebo shared) deaktivuje starú aktívnu
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var deactivate = conn.CreateCommand())
            {
                deactivate.Transaction = (SqliteTransaction)tx;
                deactivate.CommandText = version.CameraId is null
                    ? "UPDATE CONFIG_VERSIONS SET is_active = 0 WHERE is_active = 1 AND camera_id IS NULL;"
                    : "UPDATE CONFIG_VERSIONS SET is_active = 0 WHERE is_active = 1 AND camera_id = $cameraId;";
                if (version.CameraId is not null)
                    deactivate.Parameters.AddWithValue("$cameraId", version.CameraId.Value);
                await deactivate.ExecuteNonQueryAsync(ct);
            }

            int id;
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = (SqliteTransaction)tx;
                insert.CommandText = """
                    INSERT INTO CONFIG_VERSIONS (camera_id, profile_name, profile_type, sensitivity,
                        intensity_threshold, min_contour_area, ignore_zones_json, focus_zones_json, is_active, created_at)
                    VALUES ($cameraId, $profileName, $profileType, $sensitivity,
                        $intensityThreshold, $minContourArea, $ignoreZones, $focusZones, 1, $createdAt);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$cameraId", (object?)version.CameraId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$profileName", version.ProfileName);
                insert.Parameters.AddWithValue("$profileType", version.ProfileType);
                insert.Parameters.AddWithValue("$sensitivity", version.Sensitivity);
                insert.Parameters.AddWithValue("$intensityThreshold", version.IntensityThreshold);
                insert.Parameters.AddWithValue("$minContourArea", version.MinContourArea);
                insert.Parameters.AddWithValue("$ignoreZones", version.IgnoreZonesJson);
                insert.Parameters.AddWithValue("$focusZones", version.FocusZonesJson);
                insert.Parameters.AddWithValue("$createdAt", version.CreatedAt.ToString("O"));
                id = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeactivateAsync(int configVersionId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CONFIG_VERSIONS SET is_active = 0 WHERE config_version_id = $id;";
        cmd.Parameters.AddWithValue("$id", configVersionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ConfigVersion Map(SqliteDataReader r) => new()
    {
        ConfigVersionId = r.GetInt32(r.GetOrdinal("config_version_id")),
        CameraId = r.IsDBNull(r.GetOrdinal("camera_id"))
            ? null : r.GetInt32(r.GetOrdinal("camera_id")),
        ProfileName = r.GetString(r.GetOrdinal("profile_name")),
        ProfileType = r.GetString(r.GetOrdinal("profile_type")),
        Sensitivity = r.GetFloat(r.GetOrdinal("sensitivity")),
        IntensityThreshold = r.GetFloat(r.GetOrdinal("intensity_threshold")),
        MinContourArea = r.GetFloat(r.GetOrdinal("min_contour_area")),
        IgnoreZonesJson = r.GetString(r.GetOrdinal("ignore_zones_json")),
        FocusZonesJson = r.GetString(r.GetOrdinal("focus_zones_json")),
        IsActive = r.GetInt32(r.GetOrdinal("is_active")) != 0,
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
