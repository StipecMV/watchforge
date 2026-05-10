namespace WatchForge.MotionSentinel.Server.Service.Detection;

/// <summary>Tuning parameters for the motion detection algorithm, bound from the <c>Detection</c> config section.</summary>
public sealed record DetectionOptions
{
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
}
