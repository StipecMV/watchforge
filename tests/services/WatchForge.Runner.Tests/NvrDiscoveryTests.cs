using Microsoft.Data.Sqlite;
using WatchForge.DVRIP.Library;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S13-1: NvrDiscovery — DHCP auto-nález NVR (sken LAN na DVRIP port + login overenie
/// + auto-update host v DB). Používa reálny FakeDvripServer na localhost.
/// [NotInParallel]: testy zdieľajú FindFreePort + env var hesla — sekvenčne.
/// </summary>
[NotInParallel]
public class NvrDiscoveryTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-discovery-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;

    private async Task<(NvrDiscovery discovery, FakeDvripServer server, NvrRepository repo)> CreateAsync(
        string host, int port)
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        var repo = new NvrRepository(_connection);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', $host, $port, 'nvr-user', 'WATCHFORGE_NVR_PASSWORD');
            """;
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        await cmd.ExecuteNonQueryAsync();

        Environment.SetEnvironmentVariable("WATCHFORGE_NVR_PASSWORD", "secret");
        var server = new FakeDvripServer(
            validUsername: "nvr-user",
            validPassword: "secret",
            recordings: [],
            listenAddress: System.Net.IPAddress.Loopback,
            port: port);
        var discovery = new NvrDiscovery(repo, options =>
        {
            var client = new DvripClient(options);
            return client;
        });
        return (discovery, server, repo);
    }

    /// <summary>Nájde voľný TCP port na localhost (pre fake server + DB zhodne).</summary>
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public async Task EnsureHost_ReachableHost_ReturnsTrueWithoutScan()
    {
        // Given NVR host = localhost na voľnom porte (dosiahnuteľný cez fake server)
        var port = FindFreePort();
        var (discovery, server, repo) = await CreateAsync("127.0.0.1", port);
        await server.StartAsync();

        var (found, newHost) = await discovery.EnsureHostAsync();

        await Assert.That(found).IsTrue();
        await Assert.That(newHost).IsEqualTo("127.0.0.1");
        // Host sa nezmenil
        var nvr = (await repo.GetAllAsync())[0];
        await Assert.That(nvr.Host).IsEqualTo("127.0.0.1");

        server.Dispose();
    }

    [Test]
    public async Task IsReachable_ClosedPort_ReturnsFalse()
    {
        // Given voľný port, na ktorom nič nepočúva
        var port = FindFreePort();
        var (discovery, server, repo) = await CreateAsync("127.0.0.1", port);

        var reachable = await discovery.IsReachableAsync("127.0.0.1", port);

        await Assert.That(reachable).IsFalse();
        server.Dispose();
    }

    [Test]
    public async Task EnsureHost_UnreachableConfiguredHost_DoesNotThrow()
    {
        // Given host v DB = neexistujúca IP (169.254.x.x — APIPA, mimo siete),
        // fake NVR beží na 127.0.0.1 — sken ho nenájde (localhost nie je v LAN skene),
        // takže výsledok je false, ale nespadne to (safe fallback).
        var port = FindFreePort();
        var (discovery, server, repo) = await CreateAsync("169.254.1.1", port);
        await server.StartAsync();

        var (found, newHost) = await discovery.EnsureHostAsync();

        // Neexistujúca IP → nie je dosiahnuteľná; sken lokálnej siete nenájde localhost
        await Assert.That(found).IsFalse();
        await Assert.That(newHost).IsNull();

        server.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("WATCHFORGE_NVR_PASSWORD", null);
        if (_connection is not null)
            await _connection.DisposeAsync();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
