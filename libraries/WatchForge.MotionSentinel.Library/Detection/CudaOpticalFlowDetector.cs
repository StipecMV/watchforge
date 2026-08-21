using OpenCvSharp;
using OpenCvSharp.Cuda;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Detection;

/// <summary>
/// Dense optical flow (TV-L1) na GPU cez CUDA (S8-1) s automatickým
/// fallbackom na CPU (<see cref="OpticalFlowDetector"/>), keď CUDA nie je
/// dostupná (žiadny NVIDIA GPU / driver / runtime).
/// Post-processing (threshold + kontúry) beží na CPU — je lacný oproti
/// samotnému optical flow; regióny majú rovnaké normalizované súradnice.
/// </summary>
public sealed class CudaOpticalFlowDetector : IMotionDetector, IDisposable
{
    private readonly OpticalFlowDetector _cpuFallback;
    private readonly DenseOpticalFlow? _gpuFlow;
    private Mat? _previousGrayCpu;
    private readonly bool _useGpu;

    /// <summary>Minimálna intenzita optického toku (0–1) pre pohyb (rovnaká ako CPU).</summary>
    public float IntensityThreshold { get; init; } = 0.05f;

    /// <summary>Minimálna plocha kontúry (px) — šum pod ňou sa ignoruje.</summary>
    public double MinContourArea { get; init; } = 100.0;

    public CudaOpticalFlowDetector(DetectionOptions? options = null)
    {
        IntensityThreshold = options?.IntensityThreshold ?? 0.05f;
        MinContourArea = options?.MinContourArea ?? 100.0;

        _cpuFallback = new OpticalFlowDetector
        {
            IntensityThreshold = IntensityThreshold,
            MinContourArea = MinContourArea,
        };

        _useGpu = IsCudaAvailable();
        if (_useGpu)
        {
            _gpuFlow = OpticalFlowDual_TVL1.Create();
        }
    }

    /// <summary>True, ak systém má CUDA-enabled GPU a runtime (OpenCvSharp CUDA).</summary>
    public static bool IsCudaAvailable()
    {
        try
        {
            return Cv2Cuda.GetCudaEnabledDeviceCount() > 0;
        }
        catch (Exception ex) when (ex is OpenCVException or DllNotFoundException
            or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            // Bez CUDA native runtime (OpenCvSharpExtern bez CUDA entry pointov) → CPU fallback
            return false;
        }
    }

    /// <summary>GPU sa používa? (false = fallback CPU).</summary>
    public bool UsingGpu => _useGpu;

    public Task<IReadOnlyList<MotionRegion>> DetectAsync(VideoFrame currentFrame, CancellationToken ct = default)
    {
        if (!_useGpu)
            return _cpuFallback.DetectAsync(currentFrame, ct);

        var currentMat = (Mat)currentFrame.NativeBuffer;

        using var currentGray = new Mat();
        Cv2.CvtColor(currentMat, currentGray, ColorConversionCodes.BGR2GRAY);

        if (_previousGrayCpu == null)
        {
            _previousGrayCpu = currentGray.Clone();
            return Task.FromResult<IReadOnlyList<MotionRegion>>([]);
        }

        // ── TV-L1 optical flow na GPU ──
        var prevGpu = new GpuMat();
        var nextGpu = new GpuMat();
        var flowGpu = new GpuMat();
        try
        {
            prevGpu.Upload(_previousGrayCpu);
            nextGpu.Upload(currentGray);
            _gpuFlow!.Calc(prevGpu, nextGpu, flowGpu);

            using var flow = new Mat();
            flowGpu.Download(flow);

            // ── Post-processing na CPU ──
            Mat[] flowParts = Cv2.Split(flow);
            using var magnitude = new Mat();
            using var angle = new Mat();
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
                type: ThresholdTypes.Binary);
            thresholded.ConvertTo(thresholded, MatType.CV_8UC1);

            var regions = FindMotionRegions(thresholded, magnitude, currentMat.Width, currentMat.Height);

            _previousGrayCpu.Dispose();
            _previousGrayCpu = currentGray.Clone();

            return Task.FromResult<IReadOnlyList<MotionRegion>>(regions);
        }
        finally
        {
            prevGpu.Release();
            nextGpu.Release();
            flowGpu.Release();
        }
    }

    private List<MotionRegion> FindMotionRegions(Mat mask, Mat magnitude, int width, int height)
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
            using var roiMag = new Mat(magnitude, rect);
            double meanMag = Cv2.Mean(roiMag, roiMask).Val0;
            float intensity = Math.Clamp((float)(meanMag / 20.0), 0f, 1f);

            regions.Add(new MotionRegion
            {
                X = (float)rect.X / width,
                Y = (float)rect.Y / height,
                Width = (float)rect.Width / width,
                Height = (float)rect.Height / height,
                Intensity = intensity,
            });
        }
        return regions;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _previousGrayCpu?.Dispose();
        _previousGrayCpu = null;
        _cpuFallback.Reset();
    }

    public void Dispose()
    {
        _previousGrayCpu?.Dispose();
        _gpuFlow?.Dispose();
        _cpuFallback.Dispose();
    }
}
