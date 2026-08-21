using System.Runtime.CompilerServices;
using OpenCvSharp;

namespace WatchForge.MotionSentinel.Library.VideoSources;

/// <summary>
/// Extracts frames from a local MP4 file using OpenCV VideoCapture.
/// OpenCV handles all codec/demux complexity via the system FFmpeg backend.
/// </summary>
public sealed class FileVideoSource : IVideoSource
{
    private readonly VideoCapture _capture;

    /// <inheritdoc/>
    public int   Width      { get; }

    /// <inheritdoc/>
    public int   Height     { get; }

    /// <inheritdoc/>
    public float FrameRate  { get; }

    /// <summary>
    /// Maximálna šírka analyzovaných framov (downscale pre CPU). Ak je frame širší,
    /// zmenší sa so zachovaním pomeru strán. Null = pôvodné rozlíšenie videa.
    /// </summary>
    public int? MaxWidth { get; }

    public FileVideoSource(string localFilePath, int? maxWidth = null)
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
        MaxWidth  = maxWidth;

        if (MaxWidth is int mw && Width > mw)
        {
            Height = (int)Math.Round(Height * (double)mw / Width);
            Width  = mw;
        }
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
                    // DownscaleIfNeeded vždy vráti NOVÝ Mat (frame sa ďalej číta — nesmie byť dispose-ovaný)
                    yield return new VideoFrame(DownscaleIfNeeded(frame), timestampMs);
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

    /// <summary>
    /// S10-3: vyextrahuje ORIGINÁLNY fram (bez downscale) z videa na danom
    /// timestamp-e (seek). Vracia null, ak je timestamp mimo rozsahu videa.
    /// Používa sa na 4K face crop — crop z originálu, nie z 1080p downscalu.
    /// </summary>
    public Mat? ExtractFrameAt(long timestampMs)
    {
        var ok = _capture.Set(VideoCaptureProperties.PosMsec, timestampMs);
        if (!ok) return null;

        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty())
            return null;

        return frame.Clone(); // volajúci vlastní Mat
    }

    private Mat DownscaleIfNeeded(Mat frame)
    {
        // Vždy vráti samostatný Mat — volajúci ho vlastní (VideoFrame.Dispose).
        if (MaxWidth is not int maxWidth || frame.Width <= maxWidth)
            return frame.Clone();

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new Size(maxWidth, (int)Math.Round(frame.Height * (double)maxWidth / frame.Width)));
        return resized.Clone();
    }
}
