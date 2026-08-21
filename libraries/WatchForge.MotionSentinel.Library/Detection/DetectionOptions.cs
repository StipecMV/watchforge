namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>Tuning parameters for the motion detection algorithm, bound from the <c>Detection</c> config section.</summary>
public sealed record DetectionOptions
{
    /// <summary>
    /// S22p: zvolený algoritmus detekcie pohybu. Default: Farneback.
    /// Prepnutie (napr. cez env/config) zmení detektor v AnalyzeJobHandler.
    /// </summary>
    public MotionAlgorithm Algorithm { get; init; } = MotionAlgorithm.Farneback;

    /// <summary>
    /// Minimum optical flow intensity (0–1) required for a pixel to be counted as motion.
    /// Lower values increase sensitivity; higher values reduce false positives. Default: 0.05.
    /// </summary>
    public float IntensityThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Minimum contour area in pixels below which a candidate region is discarded as noise.
    /// Default: 100.
    /// </summary>
    public double MinContourArea { get; init; } = 100.0;

    // ── S22o/POC: MOG2 + KNN background subtraction detektory ────────────────
    /// <summary>MOG2 história (počet framov pre naučenie pozadia). Default: 500.</summary>
    public int Mog2History { get; init; } = 500;

    /// <summary>MOG2 variance threshold — vyššia = menej citlivý, menej FP. Default: 16.</summary>
    public double Mog2VarThreshold { get; init; } = 16.0;

    /// <summary>MOG2 detectShadows — true = tiene sa označia (predné) a odfiltrujú. Default: true.</summary>
    public bool Mog2DetectShadows { get; init; } = true;

    // ── S22p: KNN + TVL1 ────────────────────────────────────────────────────
    /// <summary>KNN história. Default: 500.</summary>
    public int KnnHistory { get; init; } = 500;
    /// <summary>KNN dist2Threshold. Default: 400.</summary>
    public double KnnDist2Threshold { get; init; } = 400.0;
    /// <summary>KNN detectShadows. Default: true.</summary>
    public bool KnnDetectShadows { get; init; } = true;
    /// <summary>Abs-diff prah rozdielu (0–255) — pixel = pohyb. Default: 25.</summary>
    public double AbsDiffThreshold { get; init; } = 25.0;
}
