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

var startTimeStr    = config["Dvrip:StartTime"]      ?? throw new InvalidOperationException("Dvrip:StartTime not configured");
var durationMinutes = int.Parse(config["Dvrip:DurationMinutes"] ?? "60");
var channels        = config.GetSection("Dvrip:Channels").Get<int[]>()
                      ?? throw new InvalidOperationException("Dvrip:Channels not configured");

var from = DateTime.Parse(startTimeStr);
var to   = from.AddMinutes(durationMinutes);

Directory.CreateDirectory(downloadDir);

// ── 1. Query phase (sequential, single connection) ────────────────────────────
var filesByChannel = new Dictionary<int, List<NvrFile>>();

Console.WriteLine($"🔌 Connecting to {host}:{port} ...");
using (var queryClient = new DvripClient(host, port, username, password))
{
    var login = await queryClient.LoginAsync();
    Console.WriteLine("✅ Login OK");
    Console.WriteLine($"   Device type  : {login.DeviceType}");
    Console.WriteLine($"   Channels     : {login.ChannelNum}");
    Console.WriteLine($"   Session ID   : {login.SessionIdHex}");
    Console.WriteLine($"   Keep-alive   : {login.AliveInterval}s");
    Console.WriteLine();

    Console.WriteLine($"🔍 Querying channels {channels[0]}-{channels[^1]}  {from:yyyy-MM-dd HH:mm:ss} -> {to:HH:mm:ss}");

    foreach (var ch in channels)
    {
        var files = await queryClient.QueryFilesAsync(from, to, ch);
        Console.WriteLine($"   Channel {ch}: {files.Count} file(s)");
        if (files.Count > 0)
            filesByChannel[ch] = files.DistinctBy(f => f.FileName).OrderBy(f => f.BeginTime).ToList();
    }
}

Console.WriteLine();

var totalFiles = filesByChannel.Values.Sum(f => f.Count);

if (totalFiles == 0)
{
    Console.WriteLine("📭 No files found in the given period.");
    Console.WriteLine();
    Console.WriteLine("====================================");
    Console.WriteLine("✅ Done!");
    return;
}

Console.WriteLine($"📂 Found {totalFiles} file(s) across {filesByChannel.Count} channel(s):");
foreach (var (ch, files) in filesByChannel.OrderBy(kv => kv.Key))
{
    foreach (var f in files)
    {
        var size = f.FileLengthMB >= 1.0
            ? $"{f.FileLengthMB:F1} MB"
            : $"{f.FileLengthBytes / 1024.0:F1} KB";
        Console.WriteLine($"   📹 [Ch{ch}] {Path.GetFileName(f.FileName)}  {f.BeginTime:HH:mm:ss}-{f.EndTime:HH:mm:ss}  [{size}]");
    }
}
Console.WriteLine();

// ── 2. Download phase (parallel per channel) ───────────────────────────────────
Console.WriteLine($"📥 Downloading {totalFiles} file(s) to {downloadDir}");
Console.WriteLine($"   ({filesByChannel.Count} channel(s) in parallel)");
Console.WriteLine();

var consoleLock = new object();
int succeeded = 0, failed = 0;

var channelTasks = filesByChannel.Select(async kv =>
{
    var ch    = kv.Key;
    var files = kv.Value;

    // Each channel gets its own client; DownloadFileAsync reconnects per file internally.
    using var client = new DvripClient(host, port, username, password);

    for (int i = 0; i < files.Count; i++)
    {
        var file     = files[i];
        var destName = Path.GetFileName(file.FileName);
        var destPath = Path.Combine(downloadDir, destName);

        lock (consoleLock)
            Console.WriteLine($"   ⬇️  [Ch{ch} {i + 1}/{files.Count}] {destName}  ({file.FileLengthMB:F1} MB)");

        long lastReported = 0;
        var progress = new Progress<long>(bytes =>
        {
            // Report every 10 MB or at completion to avoid flooding the console.
            if (bytes - lastReported >= 10_485_760 || bytes >= file.FileLengthBytes)
            {
                lastReported = bytes;
                var pct = file.FileLengthBytes > 0 ? bytes * 100 / file.FileLengthBytes : 0;
                lock (consoleLock)
                    Console.WriteLine($"      [Ch{ch}] {bytes / 1_048_576.0:F0} MB / {file.FileLengthMB:F0} MB ({pct}%)");
            }
        });

        try
        {
            var outputPath = await client.DownloadFileAsync(file, destPath, progress);
            lock (consoleLock)
            {
                Console.WriteLine($"      ✅ [Ch{ch}] Done -> {Path.GetFileName(outputPath)}");
                Console.WriteLine($"      🎬 [Ch{ch}] Preview: ffplay \"{outputPath}\"");
            }
            Interlocked.Increment(ref succeeded);
        }
        catch (Exception ex)
        {
            lock (consoleLock)
                Console.WriteLine($"      ❌ [Ch{ch}] Failed: {ex.Message}");
            Interlocked.Increment(ref failed);
        }
    }
});

await Task.WhenAll(channelTasks);

Console.WriteLine();
Console.WriteLine("====================================");
Console.WriteLine(failed == 0
    ? $"✅ Done!  {succeeded} downloaded."
    : $"⚠️  Done!  {succeeded} downloaded, {failed} failed.");
