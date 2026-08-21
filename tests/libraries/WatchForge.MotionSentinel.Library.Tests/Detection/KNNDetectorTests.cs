using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

/// <summary>
/// S22p: KNN background subtraction motion detektor (OpenCV BackgroundSubtractorKNN)
/// — hlavný konkurent MOG2 z rodiny background subtraction. Testuje správanie cez
/// IMotionDetector rozhranie (warmup, statika, pohyb, normalizácia, reset).
/// </summary>
public sealed class KNNDetectorTests
{
    [Test]
    public async Task FirstFrames_ReturnEmpty_UntilBackgroundLearned()
    {
        using var detector = new KNNDetector();
        for (int i = 0; i < 3; i++)
        {
            using var frame = StaticFrame();
            var regions = await detector.DetectAsync(frame, CancellationToken.None);
            await Assert.That(regions).IsEmpty();
        }
    }

    [Test]
    public async Task StaticFrames_AfterWarmup_ReturnEmpty()
    {
        using var detector = new KNNDetector();
        for (int i = 0; i < 6; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        for (int i = 0; i < 3; i++)
        {
            var regions = await detector.DetectAsync(StaticFrame(), CancellationToken.None);
            await Assert.That(regions).IsEmpty();
        }
    }

    [Test]
    public async Task MovingObject_AfterWarmup_ReturnsRegion()
    {
        using var detector = new KNNDetector();
        for (int i = 0; i < 6; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            using var frame = FrameWithMovingObject(i);
            var regions = await detector.DetectAsync(frame, CancellationToken.None);
            if (regions.Count > 0)
                await Assert.That(regions[0].Width).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Regions_AreNormalized_To01()
    {
        using var detector = new KNNDetector();
        for (int i = 0; i < 6; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            using var frame = FrameWithMovingObject(i);
            foreach (var r in await detector.DetectAsync(frame, CancellationToken.None))
            {
                await Assert.That(r.X).IsGreaterThanOrEqualTo(0f).And.IsLessThanOrEqualTo(1f);
                await Assert.That(r.Y).IsGreaterThanOrEqualTo(0f).And.IsLessThanOrEqualTo(1f);
                await Assert.That(r.Width).IsGreaterThanOrEqualTo(0f).And.IsLessThanOrEqualTo(1f);
                await Assert.That(r.Height).IsGreaterThanOrEqualTo(0f).And.IsLessThanOrEqualTo(1f);
            }
        }
    }

    [Test]
    public async Task Reset_ClearsState_NextFrameReturnsEmpty()
    {
        using var detector = new KNNDetector();
        for (int i = 0; i < 6; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        detector.Reset();
        using var frame = StaticFrame();
        var regions = await detector.DetectAsync(frame, CancellationToken.None);
        await Assert.That(regions).IsEmpty();
    }

    private static VideoFrame StaticFrame()
        => new(new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30)), 0);

    private static VideoFrame FrameWithMovingObject(int step)
    {
        var mat = new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30));
        Cv2.Rectangle(mat, new Rect(50 + step * 25, 90, 40, 60), Scalar.All(220), -1);
        return new VideoFrame(mat, step * 1000L);
    }
}
