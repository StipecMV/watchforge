namespace WatchForge.MotionSentinel.Server.Core.Models;

/// <summary>A single interval in a recording during which motion was detected.</summary>
public sealed record MotionEvent
{
    /// <summary>Offset from the start of the video at which motion began, in milliseconds.</summary>
    public long TimestampMs { get; init; }

    /// <summary>Length of the motion interval in milliseconds (matches the frame-sampling interval).</summary>
    public long DurationMs { get; init; }

    /// <summary>Spatial regions within the frame where motion was detected.</summary>
    public IReadOnlyList<MotionRegion> Regions { get; init; } = [];
}
