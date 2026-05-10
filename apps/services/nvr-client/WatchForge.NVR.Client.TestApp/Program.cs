Console.WriteLine("🔧 WatchForge DVRIP File Downloader");
Console.WriteLine("====================================");
Console.WriteLine();

// ── Config ────────────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var host        = config["Dvrip:Host"]     ?? throw new InvalidOperationException("Dvrip:Host not configured");
var port        = int.Parse(config["Dvrip:Port"] ?? "34567");
var username    = config["Dvrip:Username"] ?? throw new InvalidOperationException("Dvrip:Username not configured");
var password    = config["Dvrip:Password"] ?? throw new InvalidOperationException("Dvrip:Password not configured");
var downloadDir = config["Dvrip:DownloadDir"] is { Length: > 0 } d ? d : Directory.GetCurrentDirectory();
var channels    = config.GetSection("Dvrip:Channels").Get<int[]>()
                  ?? throw new InvalidOperationException("Dvrip:Channels not configured");

var mode         = (config["Dvrip:Mode"] ?? "oneshot").ToLowerInvariant();
var outputFormat = (config["Dvrip:OutputFormat"] ?? "mp4").ToLowerInvariant();
var pollInterval = int.Parse(config["Dvrip:PollIntervalSeconds"] ?? "60");

if (outputFormat is not "mp4" and not "mkv")
{
    Console.WriteLine($"   ⚠️  Unknown OutputFormat '{outputFormat}', defaulting to 'mp4'");
    outputFormat = "mp4";
}

Directory.CreateDirectory(downloadDir);

// ── Graceful shutdown ─────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n🛑 Shutdown requested, finishing current downloads...");
    cts.Cancel();
};

Console.WriteLine($"   Mode         : {mode.ToUpper()}");
Console.WriteLine($"   Output format: {outputFormat.ToUpper()}");
Console.WriteLine($"   Download dir : {downloadDir}");
Console.WriteLine();

// ── Run ───────────────────────────────────────────────────────────────────────
if (mode == "infinite")
    await RunInfiniteModeAsync(cts.Token);
else
    await RunOneshotModeAsync(cts.Token);

// ═════════════════════════════════════════════════════════════════════════════
// ONESHOT MODE
// ═════════════════════════════════════════════════════════════════════════════
async Task RunOneshotModeAsync(CancellationToken ct)
{
    var startTimeStr    = config["Dvrip:StartTime"]
                          ?? throw new InvalidOperationException("Dvrip:StartTime required in oneshot mode");
    var durationMinutes = int.Parse(config["Dvrip:DurationMinutes"] ?? "60");
    var from = DateTime.Parse(startTimeStr);
    var to   = from.AddMinutes(durationMinutes);

    // Query
    var filesByChannel = await QueryAllChannelsAsync(from, to, channels, ct);
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
    PrintFileList(filesByChannel);
    Console.WriteLine();

    // Download
    Console.WriteLine($"📥 Downloading {totalFiles} file(s) to {downloadDir}  [{outputFormat.ToUpper()}]");
    Console.WriteLine($"   ({filesByChannel.Count} channel(s) in parallel)");
    Console.WriteLine();

    var (succeeded, failed) = await DownloadAllAsync(filesByChannel, stateService: null, ct);

    Console.WriteLine();
    Console.WriteLine("====================================");
    Console.WriteLine(failed == 0
        ? $"✅ Done!  {succeeded} downloaded."
        : $"⚠️  Done!  {succeeded} downloaded, {failed} failed.");
}

