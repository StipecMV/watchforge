using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;

// WatchForge NVR benchmark (S13): stiahne 6×15 min záznamov (6 kamier, dnešný deň)
// a zmeria download throughput. Po stiahnutí spusti tools/cpu-benchmark na reálnych dátach.
// Použitie: dotnet run --project tools/nvr-bench [--channels 0-5] [--hours N]

var host = "192.168.68.10"; // NVR (DHCP)
var secrets = File.ReadAllLines("/home/hp-camera-hub/workspace/watchforge/deploy/secrets/nvr-auth.yaml");
string user = "", pass = "";
foreach (var line in secrets)
{
    if (line.StartsWith("username:")) user = line.Split(':', 2)[1].Trim();
    if (line.StartsWith("password:")) pass = line.Split(':', 2)[1].Trim();
}

var channels = new[] { 0, 1, 2, 3, 4, 5 };
var hoursBack = 6; // dnešné záznamy za posledných 6 hodín (15-min segmenty)
var outDir = "/tmp/nvr-bench";
Directory.CreateDirectory(outDir);

Console.WriteLine($"=== NVR benchmark (S13) — {host} ===");
Console.WriteLine($"Sta hujem 6×15 min záznamov (kanály 0-5, posledných {hoursBack} h) do {outDir}");
Console.WriteLine();

// 1. Query dnešných nahrávok pre každý kanál
var now = DateTime.Now;
var from = now.AddHours(-hoursBack);
var filesByChannel = new List<NvrFile>[6];
var totalBytes = 0L;

using (var client = new DvripClient(new DvripClientOptions { Host = host, Port = 34567, Username = user, Password = pass }))
{
    var login = await client.LoginAsync();
    Console.WriteLine($"Login OK (session {login.SessionId})");

    for (int ch = 0; ch < 6; ch++)
    {
        var files = await client.QueryFilesAsync(from, now, ch);
        filesByChannel[ch] = files;
        var bytes = files.Sum(f => f.FileLengthBytes);
        totalBytes += bytes;
        Console.WriteLine($"  CH{ch + 1}: {files.Count} záznamov, {bytes / 1024.0 / 1024.0:F0} MB");
    }
}

Console.WriteLine();
Console.WriteLine($"Celkom {totalBytes / 1024.0 / 1024.0 / 1024.0:F2} GB záznamov v okne {hoursBack} h");
Console.WriteLine();

// 2. Stiahni prvý 15-min segment z každého kanálu (aspoň 300 MB = plný segment,
//    nie event klip — CH6 mal 9-sekundový motion klip ako FirstOrDefault)
var results = new List<(int Ch, string File, long Bytes, double Sec, double Mbps)>();
for (int ch = 0; ch < 6; ch++)
{
    var file = filesByChannel[ch].Where(f => f.FileLengthMB >= 300).OrderBy(f => f.BeginTime).FirstOrDefault();
    if (file is null)
    {
        Console.WriteLine($"  CH{ch + 1}: žiadne záznamy — preskakujem");
        continue;
    }

    // NVR FileName je plná cesta (/idea0/2026-08-08/001/...) — sanitizuj na basename
    var safeName = file.FileName.Split('/').Last().Replace('[', '_').Replace(']', '_').Replace('@', '_').TrimEnd('.');
    // Dôležité: dest BEZ prípony — ConvertVideoAsync sám pridá .mp4 (raw HEVC → MP4).
    // S príponou .mp4 by ffmpeg input==output zlyhal a súbor by ostal raw stream.
    var destRaw = Path.Combine(outDir, $"ch{ch + 1}-{safeName}");
    var destMp4 = destRaw + ".mp4";
    if (File.Exists(destMp4) && new FileInfo(destMp4).Length > 10_000_000)
    {
        Console.WriteLine($"  CH{ch + 1}: už stiahnuté ({destMp4.Split('/').Last()}) — preskakujem");
        var existingSize = new FileInfo(destMp4).Length;
        results.Add((ch + 1, destMp4, existingSize, 0, 0));
        continue;
    }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    using var client = new DvripClient(new DvripClientOptions { Host = host, Port = 34567, Username = user, Password = pass });
    var path = await client.DownloadFileAsync(file, destRaw, "mp4");
    sw.Stop();

    var size = new FileInfo(path).Length;
    var mbps = size * 8.0 / 1_000_000.0 / sw.Elapsed.TotalSeconds;
    results.Add((ch + 1, path, size, sw.Elapsed.TotalSeconds, mbps));
    Console.WriteLine($"  CH{ch + 1}: {size / 1024.0 / 1024.0:F0} MB za {sw.Elapsed.TotalSeconds:F1} s = {mbps:F0} Mbit/s");
}

Console.WriteLine();
Console.WriteLine("=== SÚHRN DOWNLOADU ===");
Console.WriteLine("| Kanál | Veľkosť (MB) | Čas (s) | Rýchlosť (Mbit/s) |");
Console.WriteLine("|---|---|---|---|");
foreach (var r in results)
    Console.WriteLine($"| CH{r.Ch} | {r.Bytes / 1024.0 / 1024.0:F0} | {r.Sec:F1} | {r.Mbps:F0} |");

Console.WriteLine();
Console.WriteLine($"Hotovo — {results.Count} súborov v {outDir}");
return 0;
