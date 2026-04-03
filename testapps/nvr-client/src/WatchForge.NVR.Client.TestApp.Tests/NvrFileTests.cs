namespace WatchForge.NVR.Client.TestApp.Tests;

public class NvrFileTests
{
    // ── ParseNvrDateTime ──────────────────────────────────────────────────────

    [Test]
    public async Task ParseNvrDateTime_StandardFormat_ReturnsCorrectDateTime()
    {
        // Given a datetime string in the standard DVRIP format
        var input = "2026-04-02 10:08:02";

        // When parsed
        var result = NvrFile.ParseNvrDateTime(input);

        // Then the correct DateTime is returned
        await Assert.That(result).IsEqualTo(new DateTime(2026, 4, 2, 10, 8, 2));
    }

    [Test]
    public async Task ParseNvrDateTime_MalformedNvrFormat_ReturnsCorrectDateTime()
    {
        // Given the malformed format emitted by Sofia firmware (no space between date and time)
        var input = "2026-04-0210:08:02";

        // When parsed
        var result = NvrFile.ParseNvrDateTime(input);

        // Then the correct DateTime is returned despite the missing space
        await Assert.That(result).IsEqualTo(new DateTime(2026, 4, 2, 10, 8, 2));
    }

    [Test]
    public async Task ParseNvrDateTime_MalformedNvrFormat_EndOfDayTime_ReturnsCorrectDateTime()
    {
        // Given the malformed format with 23:59:59
        var input = "2026-04-0223:59:59";

        // When parsed
        var result = NvrFile.ParseNvrDateTime(input);

        // Then end-of-day time is parsed correctly
        await Assert.That(result).IsEqualTo(new DateTime(2026, 4, 2, 23, 59, 59));
    }

    [Test]
    public async Task ParseNvrDateTime_NullInput_ReturnsMinValue()
    {
        // Given null input
        // When parsed
        var result = NvrFile.ParseNvrDateTime(null);

        // Then MinValue is returned (not an exception)
        await Assert.That(result).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task ParseNvrDateTime_EmptyInput_ReturnsMinValue()
    {
        // Given an empty string
        // When parsed
        var result = NvrFile.ParseNvrDateTime("");

        // Then MinValue is returned
        await Assert.That(result).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task ParseNvrDateTime_UnrecognisedFormat_ReturnsMinValue()
    {
        // Given a garbage string that matches no known format
        var input = "not-a-date";

        // When parsed
        var result = NvrFile.ParseNvrDateTime(input);

        // Then MinValue is returned without throwing
        await Assert.That(result).IsEqualTo(DateTime.MinValue);
    }

    // ── ParseFileLength ───────────────────────────────────────────────────────

    [Test]
    public async Task ParseFileLength_HexString_ReturnsByteCount()
    {
        // Given the hex string from the known NVR response in the spec
        var input = "0x00103D75";

        // When parsed
        var result = NvrFile.ParseFileLength(input);

        // Then the correct byte count is returned (0x00103D75 = 1064309)
        await Assert.That(result).IsEqualTo(1_064_309L);
    }

    [Test]
    public async Task ParseFileLength_LowercaseHexPrefix_ReturnsByteCount()
    {
        // Given a lowercase 0x prefix
        var input = "0x00103d75";

        // When parsed
        var result = NvrFile.ParseFileLength(input);

        // Then the value is the same regardless of case
        await Assert.That(result).IsEqualTo(1_064_309L);
    }

    [Test]
    public async Task ParseFileLength_PlainDecimalString_ReturnsValue()
    {
        // Given a plain decimal string (some firmware variants omit the hex prefix)
        var input = "1064309";

        // When parsed
        var result = NvrFile.ParseFileLength(input);

        // Then the decimal value is returned directly
        await Assert.That(result).IsEqualTo(1_064_309L);
    }

    [Test]
    public async Task ParseFileLength_NullInput_ReturnsZero()
    {
        // Given null input
        // When parsed
        var result = NvrFile.ParseFileLength(null);

        // Then zero is returned
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task ParseFileLength_EmptyInput_ReturnsZero()
    {
        // Given an empty string
        // When parsed
        var result = NvrFile.ParseFileLength("");

        // Then zero is returned
        await Assert.That(result).IsEqualTo(0L);
    }

    // ── FileLengthMB ─────────────────────────────────────────────────────────

    [Test]
    public async Task FileLengthMB_KnownByteCount_CalculatesCorrectly()
    {
        // Given a file whose size is 1 048 576 bytes (exactly 1 MiB)
        var file = new NvrFile { FileLengthBytes = 1_048_576 };

        // When the MB property is read
        var mb = file.FileLengthMB;

        // Then it equals 1.0
        await Assert.That(mb).IsEqualTo(1.0);
    }

    [Test]
    public async Task FileLengthMB_KnownNvrFileSize_MatchesExpected()
    {
        // Given the file length from the spec (0x00103D75 = 1 064 309 bytes ≈ 1.015 MB)
        var file = new NvrFile { FileLengthBytes = 1_064_309 };

        // When converted to MB
        var mb = file.FileLengthMB;

        // Then the value is approximately 1.015
        await Assert.That(mb).IsGreaterThan(1.01);
        await Assert.That(mb).IsLessThan(1.02);
    }
}
