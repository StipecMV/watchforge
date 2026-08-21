using WatchForge.DVRIP.Library.Models;

namespace WatchForge.DVRIP.Library.Tests;

/// <summary>
/// S3-1: filename parsing (koniec intervalu bez sekúnd — "15.15" vs "15.15.00")
/// a cleanup bug fix (raw súbor sa po konverzii maže, nie presúva na .raw).
/// </summary>
public class NvrFilenameTests
{
    [Test]
    public async Task ParseNvrFilename_SegmentWithSeconds_ReturnsFullRange()
    {
        // Given štandardný názov segmentu (koniec so sekundami)
        const string filename = "[Ch0]_2026-04-06_07.00.00-07.15.00.mkv";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then channel, begin a end sú správne
        await Assert.That(parsed.Channel).IsEqualTo("Ch0");
        await Assert.That(parsed.BeginTime).IsEqualTo(new DateTime(2026, 4, 6, 7, 0, 0));
        await Assert.That(parsed.EndTime).IsEqualTo(new DateTime(2026, 4, 6, 7, 15, 0));
        await Assert.That(parsed.IsValid).IsTrue();
    }

    [Test]
    public async Task ParseNvrFilename_SegmentWithoutSecondsAtEnd_DefaultsToZeroSeconds()
    {
        // Given názov s koncom bez sekúnd (reálny Movols NVR: "15.15" nie "15.15.00")
        const string filename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then end = 15:15:00 (sekundy sa doplnia na 00)
        await Assert.That(parsed.Channel).IsEqualTo("Ch0");
        await Assert.That(parsed.BeginTime).IsEqualTo(new DateTime(2026, 4, 5, 15, 0, 0));
        await Assert.That(parsed.EndTime).IsEqualTo(new DateTime(2026, 4, 5, 15, 15, 0));
        await Assert.That(parsed.IsValid).IsTrue();
    }

    [Test]
    public async Task ParseNvrFilename_EventClip_ShortRange()
    {
        // Given krátky event klip (30 s)
        const string filename = "[Ch2]_2026-04-06_14.30.00-14.30.30.mkv";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then správny rozsah
        await Assert.That(parsed.Channel).IsEqualTo("Ch2");
        await Assert.That(parsed.BeginTime).IsEqualTo(new DateTime(2026, 4, 6, 14, 30, 0));
        await Assert.That(parsed.EndTime).IsEqualTo(new DateTime(2026, 4, 6, 14, 30, 30));
        await Assert.That(parsed.Duration).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task ParseNvrFilename_EventClipWithoutSecondsEnds()
    {
        // Given event klip, oba konce bez sekúnd ("15.00-15.02")
        const string filename = "[Ch1]_2026-04-06_15.00-15.02.mkv";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then sekundy sa doplnia na 00
        await Assert.That(parsed.BeginTime).IsEqualTo(new DateTime(2026, 4, 6, 15, 0, 0));
        await Assert.That(parsed.EndTime).IsEqualTo(new DateTime(2026, 4, 6, 15, 2, 0));
        await Assert.That(parsed.IsValid).IsTrue();
    }

    [Test]
    public async Task ParseNvrFilename_Invalid_ReturnsInvalid()
    {
        // Given neplatný názov
        const string filename = "random-file.txt";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then invalid
        await Assert.That(parsed.IsValid).IsFalse();
        await Assert.That(parsed.Channel).IsEqualTo("");
    }

    [Test]
    public async Task ParseNvrFilename_SingleDigitHour_IsParsed()
    {
        // Given segment skoro ráno (hodina bez vedúcej nuly)
        const string filename = "[Ch0]_2026-04-06_6.00.00-6.15.00.mkv";

        // When parsujeme
        var parsed = NvrFile.ParseNvrFilename(filename);

        // Then začiatok 06:00
        await Assert.That(parsed.BeginTime).IsEqualTo(new DateTime(2026, 4, 6, 6, 0, 0));
        await Assert.That(parsed.EndTime).IsEqualTo(new DateTime(2026, 4, 6, 6, 15, 0));
    }
}
