using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou USERS (FR-12).
/// - Username je fixný v DB (žiadna registrácia).
/// - password_hash: PBKDF2 (Rfc2898DeriveBytes, SHA-256, 100k iterácií); prázdny = používateľ ešte nenastavil heslo.
/// - Reset password = vymazanie hashu → pri ďalšom prihlásení sa nastaví nové.
/// </summary>
public sealed class UserRepository(SqliteConnection connection) : IUserRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM USERS WHERE username = $username;";
        cmd.Parameters.AddWithValue("$username", username.ToLowerInvariant());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<User>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM USERS ORDER BY user_id;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Map(reader));
        return result;
    }

    public async Task<int> InsertAsync(User user, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO USERS (username, password_hash, role, avatar_id, locale, created_at)
            VALUES ($username, $passwordHash, $role, $avatarId, $locale, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$username", user.Username.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("$role", user.Role);
        cmd.Parameters.AddWithValue("$avatarId", user.AvatarId);
        cmd.Parameters.AddWithValue("$locale", user.Locale);
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdatePasswordHashAsync(int userId, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE USERS SET password_hash = $hash WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$hash", passwordHash);
        cmd.Parameters.AddWithValue("$id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResetPasswordAsync(int userId, CancellationToken ct = default)
        => await UpdatePasswordHashAsync(userId, "", ct);

    /// <summary>Upraví profil (avatar_id, locale) — S6-5 Settings → Profile.</summary>
    public async Task UpdateProfileAsync(int userId, int avatarId, string locale, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE USERS SET avatar_id = $avatarId, locale = $locale WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$avatarId", avatarId);
        cmd.Parameters.AddWithValue("$locale", locale);
        cmd.Parameters.AddWithValue("$id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Seed default používateľov (FR-12): Admin admin + User One/User Two/User Three/User Four standard.</summary>
    public async Task SeedDefaultUsersAsync(CancellationToken ct = default)
    {
        var defaults = new (string Username, string Role, int AvatarId)[]
        {
            ("admin", "admin", 1),
            ("user1", "standard", 2),
            ("user2", "standard", 3),
            ("user3", "standard", 4),
            ("user4", "standard", 5),
        };

        foreach (var (username, role, avatar) in defaults)
        {
            if (await GetByUsernameAsync(username, ct) is null)
            {
                await InsertAsync(new User
                {
                    Username = username,
                    Role = role,
                    AvatarId = avatar,
                    Locale = "sk",
                }, ct);
            }
        }
    }

    /// <summary>PBKDF2 hash — formát: {iterations}.{saltBase64}.{hashBase64}</summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Pbkdf2Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static User Map(SqliteDataReader r) => new()
    {
        UserId = r.GetInt32(r.GetOrdinal("user_id")),
        Username = r.GetString(r.GetOrdinal("username")),
        PasswordHash = r.GetString(r.GetOrdinal("password_hash")),
        Role = r.GetString(r.GetOrdinal("role")),
        AvatarId = r.GetInt32(r.GetOrdinal("avatar_id")),
        Locale = r.GetString(r.GetOrdinal("locale")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
