using System.Diagnostics;
using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;
using WatchForge.MotionSentinel.Library.VideoSources;

// WatchForge CPU benchmark (S13 / research agent 2026-08-08)
// Baseline: Farneback + HOG pri 720p/1080p/1440p, bottleneck fázy, škálovateľnosť.
// Spustenie: dotnet run --project tools/cpu-benchmark [--frames N]

var frames = args.Length > 0 && args[0] == "--frames" && args.Length > 1 ? int.Parse(args[1]) : 30;

var tmp = Path.Combine(Path.GetTempPath(), "wf-bench");
Directory.CreateDirectory(tmp);

Console.WriteLine("=== WatchForge CPU benchmark (CPU-only produkčná cesta) ===");
Console.WriteLine($"Frames per measurement: {frames}, CPU: {Environment.ProcessorCount} cores");
Console.WriteLine();

// ── 1. BASELINE: Farneback + HOG pri 720p / 1080p / 1440p ──
var resolutions = new (string Name, int W, int H)[] { ("720p", 1280, 720), ("1080p", 1920, 1080), ("1440p", 2560, 1440) };
var baseline = new List<(string Name, double FarnebackMs, double HogMs, double CombinedMs)>();

foreach (var (name, w, h) in resolutions)
{
    var video = await GenerateVideoAsync(tmp, $"{name}.mp4", w, h);
    var farneback = new OpticalFlowDetector();
    var hog = new HogPersonDetector();

    // Farneback
    var fbMs = await MeasureDetectorAsync(video, (f, ct) => farneback.DetectAsync(f, ct), frames);
    // HOG (samostatne, na tom istom videu — nový source)
    var hogMs = await MeasureDetectorAsync(video, (f, ct) => hog.DetectAsync(f, ct), frames);
    // Kombinovaná pipeline: Farneback → (ak pohyb) HOG — simulácia AnalyzeJobHandler
    var combinedMs = await MeasureCombinedAsync(video, farneback, hog, frames);

    baseline.Add((name, fbMs, hogMs, combinedMs));
    Console.WriteLine($"  {name}: Farneback {fbMs:F1} ms/f ({1000.0 / fbMs:F0} fps) | HOG {hogMs:F1} ms/f ({1000.0 / hogMs:F0} fps) | combo {combinedMs:F1} ms/f ({1000.0 / combinedMs:F0} fps)");

    farneback.Dispose(); hog.Dispose();
}

// ── 2. BOTTLENECK: fázy pipeline na 1080p ──
Console.WriteLine();
Console.WriteLine("=== Bottleneck analýza (1080p) ===");
var bVideo = await GenerateVideoAsync(tmp, "bottleneck.mp4", 1920, 1080);
await MeasurePhasesAsync(bVideo, frames);

// ── 2b. KALIBRÁCIA: S8-1 benchmark meral 320x240 — porovnanie so 1080p ──
Console.WriteLine();
Console.WriteLine("=== Kalibrácia S8-1 (320×240 — čo meral pôvodný benchmark) ===");
var calibVideo = await GenerateVideoAsync(tmp, "calib.mp4", 320, 240);
var calibFb = await MeasureDetectorAsync(calibVideo, (f, ct) => new OpticalFlowDetector().DetectAsync(f, ct), frames);
Console.WriteLine($"  320×240: Farneback {calibFb:F1} ms/f ({(1000.0 / calibFb):F0} fps) ← S8-1 „27 ms\" meral toto");
var realVideo = await GenerateVideoAsync(tmp, "realistic.mp4", 1920, 1080);
var realFb = await MeasureDetectorAsync(realVideo, (f, ct) => new OpticalFlowDetector().DetectAsync(f, ct), frames);
Console.WriteLine($"  1080p (realistické, menej textúr): Farneback {realFb:F1} ms/f ({(1000.0 / realFb):F0} fps)");
using (var realHog = new HogPersonDetector())
using (var realFbDet = new OpticalFlowDetector())
{
    var realCombo = await MeasureCombinedAsync(realVideo, realFbDet, realHog, frames);
    Console.WriteLine($"  1080p (realistické) combo: {realCombo:F1} ms/f ({(1000.0 / realCombo):F0} fps)");
}

// ── 3. ŠKÁLOVATEĽNOSŤ: 1/2/4 paralelné analýzy (1080p, kombinovaná pipeline) ──
Console.WriteLine();
Console.WriteLine("=== Škálovateľnosť (1080p, paralelné analýzy) ===");
var sVideo = await GenerateVideoAsync(tmp, "scale.mp4", 1920, 1080);
foreach (var n in new[] { 1, 2, 4 })
{
    await MeasureParallelAsync(sVideo, n, frames);
}

