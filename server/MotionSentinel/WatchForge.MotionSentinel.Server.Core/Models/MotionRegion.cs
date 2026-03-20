namespace WatchForge.MotionSentinel.Server.Core.Models;

/// <summary>
/// A bounding box within a video frame where motion was detected.
/// All coordinate and size values are normalised to the range [0, 1].
/// </summary>
public sealed record MotionRegion
{
    /// <summary>Normalised X coordinate of the left edge of the bounding box.</summary>
    public float X { get; init; }

    /// <summary>Normalised Y coordinate of the top edge of the bounding box.</summary>
    public float Y { get; init; }

    /// <summary>Normalised width of the bounding box.</summary>
    public float Width { get; init; }

    /// <summary>Normalised height of the bounding box.</summary>
    public float Height { get; init; }

    /// <summary>Optical flow magnitude. 0 = no motion, 1 = maximum.</summary>
    public float Intensity { get; init; }
}
