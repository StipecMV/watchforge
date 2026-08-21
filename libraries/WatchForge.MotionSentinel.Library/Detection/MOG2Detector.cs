using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// S22o: Motion detection pomocou MOG2 background subtraction (OpenCV
/// <see cref="BackgroundSubtractorMOG2"/>) — rýchlejšia alternatíva k Farneback
/// optical flow (~15–40× na motion kroku). Učí si model pozadia (history framov)
/// a vráti regióny, kde sa aktuálny frame líši od pozadia.
///
/// Prednosti: výrazne rýchlejší ako Farneback, robustnejší na tiene (detectShadows)
/// a stacionárne objekty (ktoré sa stanú súčasťou pozadia). Pozor: vyžaduje warmup
/// (prvých ~20–50 framov na naučenie pozadia — prvé framy vracajú prázdny zoznam,
/// rovnako ako Farneback na prvej snímke).
///
/// Keep-interface: implementuje <see cref="IMotionDetector"/>, regióny sú
/// normalizované 0..1 — vymeníteľné za <see cref="OpticalFlowDetector"/>.
/// Poznámka: init property sa nastavia až po konštruktore (object initializer),
/// preto subtractor inicializujeme LAZY pri prvom DetectAsync / Reset.
/// </summary>
public sealed class MOG2Detector : IMotionDetector, IDisposable
{
    private BackgroundSubtractorMOG2? _subtractor;
    private int _warmupFrames;

    /// <summary>MOG2 história (počet framov pozadia). Default: 500.</summary>
    public int History { get; init; } = 500;

    /// <summary>MOG2 variance threshold — vyššia = menej citlivý. Default: 16.</summary>
    public double VarThreshold { get; init; } = 16.0;

    /// <summary>Detegovať tiene (odfiltrujú sa ako pozadie). Default: true.</summary>
    public bool DetectShadows { get; init; } = true;

    /// <summary>Minimálna plocha kontúry (px) pre región — filter šumu. Default: 100.</summary>
    public double MinContourArea { get; init; } = 100.0;

    private BackgroundSubtractorMOG2 Subtract
    {
        get => _subtractor ??= BackgroundSubtractorMOG2.Create(
            history: (int)History,
            varThreshold: VarThreshold,
            detectShadows: DetectShadows);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MotionRegion>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<MotionRegion>>(ct);

        using var fgMask = new Mat();
        // learningRate: -1 = auto. Pri 1 fps medzi framami jemne aktualizuje model
        // pozadia, aby stacionárne objekty prešli do pozadia.
        Subtract.Apply(currentMat, fgMask, learningRate: -1);

        // Detegujeme len "hot" pixely (255); pri DetectShadows sú tiene 127 (šedá)
        // a predné objekty 255 — tiene odfiltrujeme prahom 200.
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
            // Intenzita = pokrytie framu pohybom (0..1) — jednoduchá metrika pre MOG2
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
        // Znovu-vytvoríme model — pozadie sa naučí odznova (clear nestačí plne).
        _subtractor?.Dispose();
        _subtractor = null;
        _warmupFrames = 0;
    }

    /// <summary>Uvoľní natívny OpenCV BackgroundSubtractor.</summary>
    public void Dispose()
    {
        _subtractor?.Dispose();
        _subtractor = null;
    }
}
