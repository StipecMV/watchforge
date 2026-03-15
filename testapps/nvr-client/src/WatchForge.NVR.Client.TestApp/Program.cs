Console.WriteLine("🏭 WatchForge NVR Client Test App");
Console.WriteLine("==================================");
Console.WriteLine();

// Build host with DI
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Directory.GetCurrentDirectory());
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables("WATCHFORGE_");
        config.AddCommandLine(args);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddWatchForgeNvrClient(options =>
        {
            options.Host = context.Configuration["Onvif:Host"] ?? "192.168.68.58";
            options.Port = int.Parse(context.Configuration["Onvif:Port"] ?? "80");
            options.Username = context.Configuration["Onvif:Username"] ?? string.Empty;
            options.Password = context.Configuration["Onvif:Password"] ?? string.Empty;
            options.ServicePath = context.Configuration["Onvif:ServicePath"] ?? "/onvif/device_service";
            options.UseHttps = bool.Parse(context.Configuration["Onvif:UseHttps"] ?? "false");
            options.TimeoutSeconds = int.Parse(context.Configuration["Onvif:TimeoutSeconds"] ?? "30");
        });
    })
    .Build();

// Get the client from DI
var client = host.Services.GetRequiredService<IOnvifClient>();

try
{
    Console.WriteLine($"🔌 Pripájam sa k {client.Host}...");
    Console.WriteLine();

    // 1️⃣ Test connection & get device info
    var deviceInfo = await client.Device.GetDeviceInformationAsync();
    Console.WriteLine($"✅ Pripojenie OK!");
    Console.WriteLine($"   Manufacturer: {deviceInfo.Manufacturer}");
    Console.WriteLine($"   Model: {deviceInfo.Model}");
    Console.WriteLine($"   Firmware: {deviceInfo.FirmwareVersion}");
    if (!string.IsNullOrEmpty(deviceInfo.SerialNumber))
        Console.WriteLine($"   Serial: {deviceInfo.SerialNumber}");
    Console.WriteLine();

    // 2️⃣ Get available services
    var services = await client.Device.GetServicesAsync();
    Console.WriteLine($"📡 Dostupné ONVIF služby ({services.Count}):");
    foreach (var svc in services)
    {
        var icon = svc.Namespace.Contains("media", StringComparison.OrdinalIgnoreCase) ? "📹" :
                   svc.Namespace.Contains("device", StringComparison.OrdinalIgnoreCase) ? "🔧" :
                   svc.Namespace.Contains("recording", StringComparison.OrdinalIgnoreCase) ? "💾" :
                   svc.Namespace.Contains("event", StringComparison.OrdinalIgnoreCase) ? "🔔" : "•";
        Console.WriteLine($"   {icon} {svc.Namespace}");
    }
    Console.WriteLine();

    // 3️⃣ Get media profiles
    var profiles = await client.Media.GetProfilesAsync();
    Console.WriteLine($"📹 Nájdených {profiles.Count} profilov:");
    foreach (var profile in profiles)
    {
        var fixedIcon = profile.IsFixed == true ? "🔒" : "📌";
        Console.WriteLine($"   {fixedIcon} {profile.Name} (token: {profile.Token})");
    }
    Console.WriteLine();

    // 4️⃣ Get RTSP stream URI for first profile
    if (profiles.Count > 0)
    {
        var firstProfile = profiles[0];
        var streamUri = await client.Media.GetStreamUriAsync(
            firstProfile.Token,
            StreamType.RTPUnicast,
            TransportProtocol.RTSP);
        
        Console.WriteLine($"🔗 RTSP URL (pre '{firstProfile.Name}'):");
        Console.WriteLine($"   {streamUri.Uri}");
        Console.WriteLine();
    }

    // 5️⃣ Check recording search support
    Console.WriteLine("🔍 Kontrolujem RecordingSearch...");
    if (client.RecordingSearch != null)
    {
        var isSupported = await client.RecordingSearch.IsSearchSupportedAsync();
        if (isSupported)
        {
            Console.WriteLine("   ✅ RecordingSearch je podporovaný!");
        }
        else
        {
            Console.WriteLine("   ⚠️ RecordingSearch nie je dostupný");
            Console.WriteLine("      → Použi RTSP + časový filter na stiahnutie");
        }
    }
    else
    {
        Console.WriteLine("   ⚠️ RecordingSearch service nie je inicializovaný");
    }
    Console.WriteLine();

    Console.WriteLine("==================================");
    Console.WriteLine("✅ Hotovo!");
}
catch (OnvifConnectionException ex)
{
    Console.WriteLine($"❌ Chyba pripojenia: {ex.Message}");
    Console.WriteLine($"   Host: {ex.Host}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Detail: {ex.InnerException.Message}");
    }
    Environment.Exit(1);
}
catch (OnvifAuthenticationException ex)
{
    Console.WriteLine($"❌ Chyba autentifikácie: {ex.Message}");
    Console.WriteLine($"   Username: {ex.Username}");
    Environment.Exit(2);
}
catch (OnvifServiceNotAvailableException ex)
{
    Console.WriteLine($"⚠️ Služba nie je dostupná: {ex.Message}");
    Environment.Exit(3);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Neočakávaná chyba: {ex.Message}");
    Console.WriteLine($"   Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
    Environment.Exit(99);
}
