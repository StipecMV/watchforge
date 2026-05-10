namespace WatchForge.MotionSentinel.Server.Service.Detection;

/// <summary>
/// Dense optical flow using Farneback algorithm.
/// Maintains previous-frame reference — call Reset() between videos.
/// </summary>
public sealed class OpticalFlowDetector : IMotionDetector, IDisposable
{
    private Mat? _previousGray;

    /// <summary>
    /// Minimum optical flow intensity (0–1) for a pixel to be counted as motion.
    /// Sourced from <see cref="DetectionOptions.IntensityThreshold"/>. Default: 0.05.
    /// </summary>
    public float IntensityThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Minimum contour area in pixels below which a candidate region is treated as noise.
    /// Sourced from <see cref="DetectionOptions.MinContourArea"/>. Default: 100.
    /// </summary>
    public double MinContourArea { get; init; } = 100.0;

    /// <inheritdoc/>
    public Task<IReadOnlyList<MotionRegion>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;

        using var currentGray = new Mat();
        Cv2.CvtColor(currentMat, currentGray, ColorConversionCodes.BGR2GRAY);

        if (_previousGray == null)
        {
            _previousGray = currentGray.Clone();
            return Task.FromResult<IReadOnlyList<MotionRegion>>([]);
        }

        using var flow = new Mat();
        Cv2.CalcOpticalFlowFarneback(
            prev:       _previousGray,
            next:       currentGray,
            flow:       flow,
            pyrScale:   0.5,
            levels:     3,
            winsize:    15,
            iterations: 3,
            polyN:      5,
            polySigma:  1.2,
            flags:      0);

        Mat[] flowParts = Cv2.Split(flow);
        using var magnitude = new Mat();
        using var angle     = new Mat();
        try
        {
            Cv2.CartToPolar(flowParts[0], flowParts[1], magnitude, angle);
        }
        finally
        {
            foreach (var part in flowParts) part.Dispose();
        }

        using var thresholded = new Mat();
        Cv2.Threshold(magnitude, thresholded,
            thresh: IntensityThreshold * 10, maxval: 255,
            type:   ThresholdTypes.Binary);
        thresholded.ConvertTo(thresholded, MatType.CV_8UC1);

        var regions = FindMotionRegions(
            thresholded, magnitude, currentMat.Width, currentMat.Height);

        _previousGray.Dispose();
        _previousGray = currentGray.Clone();

        return Task.FromResult<IReadOnlyList<MotionRegion>>(regions);
    }

    private List<MotionRegion> FindMotionRegions(
        Mat mask, Mat magnitude, int width, int height)
    {
        Cv2.FindContours(mask, out Point[][] contours, out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var regions = new List<MotionRegion>();

        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < MinContourArea) continue;

            Rect rect = Cv2.BoundingRect(contour);

            using var roiMask = new Mat(mask, rect);
            using var roiMag  = new Mat(magnitude, rect);

            double meanMag = Cv2.Mean(roiMag, roiMask).Val0;
            float intensity = Math.Clamp((float)(meanMag / 20.0), 0f, 1f);

            regions.Add(new MotionRegion
            {
                X         = (float)rect.X      / width,
                Y         = (float)rect.Y      / height,
                Width     = (float)rect.Width  / width,
                Height    = (float)rect.Height / height,
                Intensity = intensity,
            });
        }

        return regions;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _previousGray?.Dispose();
        _previousGray = null;
    }

    /// <summary>Releases the native OpenCV Mat held between frames.</summary>
    public void Dispose()
    {
        _previousGray?.Dispose();
        _previousGray = null;
    }
}
