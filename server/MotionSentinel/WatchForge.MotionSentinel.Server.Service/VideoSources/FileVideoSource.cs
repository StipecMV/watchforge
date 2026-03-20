using System.Runtime.CompilerServices;

namespace WatchForge.MotionSentinel.Server.Service.VideoSources;

/// <summary>
/// Extracts frames from a local MP4 file using OpenCV VideoCapture.
/// OpenCV handles all codec/demux complexity via the system FFmpeg backend.
/// </summary>
public sealed class FileVideoSource : IVideoSource
{
    private readonly VideoCapture _capture;

    /// <inheritdoc/>
    public long  DurationMs { get; }

    /// <inheritdoc/>
    public int   Width      { get; }

    /// <inheritdoc/>
    public int   Height     { get; }

    /// <inheritdoc/>
    public float FrameRate  { get; }

    public FileVideoSource(string localFilePath)
    {
        ArgumentNullException.ThrowIfNull(localFilePath);

        _capture = new VideoCapture(localFilePath);

        if (!_capture.IsOpened())
            throw new InvalidOperationException($"Cannot open video: {localFilePath}");

        double fps = _capture.Get(VideoCaptureProperties.Fps);
        if (fps <= 0) fps = 25.0;

        FrameRate = (float)fps;
        Width     = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
        Height    = (int)_capture.Get(VideoCaptureProperties.FrameHeight);

        long frameCount = (long)_capture.Get(VideoCaptureProperties.FrameCount);
        DurationMs = fps > 0 ? (long)(frameCount * 1000.0 / fps) : 0;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<VideoFrame> GetFramesAsync(
        int intervalMs = 500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        double fps = FrameRate;
        int frameInterval = Math.Max(1, (int)(fps * intervalMs / 1000.0));
        int frameIndex    = 0;
        var frame         = new Mat();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_capture.Read(frame) || frame.Empty()) break;

                if (frameIndex % frameInterval == 0)
                {
                    long timestampMs = (long)_capture.Get(VideoCaptureProperties.PosMsec);
                    yield return new VideoFrame(frame.Clone(), timestampMs);
                }

                frameIndex++;
                await Task.Yield();
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    public void Dispose() => _capture.Dispose();
}
