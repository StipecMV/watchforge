using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Services;

namespace WatchForge.MotionSentinel.Library.Tests.Detection;

public sealed class MotionDetectorFactoryTests
{
    [Test]
    public async Task Create_Default_ReturnsOpticalFlowDetector()
    {
        var d = MotionDetectorFactory.Create(new DetectionOptions());
        using (d)
        {
            await Assert.That(d?.GetType().Name).IsEqualTo(nameof(OpticalFlowDetector));
        }
    }

    [Test]
    public async Task Create_Farneback_ReturnsOpticalFlowDetector()
    {
        var d = MotionDetectorFactory.Create(new DetectionOptions { Algorithm = MotionAlgorithm.Farneback });
        using (d)
        {
            await Assert.That(d.GetType().Name).IsEqualTo(nameof(OpticalFlowDetector));
        }
    }

    [Test]
    public async Task Create_Mog2_ReturnsMOG2Detector()
    {
        var d = MotionDetectorFactory.Create(new DetectionOptions { Algorithm = MotionAlgorithm.Mog2 });
        using (d)
        {
            await Assert.That(d.GetType().Name).IsEqualTo(nameof(MOG2Detector));
        }
    }

    [Test]
    public async Task Create_Knn_ReturnsKNNDetector()
    {
        var d = MotionDetectorFactory.Create(new DetectionOptions { Algorithm = MotionAlgorithm.Knn });
        using (d)
        {
            await Assert.That(d.GetType().Name).IsEqualTo(nameof(KNNDetector));
        }
    }

    [Test]
    public async Task Create_AbsDiff_ReturnsAbsDiffDetector()
    {
        var d = MotionDetectorFactory.Create(new DetectionOptions { Algorithm = MotionAlgorithm.AbsDiff });
        using (d)
        {
            await Assert.That(d.GetType().Name).IsEqualTo(nameof(AbsDiffDetector));
        }
    }

    [Test]
    public async Task Create_AlwaysReturnsDisposable_ThatImplementsIMotionDetector()
    {
        foreach (var alg in new[] { MotionAlgorithm.Farneback, MotionAlgorithm.Mog2, MotionAlgorithm.Knn, MotionAlgorithm.AbsDiff })
        {
            var d = MotionDetectorFactory.Create(new DetectionOptions { Algorithm = alg });
            using (d)
            {
                await Assert.That(d).IsAssignableTo<IMotionDetector>();
                await Assert.That(d).IsAssignableTo<IDisposable>();
            }
        }
    }
}
