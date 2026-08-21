using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// S22p: Motion detection pomocou frame differencing (abs-diff) — odčíta aktuálny
/// grayscale frame od predošlého a prahuje absolútny rozdiel. Ide o NAJLACNEJŠIU
/// a najrýchlejšiu rodinu detekcie pohybu (iná ako optical flow a background
/// subtraction). Menej robustná na šum/tiene/svetelné zmeny, ale dobrá ako
/// rýchla referenčná/porovnávacia metóda.
///
/// Keep-interface: implementuje <see cref="IMotionDetector"/>, regióny sú
/// normalizované 0..1 — vymeníteľné za ostatné detektory.
/// </summary>
public sealed class AbsDiffDetector : IMotionDetector, IDisposable
{
    private Mat? _previousGray;

    /// <summary>Prah absolútneho rozdielu (0–255) pre pixel = pohyb. Default: 25.</summary>
    public double DiffThreshold { get; init; } = 25.0;

    /// <summary>Minimálna plocha kontúry (px) — filter šumu. Default: 100.</summary>
    public double MinContourArea { get; init; } = 100.0;

    /// <inheritdoc/>
    public Task<IReadOnlyList<MotionRegion>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<MotionRegion>>(ct);

        using var currentGray = new Mat();
        Cv2.CvtColor(currentMat, currentGray, ColorConversionCodes.BGR2GRAY);

        if (_previousGray == null)
        {
            _previousGray = currentGray.Clone();
            return Task.FromResult<IReadOnlyList<MotionRegion>>([]);
        }

        using var diff = new Mat();
        Cv2.Absdiff(_previousGray, currentGray, diff);

        using var binary = new Mat();
        Cv2.Threshold(diff, binary, DiffThreshold, 255, ThresholdTypes.Binary);
        binary.ConvertTo(binary, MatType.CV_8UC1);

        var regions = FindMotionRegions(binary, currentMat.Width, currentMat.Height);

        _previousGray.Dispose();
        _previousGray = currentGray.Clone();

        return Task.FromResult<IReadOnlyList<MotionRegion>>(regions);
    }

    private List<MotionRegion> FindMotionRegions(Mat mask, int width, int height)
    {
        Cv2.FindContours(mask, out Point[][] contours, out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var regions = new List<MotionRegion>();
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < MinContourArea) continue;

            Rect rect = Cv2.BoundingRect(contour);
            float intensity = Math.Clamp((float)(area / (width * height)), 0f, 1f);

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

    /// <summary>Uvoľní natívne zdroje.</summary>
    public void Dispose() => Reset();
}
