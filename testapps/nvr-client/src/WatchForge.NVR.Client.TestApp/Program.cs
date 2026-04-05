using Microsoft.Extensions.Configuration;

Console.WriteLine("🔧 WatchForge DVRIP File Downloader");
Console.WriteLine("====================================");
Console.WriteLine();

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var host        = config["Dvrip:Host"]     ?? throw new InvalidOperationException("Dvrip:Host not configured");
var port        = int.Parse(config["Dvrip:Port"] ?? "34567");
var username    = config["Dvrip:Username"] ?? throw new InvalidOperationException("Dvrip:Username not configured");
var password    = config["Dvrip:Password"] ?? throw new InvalidOperationException("Dvrip:Password not configured");
var downloadDir = config["Dvrip:DownloadDir"] is { Length: > 0 } d ? d : Directory.GetCurrentDirectory();

// Fixed query window: 2026-04-05 15:30:00 – 15:45:00, channels 0–5
var from     = new DateTime(2026, 4, 5, 15, 30, 0);
var to       = new DateTime(2026, 4, 5, 15, 45, 0);
var channels = Enumerable.Range(0, 6).ToList();

Directory.CreateDirectory(downloadDir);

Console.WriteLine($"🔌 Connecting to {host}:{port} ...");

using var client = new DvripClient(host, port, username, password);
try
{
    // ── 1. Login ──────────────────────────────────────────────────────────────
    var login = await client.LoginAsync();

    Console.WriteLine("✅ Login OK");
    Console.WriteLine($"   Device type  : {login.DeviceType}");
    Console.WriteLine($"   Channels     : {login.ChannelNum}");
    Console.WriteLine($"   Session ID   : {login.SessionIdHex}");
    Console.WriteLine($"   Keep-alive   : {login.AliveInterval}s");
    Console.WriteLine();

    // ── 2. Query files for each of the 6 channels ─────────────────────────────
    Console.WriteLine($"🔍 Querying channels {channels[0]}–{channels[^1]}  {from:yyyy-MM-dd HH:mm:ss} → {to:HH:mm:ss}");

    var allFiles = new List<NvrFile>();
    foreach (var ch in channels)
    {
        var files = await client.QueryFilesAsync(from, to, ch);
        Console.WriteLine($"   Channel {ch}: {files.Count} file(s)");
        allFiles.AddRange(files);
    }

    allFiles = allFiles
        .DistinctBy(f => f.FileName)
        .OrderBy(f => f.BeginTime)
        .ToList();

    Console.WriteLine();

    // ── 3. Print file list ────────────────────────────────────────────────────
    Console.WriteLine($"📂 Found {allFiles.Count} file(s) across all channels:");
    if (allFiles.Count == 0)
    {
        Console.WriteLine("   (no files in the given period)");
        Console.WriteLine();
        Console.WriteLine("====================================");
        Console.WriteLine("✅ Done!");
        return;
    }

    foreach (var f in allFiles)
    {
        var size = f.FileLengthMB >= 1.0
            ? $"{f.FileLengthMB:F1} MB"
            : $"{f.FileLengthBytes / 1024.0:F1} KB";

        Console.WriteLine($"   📹 {f.FileName}");
        Console.WriteLine($"       {f.BeginTime:yyyy-MM-dd HH:mm:ss} → {f.EndTime:HH:mm:ss}  [{size}]");
    }
    Console.WriteLine();

    // ── 4. Download all files ─────────────────────────────────────────────────
    Console.WriteLine($"⬇️  Downloading {allFiles.Count} file(s) → {downloadDir}");
    Console.WriteLine();

    int succeeded = 0, failed = 0;
    for (int i = 0; i < allFiles.Count; i++)
    {
        var file     = allFiles[i];
        var destName = Path.GetFileName(file.FileName);
        var destPath = Path.Combine(downloadDir, destName);

        Console.WriteLine($"   [{i + 1}/{allFiles.Count}] {destName}  ({file.FileLengthMB:F1} MB)");

        long lastReported = 0;
        var progress = new Progress<long>(bytes =>
        {
            if (bytes - lastReported >= 1_048_576 || bytes >= file.FileLengthBytes)
            {
                lastReported = bytes;
                var pct = file.FileLengthBytes > 0 ? bytes * 100 / file.FileLengthBytes : 0;
                Console.Write($"\r         Downloading {bytes / 1_048_576.0:F1} MB of {file.FileLengthMB:F1} MB ({pct}%)   ");
            }
        });

        try
        {
            var outputPath = await client.DownloadFileAsync(file, destPath, progress);
            Console.WriteLine();
            Console.WriteLine($"         ✅ {outputPath}");
            Console.WriteLine($"         Preview: ffplay \"{outputPath}\"");
            succeeded++;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"         ⚠️  Failed: {ex.Message}");
            failed++;
        }

        Console.WriteLine();
    }

    Console.WriteLine("====================================");
    Console.WriteLine($"✅ Done!  {succeeded} downloaded, {failed} failed.");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    Environment.Exit(1);
}
