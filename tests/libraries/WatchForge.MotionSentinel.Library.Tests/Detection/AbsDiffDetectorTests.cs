using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

public sealed class AbsDiffDetectorTests
{
    [Test]
    public async Task FirstFrame_ReturnsEmpty()
    {
        using var d = new AbsDiffDetector();
        using var f = StaticFrame();
        var regions = await d.DetectAsync(f, CancellationToken.None);
        await Assert.That(regions).IsEmpty();
    }

    [Test]
    public async Task StaticFrames_ReturnEmpty()
    {
        using var d = new AbsDiffDetector();
        await d.DetectAsync(StaticFrame(), CancellationToken.None);
        for (int i = 0; i < 3; i++)
        {
            var regions = await d.DetectAsync(StaticFrame(), CancellationToken.None);
            await Assert.That(regions).IsEmpty();
        }
    }

    [Test]
    public async Task MovingObject_ReturnsRegion()
    {
        using var d = new AbsDiffDetector();
        await d.DetectAsync(StaticFrame(), CancellationToken.None);

        int found = 0;
        for (int i = 0; i < 5; i++)
        {
            using var f = FrameWithMovingObject(i);
            var regions = await d.DetectAsync(f, CancellationToken.None);
            if (regions.Count > 0) found++;
        }
        await Assert.That(found).IsGreaterThan(0);
    }

    [Test]
    public async Task Regions_AreNormalized_To01()
    {
        using var d = new AbsDiffDetector();
        await d.DetectAsync(StaticFrame(), CancellationToken.None);
        for (int i = 0; i < 3; i++)
        {
            using var f = FrameWithMovingObject(i);
            foreach (var r in await d.DetectAsync(f, CancellationToken.None))
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
        using var d = new AbsDiffDetector();
        await d.DetectAsync(StaticFrame(), CancellationToken.None);
        await d.DetectAsync(FrameWithMovingObject(1), CancellationToken.None);
        d.Reset();
        using var f = StaticFrame();
        var regions = await d.DetectAsync(f, CancellationToken.None);
        await Assert.That(regions).IsEmpty();
    }

    private static VideoFrame StaticFrame()
        => new(new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30)), 0);

    private static VideoFrame FrameWithMovingObject(int step)
    {
        var mat = new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30));
        Cv2.Rectangle(mat, new Rect(50 + step * 40, 90, 40, 60), Scalar.All(220), -1);
        return new VideoFrame(mat, step * 1000L);
    }
}
