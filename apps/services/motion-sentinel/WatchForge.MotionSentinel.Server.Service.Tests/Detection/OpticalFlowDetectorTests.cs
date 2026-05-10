using OpenCvSharp;
using WatchForge.MotionSentinel.Server.Service.Detection;

namespace WatchForge.MotionSentinel.Server.Service.Tests.Detection;

public sealed class OpticalFlowDetectorTests
{
    [Test]
    public async Task DetectAsync_ReturnsEmptyList_OnFirstFrame()
    {
        var detector = new OpticalFlowDetector();
        using var mat = new Mat(4, 4, MatType.CV_8UC3);
        var regions = await detector.DetectAsync(new VideoFrame(mat, 0));
        await Assert.That(regions.Count).IsEqualTo(0);
    }

    [Test]
    public void Reset_DoesNotThrow_WhenNoPreviousFrameExists()
    {
        // Given — no frames have been processed yet
        var detector = new OpticalFlowDetector();

        // When / Then — should not throw even with null internal state
        detector.Reset();
        detector.Reset();   // safe to call multiple times
    }

    [Test]
    public async Task Reset_AllowsFirstFrameBehaviourAgain_AfterTwoFrames()
    {
        var detector = new OpticalFlowDetector();

        using var mat1 = new Mat(4, 4, MatType.CV_8UC3);
        using var mat2 = new Mat(4, 4, MatType.CV_8UC3);

        await detector.DetectAsync(new VideoFrame(mat1, 0));
        await detector.DetectAsync(new VideoFrame(mat2, 500));

        detector.Reset();

        using var mat3  = new Mat(4, 4, MatType.CV_8UC3);
        var regions = await detector.DetectAsync(new VideoFrame(mat3, 1000));
        await Assert.That(regions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultThresholds_MatchExpectedValues()
    {
        var detector = new OpticalFlowDetector();

        await Assert.That(detector.IntensityThreshold).IsEqualTo(0.05f);
        await Assert.That(detector.MinContourArea).IsEqualTo(100.0);
    }

    [Test]
    public void Dispose_DoesNotThrow_WhenNoPreviousFrameExists()
    {
        var detector = new OpticalFlowDetector();
        detector.Dispose(); // No frames processed — _previousGray is null
        detector.Dispose(); // Safe to call multiple times
    }

    [Test]
    public async Task Dispose_DoesNotThrow_AfterFrameProcessed()
    {
        var detector = new OpticalFlowDetector();
        using var mat = new Mat(4, 4, MatType.CV_8UC3);
        await detector.DetectAsync(new VideoFrame(mat, 0)); // sets _previousGray

        detector.Dispose(); // Should release _previousGray without throwing
        detector.Dispose(); // Safe to call again (idempotent)
    }
}
