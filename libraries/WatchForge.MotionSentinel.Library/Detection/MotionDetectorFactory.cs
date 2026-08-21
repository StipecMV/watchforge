using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// S22p: Podporované algoritmy detekcie pohybu — prepínateľné cez MotionDetectorFactory.
/// Umožňuje jednoducho zmeniť detektor v AnalyzeJobHandler (hlavný projekt) bezo zmeny
/// volajúceho kódu — stačí zmeniť <see cref="DetectionOptions.Algorithm"/>.
/// Tri rodiny: optical flow (Farneback), background subtraction (MOG2, KNN), frame diff (AbsDiff).
/// Pozn.: DIS nie je v OpenCvSharp 4.13 wrapperovaný; TVL1 (DenseOpticalFlowExt) segfaultuje
/// natively na tomto prostredí — namiesto toho AbsDiff (rýchla druhá rodina).
/// </summary>
public enum MotionAlgorithm
{
    /// <summary>Farneback dense optical flow (pôvodný default).</summary>
    Farneback,

    /// <summary>MOG2 background subtraction (S22o, rýchly, robustný na tiene).</summary>
    Mog2,

    /// <summary>KNN background subtraction (S22p, konkurent MOG2).</summary>
    Knn,

    /// <summary>Abs-diff frame differencing (S22p, najrýchlejšia, iná rodina).</summary>
    AbsDiff,
}

/// <summary>
/// S22p: Factory, ktorá vytvorí <see cref="IMotionDetector"/> podľa <see cref="DetectionOptions.Algorithm"/>.
/// Spája všetky volania detektorov na jedno miesto — v hlavnom UI stačí prepnúť
/// `DetectionOptions.Algorithm` (alebo env/config) a detektor sa zmení.
/// </summary>
public static class MotionDetectorFactory
{
    /// <summary>Vytvorí detector podľa options.Algorithm (default: Farneback). Vracia IDisposable detector.</summary>
    public static IMotionDetector Create(DetectionOptions options)
    {
        var algorithm = options.Algorithm;
        return algorithm switch
        {
            MotionAlgorithm.Mog2 => new MOG2Detector
            {
                History          = options.Mog2History,
                VarThreshold     = options.Mog2VarThreshold,
                DetectShadows    = options.Mog2DetectShadows,
                MinContourArea   = options.MinContourArea,
            },
            MotionAlgorithm.Knn => new KNNDetector
            {
                History          = options.KnnHistory,
                Dist2Threshold   = options.KnnDist2Threshold,
                DetectShadows    = options.KnnDetectShadows,
                MinContourArea   = options.MinContourArea,
            },
            MotionAlgorithm.AbsDiff => new AbsDiffDetector
            {
                DiffThreshold  = options.AbsDiffThreshold,
                MinContourArea = options.MinContourArea,
            },
            _ => new OpticalFlowDetector
            {
                IntensityThreshold = options.IntensityThreshold,
                MinContourArea     = options.MinContourArea,
            },
        };
    }
}
