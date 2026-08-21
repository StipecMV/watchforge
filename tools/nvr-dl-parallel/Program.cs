using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;

// WatchForge S15 v2: ČISTÉ meranie paralelného downloadu z NVR.
// Metodika: rovnakých 16×15-min segmentov (6 kamier, najnovšie plné segmenty)
// sa stiahne pre KAŽDÚ konfiguráciu paralelizmu (4, 6, 8, 10, 12, 14, 16).
// Na rozdiel od v1 (rôzne veľkosti segmentov) sú časy priamo porovnateľné.
//
// Použitie: nvr-dl-parallel [p1,p2,...]  (default: 4,6,8,10,12,14,16)

var host = "192.168.68.10"; // NVR
var secrets = File.ReadAllLines("/home/hp-camera-hub/workspace/watchforge/deploy/secrets/nvr-auth.yaml");
string user = "", pass = "";
foreach (var line in secrets)
{
    if (line.StartsWith("username:")) user = line.Split(':', 2)[1].Trim();
    if (line.StartsWith("password:")) pass = line.Split(':', 2)[1].Trim();
}

var configsArg = args.Length > 0 ? args[0] : "4,6,8,10,12,14,16";
var configs = configsArg.Split(',').Select(int.Parse).ToArray();
var outRoot = "/tmp/nvr-dl-parallel-v2";
Directory.CreateDirectory(outRoot);

// 1. Query dnešné záznamy pre 6 kanálov
var now = DateTime.Now;
var dayStart = now.Date;
var filesByChannel = new List<NvrFile>[6];

using (var client = new DvripClient(new DvripClientOptions { Host = host, Port = 34567, Username = user, Password = pass }))
{
    var login = await client.LoginAsync();
    Console.WriteLine($"Login OK (session {login.SessionId}) — query dnešných záznamov (0:00 → teraz)");

    for (int ch = 0; ch < 6; ch++)
    {
        var files = await client.QueryFilesAsync(dayStart, now, ch);
        filesByChannel[ch] = files.Where(f => f.FileLengthMB >= 300)
                                  .OrderByDescending(f => f.BeginTime)
                                  .ToList();
        Console.WriteLine($"  CH{ch + 1}: {files.Count} záznamov, {filesByChannel[ch].Count} plných ≥300 MB");
    }
}

// 2. Vyber 16 najnovších plných 15-min segmentov naprieč kanálmi
var selected = new List<(int Ch, NvrFile File)>();
foreach (var ch in Enumerable.Range(0, 6))
{
    foreach (var f in filesByChannel[ch].Take(4)) // max 4 najnovšie z každého kanálu
    {
        selected.Add((ch + 1, f));
        if (selected.Count >= 16) break;
    }
    if (selected.Count >= 16) break;
}
selected = selected.OrderByDescending(s => s.File.BeginTime).ToList();

Console.WriteLine();
Console.WriteLine($"Vybratých {selected.Count}×15-min segmentov (najnovšie, ~{selected.Sum(s => s.File.FileLengthMB):F0} MB spolu):");
foreach (var (ch, f) in selected.Take(16))
    Console.WriteLine($"  CH{ch} {f.BeginTime:HH:mm}-{f.EndTime:HH:mm} {f.FileLengthMB:F0} MB");
Console.WriteLine();
Console.WriteLine("=== MERANIE PARALELNÉHO DOWNLOADU v2 (rovnaké segmenty, 15-min) ===");
Console.WriteLine("Konfigurácie: " + string.Join(", ", configs) + " súbežných streamov");
Console.WriteLine();

var summary = new List<(int Parallel, double WallSec, double TotalMB, double AggMbps, double PerStreamMbps)>();

