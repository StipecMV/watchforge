// Dočasný diagnostický nástroj — HW info z NVR cez DVRIP.
// Spúšťa sa cez `dotnet run`, NIKDY sa necommitne.
// Použitie: dotnet run --project tools/nvr-probe -- <host>
using WatchForge.DVRIP.Library;

var host = args.Length > 0 ? args[0] : "192.168.68.10";
var secrets = File.ReadAllLines("/home/hp-camera-hub/workspace/watchforge/deploy/secrets/nvr-auth.yaml");
string user = "", pass = "";
foreach (var line in secrets)
{
    if (line.StartsWith("username:")) user = line.Split(':', 2)[1].Trim();
    if (line.StartsWith("password:")) pass = line.Split(':', 2)[1].Trim();
}

Console.WriteLine($"=== DVRIP HW PROBE na {host} ===");
var client = new DvripClient(new DvripClientOptions
{
    Host = host, Port = 34567, Username = user, Password = pass,
    ReadTimeoutSeconds = 8
});

try
{
    var login = await client.LoginAsync();
    Console.WriteLine($"LOGIN OK: DeviceType={login.DeviceType}, ChannelNum={login.ChannelNum}, Session={login.SessionId:X8}\n");

    async Task Probe(string name, string payload)
    {
        try
        {
            var resp = await client.SendCommandAsync(name, payload);
            Console.WriteLine($"--- {name} ---");
            Console.WriteLine(resp.Length > 3000 ? resp[..3000] + "…(orezané)" : resp);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"--- {name}: CHYBA {ex.GetType().Name}: {ex.Message}\n");
        }
    }

    // Systémové info (HW model, firmware, sériové číslo)
    await Probe("OPMachine", "{}");
    // Disky (HDD/SSD — čo tam je, kapacita, stav)
    await Probe("OPStorage", "{}");
    // Sieť (IP, MAC, DNS, DHCP?)
    await Probe("OPNetWork", "{}");
    // Časové pásmo / čas
    await Probe("OPTime", "{}");

    // Kanály — nastavenia kamier (rozlíšenie, bitrate, kodek, onvif…)
    var chCount = login.ChannelNum;
    for (int ch = 0; ch < Math.Min(chCount, 8); ch++)
    {
        Console.WriteLine($"===== KANÁL {ch} =====");
        await Probe("OPChannelSetting", $"{{\"Channel\":{ch}}}");
        await Probe("OPCamera", $"{{\"Channel\":{ch}}}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    client.Dispose();
}
