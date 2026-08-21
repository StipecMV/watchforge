using OpenCvSharp;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

/// <summary>
/// S8-2: HogPersonDetector — person/object detection klasickou CV (HOG+SVM).
/// Testy overujú deterministické správanie: prázdny fram → 0 detekcií,
/// normalizované koordináty (0..1), parametre z options.
/// (Reálna detekcia osoby vyžaduje fotku človeka — overené na reálnych dátach
/// v rámci E2E, tu len deterministické správanie.)
/// </summary>
public class HogPersonDetectorTests
{
    private static Mat BlankFrame(int width = 320, int height = 240)
        => new(width, height, MatType.CV_8UC3, Scalar.Black);

    [Test]
    public async Task BlankFrame_ReturnsNoDetections()
    {
        using var detector = new HogPersonDetector();
        using var mat = BlankFrame();

        var detections = await detector.DetectAsync(new VideoFrame(mat, 0));

        await Assert.That(detections).IsEmpty();
    }

    [Test]
    public async Task NoiseFrame_DoesNotThrow_AndRegionsAreNormalized()
    {
        using var detector = new HogPersonDetector();
        using var mat = new Mat(320, 240, MatType.CV_8UC3);
        Cv2.Randu(mat, new Scalar(0, 0, 0), new Scalar(255, 255, 255));

        // When — nesmie spadnúť na šume; ak vráti detekcie, regióny musia byť 0..1
        var detections = await detector.DetectAsync(new VideoFrame(mat, 0));

        foreach (var d in detections)
        {
            await Assert.That(d.Region.X).IsGreaterThanOrEqualTo(0f);
            await Assert.That(d.Region.Y).IsGreaterThanOrEqualTo(0f);
            await Assert.That(d.Region.X + d.Region.W).IsLessThanOrEqualTo(1f);
            await Assert.That(d.Region.Y + d.Region.H).IsLessThanOrEqualTo(1f);
            await Assert.That(d.Confidence).IsGreaterThanOrEqualTo(0f);
            await Assert.That(d.Confidence).IsLessThanOrEqualTo(1f);
            await Assert.That(d.ObjectClass).IsEqualTo("person");
        }
    }

    [Test]
    public async Task HighMinConfidence_FiltersOutDetections()
    {
        using var detector = new HogPersonDetector { MinConfidence = 1.0f };
        using var mat = BlankFrame();

        // MinConfidence 1.0 → žiadna detekcia neprejde (confidence je vždy < 1)
        var detections = await detector.DetectAsync(new VideoFrame(mat, 0));

        await Assert.That(detections).IsEmpty();
    }

    [Test]
    public async Task Defaults_AreSane()
    {
        var detector = new HogPersonDetector();
        await Assert.That(detector.MinConfidence).IsEqualTo(0.3f);
        await Assert.That(detector.MaxDetections).IsEqualTo(8);
        await Assert.That(detector.Scale).IsEqualTo(1.05);
        detector.Dispose();
    }

    [Test]
    public async Task OddSizedFrame_DoesNotThrow_AndReturnsNormalizedRegions()
    {
        // Edge case: fram s rozmermi, na ktorých HOG interná afinná transformácia
        // v OpenCvSharp 4.13 zlyháva („M0.rows == 2 && M0.cols == 3") — detektor
        // musí chybu prehltnúť a vrátiť prázdny zoznam, nie spadnúť.
        using var detector = new HogPersonDetector();
        using var mat = new Mat(647, 1133, MatType.CV_8UC3, Scalar.Black);

        var detections = await detector.DetectAsync(new VideoFrame(mat, 0));

        foreach (var d in detections)
        {
            await Assert.That(d.Region.X + d.Region.W).IsLessThanOrEqualTo(1f);
            await Assert.That(d.Region.Y + d.Region.H).IsLessThanOrEqualTo(1f);
        }
    }
}