// ═════════════════════════════════════════════════════════════════════════════
// INFINITE MODE
// ═════════════════════════════════════════════════════════════════════════════
async Task RunInfiniteModeAsync(CancellationToken ct)
{
    var stateService = new DownloadStateService(downloadDir);
    Console.WriteLine($"♾️  Infinite mode — polling every {pollInterval}s");
    Console.WriteLine($"   State file   : {Path.Combine(downloadDir, "downloaded.json")}");
    Console.WriteLine($"   Already known: {stateService.TotalDownloaded} file(s)");
    Console.WriteLine();

    int pollCount = 0;
    while (!ct.IsCancellationRequested)
    {
        pollCount++;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 Poll #{pollCount}");

        try
        {
            // Query entire available history (large window)
            var from = new DateTime(2020, 1, 1);
            var to   = DateTime.Now.AddDays(1);

            var allFiles = await QueryAllChannelsAsync(from, to, channels, ct);

            // Filter out already-downloaded files
            var newFiles = new Dictionary<int, List<NvrFile>>();
            int skipCount = 0;
            foreach (var (ch, files) in allFiles)
            {
                var toDownload = files.Where(f => !stateService.IsDownloaded(ch, f.FileName)).ToList();
                skipCount += files.Count - toDownload.Count;
                if (toDownload.Count > 0)
                    newFiles[ch] = toDownload;
            }

            var totalNew = newFiles.Values.Sum(f => f.Count);
            Console.WriteLine($"   Found {allFiles.Values.Sum(f => f.Count)} total, {skipCount} already downloaded, {totalNew} new");

            if (totalNew > 0)
            {
                Console.WriteLine();
                var (succeeded, failed) = await DownloadAllAsync(newFiles, stateService, ct);
                Console.WriteLine($"   ✅ {succeeded} downloaded{(failed > 0 ? $", {failed} failed" : "")}");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Poll error: {ex.Message}");
        }

        if (ct.IsCancellationRequested) break;

        Console.WriteLine($"   ⏳ Next poll in {pollInterval}s  (Ctrl+C to stop)");
        Console.WriteLine();
        try { await Task.Delay(TimeSpan.FromSeconds(pollInterval), ct); }
        catch (OperationCanceledException) { break; }
    }

    Console.WriteLine();
    Console.WriteLine("====================================");
    Console.WriteLine("✅ Shutdown complete.");
}

// ═════════════════════════════════════════════════════════════════════════════
// SHARED HELPERS
// ═════════════════════════════════════════════════════════════════════════════

async Task<Dictionary<int, List<NvrFile>>> QueryAllChannelsAsync(
    DateTime from, DateTime to, int[] chList, CancellationToken ct)
{
    var result = new Dictionary<int, List<NvrFile>>();

    Console.WriteLine($"🔌 Connecting to {host}:{port} ...");
    using var queryClient = new DvripClient(host, port, username, password);
    var login = await queryClient.LoginAsync(ct);
    Console.WriteLine("✅ Login OK");
    Console.WriteLine($"   Device type  : {login.DeviceType}");
    Console.WriteLine($"   Session ID   : {login.SessionIdHex}");
    Console.WriteLine();

    Console.WriteLine($"🔍 Querying {chList.Length} channel(s)  {from:yyyy-MM-dd HH:mm} → {to:yyyy-MM-dd HH:mm}");
    foreach (var ch in chList)
    {
        var files = (await queryClient.QueryFilesAsync(from, to, ch, ct))
            .Where(f => f.BeginTime >= from)
            .DistinctBy(f => f.FileName)
            .OrderBy(f => f.BeginTime)
            .ToList();
        Console.WriteLine($"   Channel {ch}: {files.Count} file(s)");
        if (files.Count > 0)
            result[ch] = files;
    }
    Console.WriteLine();
    return result;
}

async Task<(int succeeded, int failed)> DownloadAllAsync(
    Dictionary<int, List<NvrFile>> filesByChannel,
    DownloadStateService? stateService,
    CancellationToken ct)
{
    var consoleLock = new object();
    int succeeded = 0, failed = 0;

    var channelTasks = filesByChannel.Select(async kv =>
    {
        var ch    = kv.Key;
        var files = kv.Value;
        using var client = new DvripClient(host, port, username, password);

        for (int i = 0; i < files.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var file      = files[i];
            var nvrBase   = Path.GetFileNameWithoutExtension(file.FileName);
            var cleanName = Regex.Replace(nvrBase, @"\[[^\]]*\]", "");
            var destName  = $"[Ch{ch}]_{file.BeginTime:yyyy-MM-dd}_{cleanName}.{outputFormat}";
            var rawPath   = Path.Combine(downloadDir, Path.ChangeExtension(destName, ".downloading"));
            var finalPath = Path.Combine(downloadDir, destName);

            // Skip if final file already exists on disk (e.g. after crash)
            if (File.Exists(finalPath))
            {
                await stateService?.MarkDownloadedAsync(ch, file.FileName)!;
                Interlocked.Increment(ref succeeded);
                continue;
            }

            lock (consoleLock)
                Console.WriteLine($"   ⬇️  [Ch{ch} {i + 1}/{files.Count}] {destName}  ({file.FileLengthMB:F1} MB)");

            long lastReported = 0;
            var progress = new Progress<long>(bytes =>
            {
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
                var outputPath = await client.DownloadFileAsync(file, rawPath, outputFormat, progress, ct);
                // Rename to final name if still has .downloading extension
                if (outputPath != rawPath && File.Exists(outputPath))
                {
                    var finalConverted = Path.Combine(downloadDir, Path.GetFileName(outputPath));
                    if (outputPath != finalConverted)
                        File.Move(outputPath, finalConverted, overwrite: true);
                }
                if (File.Exists(rawPath)) File.Delete(rawPath);

                lock (consoleLock)
                    Console.WriteLine($"      ✅ [Ch{ch}] → {destName}");

                if (stateService is not null)
                    await stateService.MarkDownloadedAsync(ch, file.FileName);
                Interlocked.Increment(ref succeeded);
            }
            catch (Exception ex)
            {
                lock (consoleLock)
                    Console.WriteLine($"      ❌ [Ch{ch}] Failed: {ex.Message}");
                if (File.Exists(rawPath)) try { File.Delete(rawPath); } catch { }
                Interlocked.Increment(ref failed);
            }
        }
    });

    await Task.WhenAll(channelTasks);
    return (succeeded, failed);
}

void PrintFileList(Dictionary<int, List<NvrFile>> filesByChannel)
{
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
}
