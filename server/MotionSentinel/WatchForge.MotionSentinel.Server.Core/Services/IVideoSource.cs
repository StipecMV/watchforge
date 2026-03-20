namespace WatchForge.MotionSentinel.Server.Core.Services;

/// <summary>
/// Abstracts a video source that yields decoded frames.
/// Metadata properties are available immediately after construction;
/// frames are produced lazily via <see cref="GetFramesAsync"/>.
/// </summary>
public interface IVideoSource : IDisposable
{
    /// <summary>Total duration of the video in milliseconds.</summary>
    long DurationMs { get; }

    /// <summary>Frame width in pixels.</summary>
    int Width { get; }

    /// <summary>Frame height in pixels.</summary>
    int Height { get; }

    /// <summary>Frames per second as reported by the container.</summary>
    float FrameRate { get; }

    /// <summary>
    /// Yields decoded frames sampled at the given interval.
    /// Each yielded <see cref="VideoFrame"/> must be disposed by the caller.
    /// </summary>
    /// <param name="intervalMs">Minimum gap between yielded frames in milliseconds.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<VideoFrame> GetFramesAsync(
        int intervalMs = 500,
        CancellationToken ct = default);
}
