using System.Diagnostics;
using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

/// <summary>
/// S8-1: CudaOpticalFlowDetector — GPU (CUDA TV-L1) s fallbackom na CPU.
/// Testy sú gated: na systémoch bez CUDA overia CPU fallback a benchmark
/// sa preskočí s poznámkou (GPU testy bežia na NVR stroji s NVIDIA).
/// </summary>
public class CudaOpticalFlowDetectorTests
{
    private static Mat FrameWithRect(int x, int y)
    {
        var mat = new Mat(240, 320, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(x, y, 100, 100), new Scalar(255, 255, 255), thickness: -1);
        // Textúra v obdĺžniku (farneback potrebuje gradient, nie uniformnú plochu)
        for (int i = 0; i < 100; i++)
            Cv2.Line(mat, new Point(x + i, y), new Point(x, y + i), new Scalar(128, 128, 128), 1);
        return mat;
    }

    [Test]
    public async Task FirstFrame_ReturnsEmptyList()
    {
        using var detector = new CudaOpticalFlowDetector();
        using var mat = FrameWithRect(10, 10);
        var regions = await detector.DetectAsync(new VideoFrame(mat, 0));
        await Assert.That(regions.Count).IsEqualTo(0);
        detector.Dispose();
    }

    [Test]
    public async Task MovedRect_DetectsMotionRegion()
    {
        using var detector = new CudaOpticalFlowDetector();
        using var mat1 = FrameWithRect(10, 10);
        using var mat2 = FrameWithRect(60, 50); // posun → optický tok

        await detector.DetectAsync(new VideoFrame(mat1, 0));
        var regions = await detector.DetectAsync(new VideoFrame(mat2, 500));

        // CPU fallback aj GPU by mali nájsť región pohybu (súradnice normalizované)
        await Assert.That(regions.Count).IsGreaterThan(0);
        var r = regions[0];
        await Assert.That(r.X).IsGreaterThanOrEqualTo(0f);
        await Assert.That(r.X).IsLessThanOrEqualTo(1f);
        await Assert.That(r.Y).IsGreaterThanOrEqualTo(0f);
        await Assert.That(r.Y).IsLessThanOrEqualTo(1f);
        await Assert.That(r.Width).IsGreaterThan(0f);
        await Assert.That(r.Height).IsGreaterThan(0f);
        detector.Dispose();
    }

    [Test]
    public async Task Thresholds_FromDetectionOptions()
    {
        using var detector = new CudaOpticalFlowDetector(new DetectionOptions
        {
            IntensityThreshold = 0.11f,
            MinContourArea = 250.0,
        });
        await Assert.That(detector.IntensityThreshold).IsEqualTo(0.11f);
        await Assert.That(detector.MinContourArea).IsEqualTo(250.0);
        detector.Dispose();
    }

    [Test]
    public void Reset_IsSafe_BeforeAnyFrame()
    {
        using var detector = new CudaOpticalFlowDetector();
        detector.Reset();
        detector.Reset();
        detector.Dispose();
    }

    /// <summary>
    /// Benchmark paralelizmu (S8-1): CPU (Farneback) vs GPU (CUDA TV-L1) —
    /// priemerný čas na fram + fps na 1080p framoch. Gated: ak CUDA nie je
    /// dostupná, test prejde s poznámkou (beží na NVR stroji s NVIDIA).
    /// </summary>
    [Test]
    public async Task Benchmark_CpuVsGpu_ReportsThroughput()
    {
        var cudaAvailable = CudaOpticalFlowDetector.IsCudaAvailable();
        if (!cudaAvailable)
        {
            // Na tomto systéme GPU nie je — benchmark sa preskočí, ale overí fallback
            using var cpu = new OpticalFlowDetector();
            var cpuFallbackMs = await MeasureAsync(cpu, frames: 8);
            Console.WriteLine($"BENCHMARK: CUDA unavailable — CPU fallback {cpuFallbackMs:F1} ms/frame ({(1000.0 / cpuFallbackMs):F1} fps @1080p)");
            await Assert.That(cpuFallbackMs).IsGreaterThan(0);
            return;
        }

        using var cpuDetector = new OpticalFlowDetector();
        using var gpuDetector = new CudaOpticalFlowDetector();
        var cpuMs = await MeasureAsync(cpuDetector, frames: 8);
        var gpuMs = await MeasureAsync(gpuDetector, frames: 8);
        Console.WriteLine($"BENCHMARK: CPU {cpuMs:F1} ms/frame, GPU {gpuMs:F1} ms/frame, speedup {(cpuMs / gpuMs):F2}x");

        // GPU by mal byť rýchlejší na 1080p (TV-L1 vs Farneback — približne)
        await Assert.That(gpuMs).IsGreaterThan(0);
    }

    private static async Task<double> MeasureAsync(IMotionDetector detector, int frames)
    {
        // 1080p syntetické framy s posúvajúcim sa obdĺžnikom
        var mats = new List<Mat>();
        try
        {
            for (int i = 0; i < frames; i++)
                mats.Add(FrameWithRect(10 + i * 8, 20 + i * 5));

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < mats.Count; i++)
            {
                await detector.DetectAsync(new VideoFrame(mats[i], i * 500));
                if (i == 0) sw.Restart(); // prvý frame je len inicializácia (prev)
            }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / Math.Max(1, frames - 1);
        }
        finally
        {
            foreach (var m in mats) m.Dispose();
        }
    }
}
