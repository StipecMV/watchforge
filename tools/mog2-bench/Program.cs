using System.Diagnostics;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;
using WatchForge.MotionSentinel.Library.VideoSources;

// S22p: benchmark štyroch motion detektorov (Farneback / MOG2 / KNN / AbsDiff)
// na reálnych stiahnutých NVR nahrávkach. Meriame: čas/motion krok, počet snímok
// s pohybom, celkový čas. Všetky na rovnakom vstupe pri 1 fps / 1080p (maxWidth 1920).
//
// Použitie:
//   dotnet run --project tools/mog2-bench -- "~/watchforge-media/tmp" [intervalMs]

var dir = args.Length > 0 ? args[0] : "~/watchforge-media/tmp";
var intervalMs = args.Length > 1 ? int.Parse(args[1]) : 1000;
dir = dir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

var files = Directory.GetFiles(dir, "*.mp4")
    .OrderBy(f => f)
    .ToArray();

// Definície detektorov cez MotionDetectorFactory (S22p) — rovnaký vstup pre všetky
var algos = new (string Name, MotionAlgorithm Algo, DetectionOptions Opts)[]
{
    ("Farneback", MotionAlgorithm.Farneback, new DetectionOptions()),
    ("MOG2",      MotionAlgorithm.Mog2,      new DetectionOptions { Mog2History = 500, Mog2VarThreshold = 16.0 }),
    ("KNN",       MotionAlgorithm.Knn,       new DetectionOptions { KnnHistory = 500, KnnDist2Threshold = 400.0 }),
    ("AbsDiff",   MotionAlgorithm.AbsDiff,   new DetectionOptions { AbsDiffThreshold = 25.0 }),
};

Console.WriteLine($"Benchmark: {string.Join(" / ", algos.Select(a => a.Name))} — {files.Length} videí, {intervalMs} ms interval");
Console.WriteLine();

foreach (var file in files)
{
    var fi = new FileInfo(file);
    var name = fi.Name;
    var sizeMb = fi.Length / 1024.0 / 1024.0;

    Console.WriteLine($"{name}  ({sizeMb:F0} MB)");
    foreach (var a in algos)
    {
        using var src = new FileVideoSource(file, maxWidth: 1920);
        using var detector = MotionDetectorFactory.Create(a.Opts with { Algorithm = a.Algo });
        detector.Reset();
        var (time, motion, frames) = Run(src, detector, intervalMs);
        int width = src.Width;
        Console.WriteLine($"   {a.Name,-10} {frames,5} framov, {motion,4} s pohybom, {time/1000.0,6:F1} s total, {((frames>0)?((double)time/frames):0):F1} ms/f   ({width}px)");
    }
    Console.WriteLine();
}

static (long TimeMs, int MotionFrames, int Frames) Run(
    FileVideoSource src,
    IMotionDetector detector,
    int intervalMs)
{
    var sw = Stopwatch.StartNew();
    int motion = 0, frames = 0;
    foreach (var frame in src.GetFramesAsync(intervalMs).ToBlockingEnumerable())
    {
        using (frame)
        {
            var regions = detector.DetectAsync(frame, CancellationToken.None).GetAwaiter().GetResult();
            frames++;
            if (regions.Count > 0) motion++;
        }
    }
    sw.Stop();
    return (sw.ElapsedMilliseconds, motion, frames);
}
