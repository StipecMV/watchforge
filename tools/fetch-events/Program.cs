using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;

// S22p: fetch-events — stiahne KRÁTKE M eventy (motion eventy) z NVR z vybraných
// kamier pre porovnávaciu appku detekcie pohybu.
//
// Použitie:
//   dotnet run --project tools/fetch-events -- <outDir> <hodinySpät> [kanály...]
//   (default: 6 h spät, kanály 1,4,5 — CH2, CH4, CH6(hospodársky dvor))

var outDir = args.Length > 0 ? args[0] : "/tmp/motion-compare/videos";
var hoursBack = args.Length > 1 ? int.Parse(args[1]) : 6;
var channels = args.Length > 2
    ? args.Skip(2).Select(int.Parse).ToArray()
    : new[] { 1, 4, 5 }; // CH2, CH4, CH6

var host = "192.168.68.10"; // NVR — normalizované pre git; lokálne nahraď reálnou IP
var secrets = File.ReadAllLines("/home/hp-camera-hub/workspace/watchforge/deploy/secrets/nvr-auth.yaml");
string user = "", pass = "";
foreach (var line in secrets)
{
    if (line.StartsWith("username:")) user = line.Split(':', 2)[1].Trim();
    if (line.StartsWith("password:")) pass = line.Split(':', 2)[1].Trim();
}

Directory.CreateDirectory(outDir);
var now = DateTime.Now;
var from = now.AddHours(-hoursBack);
Console.WriteLine($"Stiahnem M eventy (krátke) z kanálov [{string.Join(",", channels)}] za {hoursBack} h do {outDir}");

using (var client = new DvripClient(new DvripClientOptions { Host = host, Port = 34567, Username = user, Password = pass }))
{
    var login = await client.LoginAsync();
    Console.WriteLine($"Login OK (session {login.SessionId}) — query {from:HH:mm} → {now:HH:mm}");

    foreach (var ch in channels)
    {
        try
        {
            var files = await client.QueryFilesAsync(from, now, ch);
            // M eventy = KRÁTKE (motion) — filter < 30 MB (15-min segmenty sú ~400 MB,
            // dlhšie eventy 60-130 MB sú R). Krátke ≈ 3-25 MB ≈ 5-30 s.
            var events = files
                .Where(f => f.FileLengthMB < 30 && f.FileLengthMB > 0.5)
                .OrderByDescending(f => f.BeginTime)
                .Take(3)
                .ToList();

            Console.WriteLine($"  CH{ch + 1}: {files.Count} záznamov, {events.Count} krátkych eventov vybraných");
            foreach (var f in events)
            {
                var safeName = f.FileName.Split('/').Last().Replace('[', '_').Replace(']', '_').Replace('@', '_').TrimEnd('.');
                var destRaw = Path.Combine(outDir, $"ch{ch + 1}-{safeName}");
                var destMp4 = destRaw + ".mp4";
                if (File.Exists(destMp4) && new FileInfo(destMp4).Length > 100_000)
                {
                    Console.WriteLine($"    už stiahnuté: {Path.GetFileName(destMp4)} ({f.FileLengthMB:F0} MB)");
                    continue;
                }
                try
                {
                    Console.Write($"    sťahujem {f.BeginTime:HH:mm:ss} ({f.FileLengthMB:F0} MB)... ");
                    var path = await client.DownloadFileAsync(f, destRaw, "mp4");
                    Console.WriteLine($"OK → {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CHYBA: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CH{ch + 1} query zlyhal: {ex.Message}");
        }
        // NVR session/rate limit — krátka pauza medzi kanálmi
        await Task.Delay(5000);
    }
}

Console.WriteLine("Hotovo.");