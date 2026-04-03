using Microsoft.Extensions.Configuration;

Console.WriteLine("🔧 WatchForge DVRIP File Downloader");
Console.WriteLine("====================================");
Console.WriteLine();

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var host     = config["Dvrip:Host"]     ?? throw new InvalidOperationException("Dvrip:Host not configured");
var port     = int.Parse(config["Dvrip:Port"] ?? "34567");
var username = config["Dvrip:Username"] ?? throw new InvalidOperationException("Dvrip:Username not configured");
var password = config["Dvrip:Password"] ?? throw new InvalidOperationException("Dvrip:Password not configured");

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

    // ── 2. Query files (last 7 days, all channels) ────────────────────────────
    var from = DateTime.Now.Date.AddDays(-7);
    var to   = DateTime.Now;

    Console.WriteLine($"🔍 Querying files  {from:yyyy-MM-dd} → {to:yyyy-MM-dd HH:mm:ss}");

    var allFiles = new List<NvrFile>();
    for (int ch = 0; ch < login.ChannelNum; ch++)
    {
        var files = await client.QueryFilesAsync(from, to, ch);
        allFiles.AddRange(files);
    }

    // Deduplicate by filename (some firmware reports the same file on multiple channels)
    allFiles = allFiles
        .DistinctBy(f => f.FileName)
        .OrderBy(f => f.BeginTime)
        .ToList();

    Console.WriteLine();

    // ── 3. Print file list ────────────────────────────────────────────────────
    Console.WriteLine($"📂 Found {allFiles.Count} file(s):");
    if (allFiles.Count == 0)
    {
        Console.WriteLine("   (no files in the last 7 days)");
    }
    else
    {
        foreach (var f in allFiles)
        {
            var size = f.FileLengthMB >= 1.0
                ? $"{f.FileLengthMB:F1} MB"
                : $"{f.FileLengthBytes / 1024.0:F1} KB";

            Console.WriteLine($"   📹 {f.FileName}");
            Console.WriteLine($"       {f.BeginTime:yyyy-MM-dd HH:mm:ss} → {f.EndTime:HH:mm:ss}  [{size}]");
        }
    }
    Console.WriteLine();

    // ── 4. Download first file ────────────────────────────────────────────────
    if (allFiles.Count > 0)
    {
        var first       = allFiles[0];
        var destName    = Path.GetFileName(first.FileName);
        var destPath    = Path.Combine(Directory.GetCurrentDirectory(), destName);

        Console.WriteLine($"⬇️  Downloading: {destName}");
        Console.WriteLine($"   Destination : {destPath}");
        Console.WriteLine($"   Expected    : {first.FileLengthMB:F1} MB");

        long lastReported = 0;
        var progress = new Progress<long>(bytes =>
        {
            if (bytes - lastReported >= 1_048_576 || bytes >= first.FileLengthBytes)
            {
                lastReported = bytes;
                var pct = first.FileLengthBytes > 0 ? bytes * 100 / first.FileLengthBytes : 0;
                Console.Write($"\r   Progress    : {bytes / 1_048_576.0:F1} MB / {first.FileLengthMB:F1} MB ({pct}%)   ");
            }
        });

        try
        {
            await client.DownloadFileAsync(first, destPath, progress);
            Console.WriteLine();
            Console.WriteLine($"✅ Download complete → {destPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠️  Download failed: {ex.Message}");
            Console.WriteLine("   The OPPlayBack download protocol is best-effort.");
            Console.WriteLine("   See the TODO in DvripClient.cs for message ID candidates.");
        }

        Console.WriteLine();
    }

    Console.WriteLine("====================================");
    Console.WriteLine("✅ Done!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    Environment.Exit(1);
}
