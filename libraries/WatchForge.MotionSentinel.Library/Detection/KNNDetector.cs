using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// S22p: Motion detection pomocou KNN background subtraction (OpenCV
/// <see cref="BackgroundSubtractorKNN"/>) — hlavný konkurent MOG2 z rovnakej
/// rodiny. KNN udržiava per-pixel vzorkovacie vzorky a klasifikuje pixel ako
/// pozadie, ak má K vzoriek v rámci dist2Threshold. Rýchly, robustný na tiene
/// (detectShadows) a stacionárne objekty; vyžaduje warmup (prvé framy vracajú
/// prázdny zoznam).
///
/// Keep-interface: implementuje <see cref="IMotionDetector"/>, regióny sú
/// normalizované 0..1 — vymeníteľné za OpticalFlowDetector / MOG2Detector.
/// </summary>
public sealed class KNNDetector : IMotionDetector, IDisposable
{
    private BackgroundSubtractorKNN? _subtractor;
    private int _warmupFrames;

    /// <summary>KNN história (počet vzorkovacích vzoriek pre pozadie). Default: 500.</summary>
    public int History { get; init; } = 500;

    /// <summary>Prah druhej mocniny vzdialenosti pixel-vzorka (vyššia = menej citlivý). Default: 400.0.</summary>
    public double Dist2Threshold { get; init; } = 400.0;

    /// <summary>Detegovať tiene (odfiltrujú sa ako pozadie). Default: true.</summary>
    public bool DetectShadows { get; init; } = true;

    /// <summary>Minimálna plocha kontúry (px) — filter šumu. Default: 100.</summary>
    public double MinContourArea { get; init; } = 100.0;

    private BackgroundSubtractorKNN Subtract
        => _subtractor ??= BackgroundSubtractorKNN.Create(
            history: (int)History,
            dist2Threshold: Dist2Threshold,
            detectShadows: DetectShadows);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MotionRegion>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<MotionRegion>>(ct);

        using var fgMask = new Mat();
        Subtract.Apply(currentMat, fgMask, learningRate: -1);

        // Hot pixely = 255, tiene (detectShadows) = 127 — odfiltrujeme tiene prahom
        using var binary = new Mat();
        Cv2.Threshold(fgMask, binary, 200, 255, ThresholdTypes.Binary);
        binary.ConvertTo(binary, MatType.CV_8UC1);

        // Warmup: prvých pár Apply len naučí pozadie, výsledok zahodíme (ako Farneback
        // na 1. snímke). Background subtraction potrebuje ~3 framy na prvý model.
        if (_warmupFrames < 3)
        {
            _warmupFrames++;
            return Task.FromResult<IReadOnlyList<MotionRegion>>([]);
        }

        var regions = FindMotionRegions(binary, currentMat.Width, currentMat.Height);
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
        _subtractor?.Dispose();
        _subtractor = null;
        _warmupFrames = 0;
    }

    /// <summary>Uvoľní natívny OpenCV BackgroundSubtractor.</summary>
    public void Dispose() => Reset();
}