foreach (var parallel in configs)
{
    var dir = Path.Combine(outRoot, $"p{parallel}");
    Directory.CreateDirectory(dir);

    Console.WriteLine($"--- parallel={parallel} ({selected.Count} segmentov) ---");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var gate = new SemaphoreSlim(parallel);
    var tasks = selected.Select(item => Task.Run(async () =>
    {
        await gate.WaitAsync();
        try
        {
            var (ch, file) = item;
            var safeName = file.FileName.Split('/').Last()
                .Replace('[', '_').Replace(']', '_').Replace('@', '_').TrimEnd('.');
            var destRaw = Path.Combine(dir, $"ch{ch}-{safeName}");
            using var client = new DvripClient(new DvripClientOptions
            { Host = host, Port = 34567, Username = user, Password = pass });
            var fsw = System.Diagnostics.Stopwatch.StartNew();
            var path = await client.DownloadFileAsync(file, destRaw, "mp4");
            fsw.Stop();
            var size = new FileInfo(path).Length;
            var mbps = size * 8.0 / 1_000_000.0 / fsw.Elapsed.TotalSeconds;
            Console.WriteLine($"    CH{ch} {file.BeginTime:HH:mm}: {size / 1024.0 / 1024.0:F0} MB za {fsw.Elapsed.TotalSeconds:F1} s = {mbps:F0} Mbit/s");
        }
        finally { gate.Release(); }
    })).ToArray();

    await Task.WhenAll(tasks);
    sw.Stop();

    var totalMB = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
    var aggMbps = totalMB * 8.0 / sw.Elapsed.TotalSeconds;
    summary.Add((parallel, sw.Elapsed.TotalSeconds, totalMB, aggMbps, aggMbps / parallel));
    Console.WriteLine($"    CELKOM: {totalMB:F0} MB za {sw.Elapsed.TotalSeconds:F1} s = {aggMbps:F0} Mbit/s agregovane ({aggMbps / parallel:F0} Mbit/s per stream)");
    Console.WriteLine();
}

// 3. Súhrnná tabuľka + porovnanie s v1 (predošlé meranie, krátke segmenty)
Console.WriteLine("=== SÚHRN v2 (rovnaké 15-min segmenty) ===");
Console.WriteLine("| Paralelizmus | Celkový čas (s) | Dáta (MB) | Agregovaná rýchlosť (Mbit/s) | Per stream (Mbit/s) |");
Console.WriteLine("|---|---|---|---|---|");
foreach (var r in summary)
    Console.WriteLine($"| {r.Parallel} | {r.WallSec:F1} | {r.TotalMB:F0} | {r.AggMbps:F0} | {r.PerStreamMbps:F0} |");

// Porovnanie s v1 (predošlé meranie: rôzne veľkosti segmentov, 20-250 MB)
Console.WriteLine();
Console.WriteLine("=== POROVNANIE s predošlým meraním (v1) ===");
Console.WriteLine("v1 (nepriame): rôzne segmenty 20–250 MB (p2–p8) / 15-min (p1) — časy NIE sú priamo porovnateľné naprieč configmi");
Console.WriteLine("| Paralelizmus | v1 agregovane (Mbit/s) | v2 agregovane (Mbit/s) | v1 per stream (Mbit/s) | v2 per stream (Mbit/s) |");
Console.WriteLine("|---|---|---|---|---|");
var v1 = new Dictionary<int, (double Agg, double Per)> { { 1, (7, 6) }, { 2, (11, 6) }, { 3, (15, 6) }, { 6, (29, 6) }, { 8, (29, 6) } };
foreach (var r in summary)
{
    (double Agg, double Per) v = v1.TryGetValue(r.Parallel, out var x) ? (x.Agg, x.Per) : (double.NaN, double.NaN);
    var v1Agg = double.IsNaN(v.Agg) ? "—" : $"{v.Agg:F0}";
    var v1Per = double.IsNaN(v.Per) ? "—" : $"{v.Per:F0}";
    Console.WriteLine($"| {r.Parallel} | {v1Agg} | {r.AggMbps:F0} | {v1Per} | {r.PerStreamMbps:F0} |");
}

Console.WriteLine();
Console.WriteLine($"Hotovo — súbory v {outRoot} (16×15-min na konfiguráciu, ~{summary.Count} konfigurácií)");
return 0;
