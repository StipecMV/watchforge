namespace WatchForge.MotionSentinel.Server.Core.Models;

/// <summary>Technical metadata extracted from the source video file.</summary>
public sealed record VideoMetadata
{
    /// <summary>Total duration of the video in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Frames per second as reported by the container.</summary>
    public float FrameRate { get; init; }

    /// <summary>Number of frames actually passed through the motion detector.</summary>
    public int TotalFramesAnalyzed { get; init; }
}
