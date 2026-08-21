using System.Diagnostics;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;
using WatchForge.MotionSentinel.Library.VideoSources;

// WatchForge benchmark na REÁLNYCH dátach (S13-3): stiahnuté 6×15 min segmenty
// z NVR (HEVC 4K). Meria: decode, Farneback, HOG, kombinovanú pipeline,
// paralelizmus 1/2/4 a 4K downscale kompromis.
// Použitie: dotnet run --project tools/real-bench -- <dir s .mp4 súbormi> [--frames N]

var dir = args.Length > 0 ? args[0] : "/tmp/nvr-bench";
var frames = args.Length > 1 ? int.Parse(args[1]) : 30;
var videos = Directory.GetFiles(dir, "*.mp4").OrderBy(f => f).ToArray();
if (videos.Length == 0)
{
    Console.Error.WriteLine($"Žiadne .mp4 v {dir}");
    return 1;
}

Console.WriteLine($"=== WatchForge REÁLNY benchmark (S13-3) ===");
Console.WriteLine($"Videá: {videos.Length} ({videos.Length}×15 min segmentov z NVR), frames/meranie: {frames}");
foreach (var v in videos.Take(6))
{
    var fi = new FileInfo(v);
    Console.WriteLine($"  {Path.GetFileName(v)} — {fi.Length / 1048576.0:F0} MB");
}
Console.WriteLine();

// ── 1. Baseline: Farneback + HOG + combo na reálnom 1080p (downscale) ──
Console.WriteLine("=== Baseline (1080p downscale, reálne dáta) ===");
var farnebackTotal = 0.0; var hogTotal = 0.0; var comboTotal = 0.0; var count = 0;
foreach (var video in videos)
{
    farnebackTotal += await MeasureAsync(video, new OpticalFlowDetector(), null, frames);
    hogTotal += await MeasureAsync(video, new OpticalFlowDetector(), new HogPersonDetector(), frames);
    comboTotal += await MeasureAsync(video, new OpticalFlowDetector(), new HogPersonDetector(), frames);
    count++;
}
var fbAvg = farnebackTotal / count; var hogAvg = hogTotal / count; var comboAvg = comboTotal / count;
Console.WriteLine($"  Farneback (1080p): {fbAvg:F1} ms/f ({1000.0 / fbAvg:F0} fps)");
Console.WriteLine($"  HOG (1080p):       {hogAvg:F1} ms/f ({1000.0 / hogAvg:F0} fps)");
Console.WriteLine($"  Combo (1080p):     {comboAvg:F1} ms/f ({1000.0 / comboAvg:F0} fps)");
Console.WriteLine();

// ── 2. Bottleneck: decode vs Farneback ──
Console.WriteLine("=== Bottleneck (reálne dáta, 1080p) ===");
var decodeMs = await MeasureDecodeOnlyAsync(videos[0], frames);
Console.WriteLine($"  decode-only: {decodeMs:F1} ms/f ({(1000.0 / decodeMs):F0} fps)");
Console.WriteLine($"  Farneback čistý ≈ {Math.Max(0, fbAvg - decodeMs):F1} ms/f");
Console.WriteLine();

// ── 3. Škálovateľnosť: 1/2/4 paralelné analýzy ──
Console.WriteLine("=== Škálovateľnosť (reálne dáta, 1080p) ===");
foreach (var n in new[] { 1, 2, 4 })
{
    await MeasureParallelAsync(videos.Take(n).ToArray(), n, frames);
}
Console.WriteLine();

// ── 4. 4K downscale kompromis (reálne dáta) ──
Console.WriteLine("=== 4K downscale kompromis (reálne dáta) ===");
foreach (var maxW in new int?[] { 1920, 1280, null })
{
    using var source = new FileVideoSource(videos[0], maxW);
    var sw = Stopwatch.StartNew();
    int n = 0;
    using var det = new OpticalFlowDetector();
    await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
    {
        using (frame) { await det.DetectAsync(frame); }
        if (++n >= frames) break;
    }
    sw.Stop();
    var ms = sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
    var label = maxW is null ? "žiadny (4K raw)" : $"{maxW}px";
    Console.WriteLine($"  maxWidth={label}: {source.Width}x{source.Height}, {ms:F1} ms/f, {(1000.0 / ms):F0} fps");
}

Console.WriteLine();
Console.WriteLine("=== Hotovo ===");
return 0;

// ── Helpers ──

static async Task<double> MeasureAsync(string video, OpticalFlowDetector farneback, HogPersonDetector? hog, int frames)
{
    using var source = new FileVideoSource(video, maxWidth: 1920); // 1080p downscale (produkcia)
    var sw = Stopwatch.StartNew();
    int n = 0;
    await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
    {
        using (frame)
        {
            var regions = await farneback.DetectAsync(frame, CancellationToken.None);
            if (hog is not null && regions.Count > 0)
                await hog.DetectAsync(frame, CancellationToken.None);
        }
        if (++n >= frames) break;
    }
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
}

static async Task<double> MeasureDecodeOnlyAsync(string video, int frames)
{
    using var source = new FileVideoSource(video, maxWidth: 1920);
    var sw = Stopwatch.StartNew();
    int n = 0;
    await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
    {
        if (++n >= frames) break;
    }
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
}

static async Task MeasureParallelAsync(string[] videos, int parallel, int frames)
{
    var proc = Process.GetCurrentProcess();
    var cpuBefore = proc.TotalProcessorTime;
    var wall = Stopwatch.StartNew();

    var tasks = videos.Take(parallel).Select(v => Task.Run(async () =>
    {
        using var det = new OpticalFlowDetector();
        using var hog = new HogPersonDetector();
        using var source = new FileVideoSource(v, maxWidth: 1920);
        int n = 0;
        await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
        {
            using (frame)
            {
                var regions = await det.DetectAsync(frame, CancellationToken.None);
                if (regions.Count > 0) await hog.DetectAsync(frame, CancellationToken.None);
            }
            if (++n >= frames) break;
        }
    })).ToArray();
    await Task.WhenAll(tasks);
    wall.Stop();

    var cpuUsed = (proc.TotalProcessorTime - cpuBefore).TotalSeconds;
    var cpuPct = wall.Elapsed.TotalSeconds > 0
        ? cpuUsed / wall.Elapsed.TotalSeconds / Environment.ProcessorCount * 100.0 : 0;
    Console.WriteLine($"  {parallel} analýz: wall {wall.Elapsed.TotalSeconds:F1} s, CPU {cpuPct:F0}%");
}
