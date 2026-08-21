using System.Text.Json;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;
using WatchForge.MotionSentinel.Library.VideoSources;

// S22p: motion-export — spustí 4 detektory (Farneback/MOG2/KNN/AbsDiff) na JEDNOM
// videu a vyexportuje per-frame detekcie do JSON pre samostatnú porovnávaciu appku.
//
// Použitie:
//   dotnet run --project tools/motion-export -- "video.mp4" "out.json" [intervalMs]
//   (intervalMs default 1000 = 1 fps)

var videoPath = args.Length > 0 ? args[0] : throw new ArgumentException("video path required");
var outPath = args.Length > 1 ? args[1] : "motion-export.json";
var intervalMs = args.Length > 2 ? int.Parse(args[2]) : 1000;

var algorithms = new (string Key, string Name, MotionAlgorithm Algo, DetectionOptions Opts)[]
{
    ("farneback", "Farneback", MotionAlgorithm.Farneback, new DetectionOptions()),
    ("mog2",      "MOG2",      MotionAlgorithm.Mog2,      new DetectionOptions { Mog2History = 500, Mog2VarThreshold = 16.0 }),
    ("knn",       "KNN",       MotionAlgorithm.Knn,       new DetectionOptions { KnnHistory = 500, KnnDist2Threshold = 400.0 }),
    ("absdiff",   "AbsDiff",   MotionAlgorithm.AbsDiff,   new DetectionOptions { AbsDiffThreshold = 25.0 }),
};

Console.WriteLine($"Exporting detections: {videoPath} → {outPath} (interval {intervalMs} ms)");

using var probe = new FileVideoSource(videoPath, maxWidth: 1920);
int videoWidth = probe.Width, videoHeight = probe.Height;

// frameIndex → timestampMs + per-algorithm regions
var frames = new List<ExportedFrame>();

for (int fi = 0; fi < algorithms.Length; fi++)
{
    var (key, name, algo, opts) = algorithms[fi];
    using var src = new FileVideoSource(videoPath, maxWidth: 1920);
    using var detector = MotionDetectorFactory.Create(opts with { Algorithm = algo });
    detector.Reset();

    Console.WriteLine($"  [{key}] spracovávam...");
    foreach (var frame in src.GetFramesAsync(intervalMs).ToBlockingEnumerable())
    {
        using (frame)
        {
            long ts = frame.TimestampMs;
            var regions = detector.DetectAsync(frame, CancellationToken.None).GetAwaiter().GetResult();

            var entry = frames.FirstOrDefault(f => f.TimestampMs == ts);
            if (entry == null)
            {
                entry = new ExportedFrame { TimestampMs = ts };
                frames.Add(entry);
            }
            entry.Regions[key] = regions
                .Select(r => new ExportedRegion { X = r.X, Y = r.Y, W = r.Width, H = r.Height, I = r.Intensity })
                .ToList();
        }
    }
}

frames.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));

var doc = new ExportDocument
{
    VideoPath = videoPath,
    Width = videoWidth,
    Height = videoHeight,
    IntervalMs = intervalMs,
    Algorithms = algorithms.Select(a => new ExportAlgoInfo { Key = a.Key, Name = a.Name }).ToList(),
    Frames = frames,
};

var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
{
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // appka číta camelCase (doc.frames, f.timestampMs)
});
File.WriteAllText(outPath, json);
Console.WriteLine($"Hotovo: {frames.Count} framov, {json.Length / 1024} KB JSON → {outPath}");
Console.WriteLine($"Použi v samostatnej appke (otvor tools/motion-compare-ui.html cez browser alebo http server).");

public sealed class ExportDocument
{
    public string VideoPath { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int IntervalMs { get; set; }
    public List<ExportAlgoInfo> Algorithms { get; set; } = new();
    public List<ExportedFrame> Frames { get; set; } = new();
}

public sealed class ExportAlgoInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class ExportedFrame
{
    public long TimestampMs { get; set; }
    public Dictionary<string, List<ExportedRegion>> Regions { get; set; } = new();
}

public sealed class ExportedRegion
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float I { get; set; }
}
