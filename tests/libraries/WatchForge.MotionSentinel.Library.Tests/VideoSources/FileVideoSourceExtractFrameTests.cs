using OpenCvSharp;
using WatchForge.MotionSentinel.Library.VideoSources;

namespace WatchForge.MotionSentinel.Library.Tests.VideoSources;

/// <summary>
/// S10-3: FileVideoSource.ExtractFrameAt — extrakcia ORIGINÁLNEHO framu (bez
/// downscale) na danom timestamp-e pre 4K face crop.
/// </summary>
public class FileVideoSourceExtractFrameTests
{
    [Test]
    public async Task ExtractFrameAt_ReturnsOriginalResolutionFrame()
    {
        // Given video 1280×720 (testovacia "4K" náhrada — 2× šírka ako 640)
        var path = Path.Combine(Path.GetTempPath(), "wf-extract-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await GenerateVideoAsync(path, 1280, 720);

            using var full = new FileVideoSource(path);          // bez maxWidth → originál
            using var downscaled = new FileVideoSource(path, maxWidth: 640);

            // When — extrakcia originálneho framu
            var original = full.ExtractFrameAt(500);

            // Then — originálne rozlíšenie (nie downscale)
            await Assert.That(original).IsNotNull();
            await Assert.That(original!.Width).IsEqualTo(1280);
            await Assert.That(original.Height).IsEqualTo(720);
            await Assert.That(downscaled.Width).IsEqualTo(640); // kontrola: downscale source je menší
            original.Dispose();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task ExtractFrameAt_OutOfRange_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "wf-extract2-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await GenerateVideoAsync(path, 320, 240, durationSec: 2);

            using var source = new FileVideoSource(path);
            var frame = source.ExtractFrameAt(10_000); // mimo 2s videa

            await Assert.That(frame).IsNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task GenerateVideoAsync(string path, int w, int h, int durationSec = 5)
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "ffmpeg",
            $"-y -f lavfi -i testsrc2=size={w}x{h}:rate=25:duration={durationSec} -pix_fmt yuv420p -c:v libx264 -preset veryfast -crf 28 {path}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        })!;
        await proc.WaitForExitAsync();
    }
}
