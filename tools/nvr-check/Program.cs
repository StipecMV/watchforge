// Dočasný overovací skript — DVRIP login test proti reálnej sieti.
// Spúšťa sa cez `dotnet run` a NIKDY sa necommitne.
// Použitie: dotnet run --project tools/nvr-check -- <host>
using WatchForge.DVRIP.Library;

var host = args.Length > 0 ? args[0] : "192.168.68.10";
var secrets = File.ReadAllLines("/home/hp-camera-hub/workspace/watchforge/deploy/secrets/nvr-auth.yaml");
string user = "", pass = "";
foreach (var line in secrets)
{
    if (line.StartsWith("username:")) user = line.Split(':', 2)[1].Trim();
    if (line.StartsWith("password:")) pass = line.Split(':', 2)[1].Trim();
}

Console.WriteLine($"Test DVRIP login na {host}:34567 (user={user}) ...");
try
{
    var client = new DvripClient(new DvripClientOptions
    {
        Host = host, Port = 34567, Username = user, Password = pass
    });
    var result = await client.LoginAsync();
    Console.WriteLine($"✅ LOGIN OK na {host}: {result.SessionId}");
    client.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Login zlyhal na {host}: {ex.GetType().Name}: {ex.Message}");
}
