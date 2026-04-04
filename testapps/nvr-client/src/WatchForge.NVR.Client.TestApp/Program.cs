using Microsoft.Extensions.Configuration;

Console.WriteLine("🔧 WatchForge DVRIP File Downloader");
Console.WriteLine("====================================");
Console.WriteLine();

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var host          = config["Dvrip:Host"]     ?? throw new InvalidOperationException("Dvrip:Host not configured");
var port          = int.Parse(config["Dvrip:Port"] ?? "34567");
var username      = config["Dvrip:Username"] ?? throw new InvalidOperationException("Dvrip:Username not configured");
var password      = config["Dvrip:Password"] ?? throw new InvalidOperationException("Dvrip:Password not configured");
var queryDays     = int.Parse(config["Dvrip:QueryDays"]     ?? "7");
var downloadCount = int.Parse(config["Dvrip:DownloadCount"] ?? "1");
var downloadDir   = config["Dvrip:DownloadDir"] is { Length: > 0 } d ? d : Directory.GetCurrentDirectory();

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

    // ── 2. Query files ────────────────────────────────────────────────────────
    var from = DateTime.Now.Date.AddDays(-queryDays);
    var to   = DateTime.Now;

    Console.WriteLine($"🔍 Querying files  {from:yyyy-MM-dd} → {to:yyyy-MM-dd HH:mm:ss}  (last {queryDays} day(s))");

    var allFiles = new List<NvrFile>();
    for (int ch = 0; ch < login.ChannelNum; ch++)
    {
        var files = await client.QueryFilesAsync(from, to, ch);
        allFiles.AddRange(files);
    }

    allFiles = allFiles
        .DistinctBy(f => f.FileName)
        .OrderBy(f => f.BeginTime)
        .ToList();

    Console.WriteLine();

    // ── 3. Print file list ────────────────────────────────────────────────────
    Console.WriteLine($"📂 Found {allFiles.Count} file(s):");
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

    // ── 4. Download N files ───────────────────────────────────────────────────
    var toDownload = allFiles.Take(downloadCount).ToList();
    Console.WriteLine($"⬇️  Downloading {toDownload.Count} of {allFiles.Count} file(s) → {downloadDir}");
    Console.WriteLine();

    int succeeded = 0, failed = 0;
    for (int i = 0; i < toDownload.Count; i++)
    {
        var file     = toDownload[i];
        var destName = Path.GetFileName(file.FileName);
        var destPath = Path.Combine(downloadDir, destName);

        Console.WriteLine($"   [{i + 1}/{toDownload.Count}] {destName}  ({file.FileLengthMB:F1} MB)");

        long lastReported = 0;
        var progress = new Progress<long>(bytes =>
        {
            if (bytes - lastReported >= 1_048_576 || bytes >= file.FileLengthBytes)
            {
                lastReported = bytes;
                var pct = file.FileLengthBytes > 0 ? bytes * 100 / file.FileLengthBytes : 0;
                Console.Write($"\r         Progress: {bytes / 1_048_576.0:F1} MB / {file.FileLengthMB:F1} MB ({pct}%)   ");
            }
        });

        try
        {
            await client.DownloadFileAsync(file, destPath, progress);
            Console.WriteLine();
            Console.WriteLine($"         ✅ {destPath}");
            succeeded++;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"         ⚠️  Failed: {ex.Message}");
            Console.WriteLine("            (See TODO in DvripClient.cs for OPPlayBack message ID candidates)");
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
