using OpenCvSharp;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Person detection klasickou CV technikou (FR-04): HOG descriptor +
/// lineárny SVM (OpenCV default people detector). Nie je to ML model —
/// žiadna neurónová sieť, len hand-crafted features + pretrained SVM.
/// Bounding boxy sú normalizované 0..1 (rovnaký priestor ako MotionRegion),
/// takže platia priamo pre 4K fram (analýza beží na 1080p downscale).
/// </summary>
public sealed class HogPersonDetector : IObjectDetector, IDisposable
{
    private readonly HOGDescriptor _hog;

    /// <summary>Minimálna confidence (HOG SVM skóre preškálované na 0..1) pre prijatie detekcie.</summary>
    public float MinConfidence { get; init; } = 0.3f;

    /// <summary>Scale factor medzi pyramídovými úrovňami (HOG detectMultiScale).</summary>
    public double Scale { get; init; } = 1.05;

    /// <summary>Group threshold pre NMS (detectMultiScale).</summary>
    public double GroupThreshold { get; init; } = 2.0;

    /// <summary>WinStride pre detectMultiScale — menší = hustejšie skenovanie, pomalšie.</summary>
    public Size WinStride { get; init; } = new(8, 8);

    /// <summary>Maximálny počet vrátených detekcií (ochrana proti šumu).</summary>
    public int MaxDetections { get; init; } = 8;

    public HogPersonDetector()
    {
        _hog = new HOGDescriptor();
        _hog.SetSVMDetector(HOGDescriptor.GetDefaultPeopleDetector());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        VideoFrame currentFrame,
        CancellationToken ct = default)
    {
        var currentMat = (Mat)currentFrame.NativeBuffer;

        // OpenCvSharp 4.13: DetectMultiScale vracia Rect[] + out double[] weights.
        // Padding musí byť (0,0) — „mock parameter to keep the CPU interface compatibility".
        // Pozn.: na niektorých framoch HOG interná afinná transformácia zlyhá
        // („(M0.type() == CV_32F || CV_64F) && M0.rows == 2 && M0.cols == 3") —
        // person detekcia je doplnková (motion je hlavná), takže chybu prehltneme
        // a vrátime prázdny zoznam (fallback aj s groupThreshold=0, ktorý obchádza
        // interné groupRectangles).
        Rect[] found;
        double[] weights;
        try
        {
            found = _hog.DetectMultiScale(
                currentMat,
                out weights,
                hitThreshold: 0,
                winStride: WinStride,
                padding: new Size(0, 0),
                scale: Scale,
                groupThreshold: (int)GroupThreshold);
        }
        catch (OpenCVException)
        {
            try
            {
                found = _hog.DetectMultiScale(
                    currentMat,
                    out weights,
                    hitThreshold: 0,
                    winStride: WinStride,
                    padding: new Size(0, 0),
                    scale: Scale,
                    groupThreshold: 0);
            }
            catch (OpenCVException)
            {
                return Task.FromResult<IReadOnlyList<ObjectDetection>>([]);
            }
        }

        var results = new List<ObjectDetection>(Math.Min(found.Length, MaxDetections));

        for (int i = 0; i < found.Length && results.Count < MaxDetections; i++)
        {
            // HOG SVM skóre je typicky -1..~2 → clamp na 0..1
            float confidence = Math.Clamp((float)((weights[i] + 1.0) / 3.0), 0f, 1f);
            if (confidence < MinConfidence) continue;

            var rect = found[i];
            results.Add(new ObjectDetection
            {
                Region = new NormalizedRegion(
                    (float)rect.X / currentMat.Width,
                    (float)rect.Y / currentMat.Height,
                    (float)rect.Width / currentMat.Width,
                    (float)rect.Height / currentMat.Height),
                ObjectClass = "person",
                Confidence  = confidence,
            });
        }

        return Task.FromResult<IReadOnlyList<ObjectDetection>>(results);
    }

    /// <summary>Uvoľní HOG descriptor (native pamäť).</summary>
    public void Dispose() => _hog.Dispose();
}
