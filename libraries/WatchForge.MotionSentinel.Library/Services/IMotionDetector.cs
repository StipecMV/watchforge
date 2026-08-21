namespace WatchForge.MotionSentinel.Library.Services;

/// <summary>
/// Analyses a sequence of video frames and returns the spatial regions where motion occurred.
/// Implementations are stateful — call <see cref="Reset"/> between independent videos.
/// S22p: implementuje IDisposable — všetky detektory držia natívne OpenCV zdroje
/// (Mat/BackgroundSubtractor), aby ich AnalyzeJobHandler mohol `using` a factory
/// vrátiť interface.
/// </summary>
public interface IMotionDetector : IDisposable
{
    /// <summary>
    /// Analyses <paramref name="currentFrame"/> against the previous frame and returns
    /// all regions where motion was detected. Returns an empty list for the first frame.
    /// </summary>
    Task<IReadOnlyList<MotionRegion>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default);

    /// <summary>Discards any accumulated state so the next frame is treated as the first.</summary>
    void Reset();
}
