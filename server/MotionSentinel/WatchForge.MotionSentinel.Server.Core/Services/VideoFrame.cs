namespace WatchForge.MotionSentinel.Server.Core.Services;

/// <summary>A single decoded video frame together with its playback position.</summary>
/// <param name="NativeBuffer">The underlying native frame buffer (e.g. an OpenCV <c>Mat</c>). Disposed via <see cref="Dispose"/>.</param>
/// <param name="TimestampMs">Offset from the start of the video in milliseconds.</param>
public sealed record VideoFrame(object NativeBuffer, long TimestampMs) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose()
    {
        (NativeBuffer as IDisposable)?.Dispose();
    }
}
