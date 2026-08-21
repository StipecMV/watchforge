using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

/// <summary>
/// S22o: MOG2 background subtraction motion detektor — alternatíva k Farneback
/// (optical flow). Cieľ: ~15–40× rýchlejší motion krok, presnejší na tienoch/
/// stacionárnych objektoch. Testuje správanie cez IMotionDetector rozhranie.
/// </summary>
public sealed class MOG2DetectorTests
{
    [Test]
    public async Task FirstFrames_ReturnEmpty_UntilBackgroundLearned()
    {
        using var detector = new MOG2Detector();
        // Warmup: prvých pár framov buduje pozadie → prázdny zoznam (ako prvá snímka Farneback)
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
        using var detector = new MOG2Detector();
        // Warmup
        for (int i = 0; i < 5; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        // Statické pokračovanie → žiadny pohyb (pozadie sa naučilo)
        for (int i = 0; i < 3; i++)
        {
            var regions = await detector.DetectAsync(StaticFrame(), CancellationToken.None);
            await Assert.That(regions).IsEmpty();
        }
    }

    [Test]
    public async Task MovingObject_AfterWarmup_ReturnsRegion()
    {
        using var detector = new MOG2Detector();
        // Warmup na statickom pozadí
        for (int i = 0; i < 5; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        // Objekt presunieme → MOG2 deteguje pohyb
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
        using var detector = new MOG2Detector();
        for (int i = 0; i < 5; i++)
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
        using var detector = new MOG2Detector();
        for (int i = 0; i < 5; i++)
            await detector.DetectAsync(StaticFrame(), CancellationToken.None);

        detector.Reset();

        // Po resete sa pozadie nanovo učí → prázdny zoznam
        using var frame = StaticFrame();
        var regions = await detector.DetectAsync(frame, CancellationToken.None);
        await Assert.That(regions).IsEmpty();
    }

    // ── pomocné framy ────────────────────────────────────────────────────────

    /// <summary>Statický čierny frame (žiadny pohyb).</summary>
    private static VideoFrame StaticFrame()
        => new(new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30)), 0);

    /// <summary>Frame s bielym obdĺžnikom, ktorý sa posúva doprava (vyvolá pohyb).</summary>
    private static VideoFrame FrameWithMovingObject(int step)
    {
        var mat = new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(30));
        Cv2.Rectangle(mat, new Rect(50 + step * 25, 90, 40, 60), Scalar.All(220), -1);
        return new VideoFrame(mat, step * 1000L);
    }
}
