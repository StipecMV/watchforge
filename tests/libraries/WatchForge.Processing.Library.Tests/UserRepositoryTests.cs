using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library.Tests;

/// <summary>
/// S2-8: UserRepository — seed používateľov (FR-12), password hash (prázdny = nenastavené), reset.
/// </summary>
public class UserRepositoryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-users-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;
    private UserRepository? _repo;

    private async Task<UserRepository> RepoAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        _repo = new UserRepository(_connection);
        return _repo;
    }

    [Test]
    public async Task SeedDefaultUsers_CreatesFiveUsers_AdminFirst()
    {
        // Given prázdna DB
        var repo = await RepoAsync();

        // When seedneme default používateľov (FR-12: Admin admin + 4 useri)
        await repo.SeedDefaultUsersAsync();

        // Then 5 používateľov, Admin admin, ostatní standard
        var users = await repo.GetAllAsync();
        await Assert.That(users).Count().IsEqualTo(5);

        var admin = await repo.GetByUsernameAsync("admin");
        await Assert.That(admin).IsNotNull();
        await Assert.That(admin!.Role).IsEqualTo("admin");

        var user1 = await repo.GetByUsernameAsync("user1");
        await Assert.That(user1!.Role).IsEqualTo("standard");
        await Assert.That(user1.Locale).IsEqualTo("sk");
    }

    [Test]
    public async Task SeedDefaultUsers_IsIdempotent()
    {
        // Given DB so seednutými používateľmi
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();

        // When seedneme znova
        await repo.SeedDefaultUsersAsync();

        // Then stále 5 (nie 10)
        var users = await repo.GetAllAsync();
        await Assert.That(users).Count().IsEqualTo(5);
    }

    [Test]
    public async Task NewUser_HasEmptyPasswordHash_UntilSet()
    {
        // Given čerstvo seednutý používateľ
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();

        // When načítame
        var user = await repo.GetByUsernameAsync("user2");

        // Then password_hash je prázdny reťazec (prvé nastavenie hesla pri prihlásení)
        await Assert.That(user!.PasswordHash).IsEqualTo("");
    }

    [Test]
    public async Task SetPassword_ThenVerify_Succeeds()
    {
        // Given používateľ bez hesla
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();
        var user = await repo.GetByUsernameAsync("user2");

        // When nastavíme heslo (PBKDF2 hash)
        var hash = UserRepository.HashPassword("tajne-heslo");
        await repo.UpdatePasswordHashAsync(user!.UserId, hash);

        // Then verify prejde so správnym heslom a zlyhá s nesprávnym
        var stored = await repo.GetByUsernameAsync("user2");
        await Assert.That(UserRepository.VerifyPassword("tajne-heslo", stored!.PasswordHash)).IsTrue();
        await Assert.That(UserRepository.VerifyPassword("zle-heslo", stored.PasswordHash)).IsFalse();
    }

    [Test]
    public async Task UpdateProfile_ChangesAvatarAndLocale()
    {
        // Given používateľ s default avatar_id/locale
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();
        var user = await repo.GetByUsernameAsync("admin");
        await Assert.That(user!.AvatarId).IsEqualTo(1);

        // When zmeníme profil (S6-5: Settings → Profile)
        await repo.UpdateProfileAsync(user.UserId, avatarId: 7, locale: "en");

        // Then avatar_id aj locale sú zmenené (a ostatné polia ostali)
        var updated = await repo.GetByUsernameAsync("admin");
        await Assert.That(updated!.AvatarId).IsEqualTo(7);
        await Assert.That(updated.Locale).IsEqualTo("en");
        await Assert.That(updated.Role).IsEqualTo("admin");
    }

    [Test]
    public async Task UpdateProfile_IsPersistentAcrossReload()
    {
        // Given uložený profil
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();
        var user = await repo.GetByUsernameAsync("user1");
        await repo.UpdateProfileAsync(user!.UserId, avatarId: 9, locale: "en");

        // When otvoríme nové spojenie (simulácia reštartu)
        await using var second = await WatchForgeDatabase.OpenAsync(_dbPath);
        var repo2 = new UserRepository(second);

        // Then zmena prežila reload (FR-16: prežije reload stránky/restart)
        var reloaded = await repo2.GetByUsernameAsync("user1");
        await Assert.That(reloaded!.AvatarId).IsEqualTo(9);
        await Assert.That(reloaded.Locale).IsEqualTo("en");
    }

    [Test]
    public async Task ResetPassword_ClearsHash()
    {
        // Given používateľ s nastaveným heslom
        var repo = await RepoAsync();
        await repo.SeedDefaultUsersAsync();
        var user = await repo.GetByUsernameAsync("user3");
        var hash = UserRepository.HashPassword("heslo");
        await repo.UpdatePasswordHashAsync(user!.UserId, hash);

        // When reset (forgot password = vymazanie hashu v DB, FR-12)
        await repo.ResetPasswordAsync(user.UserId);

        // Then hash je prázdny → používateľ musí nastaviť nové pri ďalšom prihlásení
        var stored = await repo.GetByUsernameAsync("user3");
        await Assert.That(stored!.PasswordHash).IsEqualTo("");
    }

    [Test]
    public async Task HashPassword_ProducesDifferentSalts()
    {
        // Given rovnaké heslo hashované 2x
        var h1 = UserRepository.HashPassword("rovnake-heslo");
        var h2 = UserRepository.HashPassword("rovnake-heslo");

        // Then hashe sú rôzne (náhodná soľ) a oba verifikujú
        await Assert.That(h1).IsNotEqualTo(h2);
        await Assert.That(UserRepository.VerifyPassword("rovnake-heslo", h1)).IsTrue();
        await Assert.That(UserRepository.VerifyPassword("rovnake-heslo", h2)).IsTrue();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