// ── 4. 4K downscale kompromis ──
Console.WriteLine();
Console.WriteLine("=== 4K vstup (3840×2160) — downscale kompromis ===");
var v4k = await GenerateVideoAsync(tmp, "4k.mp4", 3840, 2160);
foreach (var maxW in new int?[] { 1920, 1280, null })
{
    var label = maxW is null ? "žiadny (4K raw)" : $"{maxW}px ({(maxW == 1920 ? "1080p" : "720p")})";
    using var source = new FileVideoSource(v4k, maxW);
    var sw = Stopwatch.StartNew();
    int n = 0;
    using var det = new OpticalFlowDetector();
    await foreach (var frame in source.GetFramesAsync(500, CancellationToken.None))
    {
        using (frame) { await det.DetectAsync(frame); }
        if (++n >= frames) break;
    }
    sw.Stop();
    var ms = sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
    Console.WriteLine($"  maxWidth={label}: source {source.Width}x{source.Height}, {ms:F1} ms/f, {(1000.0 / ms):F0} fps");
}

Console.WriteLine();
Console.WriteLine("=== Hotovo. Merania sú v stdout; vygenerované videá: " + tmp + " ===");

// ── Helpers ──

static async Task<string> GenerateVideoAsync(string dir, string name, int w, int h)
{
    var path = Path.Combine(dir, name);
    if (File.Exists(path)) return path;
    // testsrc2 s posúvajúcim sa prvkom = pohyb pre Farneback aj HOG-šum
    var filter = name.StartsWith("realistic")
        ? $"color=c=gray:size={w}x{h}:rate=25:duration=10,drawbox=x=100+t*20:y=200:w=300:h=300:color=white:t=fill"
        : $"testsrc2=size={w}x{h}:rate=25:duration=10";
    var psi = new ProcessStartInfo("ffmpeg", $"-y -f lavfi -i {filter} -pix_fmt yuv420p -c:v libx264 -preset veryfast -crf 28 {path}")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    };
    using var p = Process.Start(psi)!;
    await p.WaitForExitAsync();
    return path;
}

static async Task<double> MeasureDetectorAsync(string video, Func<VideoFrame, CancellationToken, Task> detect, int frames)
{
    using var source = new FileVideoSource(video);
    var sw = Stopwatch.StartNew();
    int n = 0;
    await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
    {
        using (frame) { await detect(frame, CancellationToken.None); }
        if (++n >= frames) break;
    }
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
}

static async Task<double> MeasureCombinedAsync(string video, OpticalFlowDetector farneback, HogPersonDetector hog, int frames)
{
    using var source = new FileVideoSource(video);
    var sw = Stopwatch.StartNew();
    int n = 0;
    await foreach (var frame in source.GetFramesAsync(0, CancellationToken.None))
    {
        using (frame)
        {
            var regions = await farneback.DetectAsync(frame, CancellationToken.None);
            if (regions.Count > 0)
                await hog.DetectAsync(frame, CancellationToken.None);
        }
        if (++n >= frames) break;
    }
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
}

static async Task MeasurePhasesAsync(string video, int frames)
{
    // decode (bez detekcie) vs decode+Farneback vs decode+Farneback+HOG
    using var src1 = new FileVideoSource(video);
    var sw = Stopwatch.StartNew();
    int n = 0;
    await foreach (var frame in src1.GetFramesAsync(0, CancellationToken.None))
    {
        if (++n >= frames) break;
    }
    sw.Stop();
    var decodeOnly = sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
    Console.WriteLine($"  decode-only: {decodeOnly:F1} ms/f ({(1000.0 / decodeOnly):F0} fps)");

    using var det = new OpticalFlowDetector();
    using var src2 = new FileVideoSource(video);
    sw.Restart(); n = 0;
    await foreach (var frame in src2.GetFramesAsync(0, CancellationToken.None))
    {
        using (frame) { await det.DetectAsync(frame, CancellationToken.None); }
        if (++n >= frames) break;
    }
    sw.Stop();
    var decodeFb = sw.Elapsed.TotalMilliseconds / Math.Max(1, n - 1);
    Console.WriteLine($"  decode+Farneback: {decodeFb:F1} ms/f → Farneback čistý ≈ {Math.Max(0, decodeFb - decodeOnly):F1} ms/f");
}

static async Task MeasureParallelAsync(string video, int parallel, int frames)
{
    var before = GC.GetTotalMemory(forceFullCollection: true) / 1024.0 / 1024.0;
    var proc = Process.GetCurrentProcess();
    var cpuBefore = proc.TotalProcessorTime;
    var wall = Stopwatch.StartNew();

    var tasks = Enumerable.Range(0, parallel).Select(_ => Task.Run(async () =>
    {
        using var det = new OpticalFlowDetector();
        using var hog = new HogPersonDetector();
        using var source = new FileVideoSource(video);
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
    var wallSec = wall.Elapsed.TotalSeconds;
    var cpuPct = wallSec > 0 ? cpuUsed / wallSec / Environment.ProcessorCount * 100.0 : 0;
    var after = GC.GetTotalMemory(forceFullCollection: true) / 1024.0 / 1024.0;

    Console.WriteLine($"  {parallel} analýz: wall {wallSec:F1} s ({frames} f/analýza), CPU {cpuPct:F0}% ({cpuUsed:F1}s / {wallSec:F1}s wall), mem +{after - before:F0} MB (GC)");
}
