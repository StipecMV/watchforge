using System.Net;
using System.Net.Sockets;
using WatchForge.DVRIP.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// NVR discovery (S13-1, DHCP riešenie): NVR nemá statickú IP (DHCP), preto ak
/// je `NVRS.host` nedosiahnuteľný, preskenujeme lokálnu sieť na DVRIP port
/// (34567) a overíme prihlásenie. Nájdený host sa automaticky uloží do DB
/// (auto-update) — Runner si tak NVR nájde sám aj po zmene IP.
/// </summary>
public sealed class NvrDiscovery(
    INvrRepository nvrRepository,
    Func<DvripClientOptions, IDvripClient> clientFactory)
{
    /// <summary>DVRIP port (Xiongmai/Sofia NVR).</summary>
    public const int DvripPort = 34567;

    /// <summary>Timeout TCP connect počas skenu (ms).</summary>
    public int ConnectTimeoutMs { get; init; } = 300;

    /// <summary>
    /// Overí, či je host dosiahnuteľný (TCP connect na DVRIP port).
    /// </summary>
    public async Task<bool> IsReachableAsync(string host, int port = DvripPort, CancellationToken ct = default)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ak je aktuálny host nedosiahnuteľný, preskenuje lokálnu sieť a nájde NVR
    /// (TCP connect + DVRIP login overenie). Nájdený host uloží do DB.
    /// Vracia true, ak sa podarilo nájsť a uložiť nový host.
    /// </summary>
    public async Task<(bool Found, string? NewHost)> EnsureHostAsync(CancellationToken ct = default)
    {
        var nvr = (await nvrRepository.GetAllAsync(ct)).FirstOrDefault();
        if (nvr is null)
            return (false, null);

        // Aktuálny host funguje → nič netreba
        if (await IsReachableAsync(nvr.Host, nvr.Port, ct))
            return (true, nvr.Host);

        // Sken lokálnej siete na DVRIP port
        var candidates = await ScanLocalNetworkAsync(nvr.Port, ct);
        var password = string.IsNullOrEmpty(nvr.PasswordSecretEnv)
            ? null
            : Environment.GetEnvironmentVariable(nvr.PasswordSecretEnv);

        foreach (var host in candidates)
        {
            if (ct.IsCancellationRequested) break;
            if (await TryLoginAsync(host, nvr.Port, nvr.Username, password, ct))
            {
                await nvrRepository.UpdateHostAsync(nvr.NvrId, host, ct);
                return (true, host);
            }
        }

        return (false, null);
    }

    /// <summary>Sken lokálnej siete: /22 rozsah z aktuálnej IP (ak /22, inak /24).</summary>
    private async Task<IReadOnlyList<string>> ScanLocalNetworkAsync(int port, CancellationToken ct = default)
    {
        var localIp = GetLocalIpAddress();
        var (networkBase, hosts) = ComputeRange(localIp);

        var found = new List<string>();
        var tasks = new List<Task>();
        var gate = new SemaphoreSlim(64); // obmedzíme paralelizmus skenu

        foreach (var host in hosts)
        {
            if (ct.IsCancellationRequested) break;
            var ip = $"{networkBase}.{host}";
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (await IsReachableAsync(ip, port, ct))
                    {
                        lock (found) found.Add(ip);
                    }
                }
                finally { gate.Release(); }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return found;
    }

    /// <summary>Overenie DVRIP loginu na kandidátovi (potvrdenie, že to je NVR).</summary>
    private async Task<bool> TryLoginAsync(string host, int port, string? username, string? password, CancellationToken ct)
    {
        try
        {
            using var client = clientFactory(new DvripClientOptions
            {
                Host = host,
                Port = port,
                Username = username ?? "",
                Password = password ?? "",
            });
            var result = await client.LoginAsync(ct);
            return result.SessionId is > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLocalIpAddress()
    {
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = addr.Address;
                if (IPAddress.IsLoopback(ip)) continue;
                if (ip.ToString().StartsWith("169.254")) continue; // APIPA
                return ip.ToString();
            }
        }
        return "192.168.1.100"; // fallback
    }

    /// <summary>Vráti (sieťový základ, zoznam hostov) pre /22 alebo /24.</summary>
    private static (string NetworkBase, List<int> Hosts) ComputeRange(string localIp)
    {
        var parts = localIp.Split('.');
        if (parts.Length != 4) return ("192.168.1", Enumerable.Range(1, 254).ToList());

        var networkBase = $"{parts[0]}.{parts[1]}";
        var third = int.Parse(parts[2]);

        // /22 → 192.168.68.x pokrýva 68.0–71.255; skenujeme celý rozsah tretieho oktetu (0–255)
        var hosts = new List<int>();
        for (int i = 1; i <= 255; i++)
        {
            if (i == third) continue; // vynecháme vlastnú IP
            hosts.Add(i);
        }
        return ($"{networkBase}.{third}", hosts);
    }
}
