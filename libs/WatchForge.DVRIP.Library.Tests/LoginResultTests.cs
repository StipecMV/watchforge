namespace WatchForge.DVRIP.Library.Tests;

public class LoginResultTests
{
    // ── SessionIdHex ──────────────────────────────────────────────────────────

    [Test]
    public async Task SessionIdHex_ZeroSessionId_ReturnsFormattedZero()
    {
        // Given a login result with session ID 0
        var result = new LoginResult { SessionId = 0 };

        // When the hex representation is read
        var hex = result.SessionIdHex;

        // Then it is zero-padded to 8 hex digits with the 0x prefix
        await Assert.That(hex).IsEqualTo("0x00000000");
    }

    [Test]
    public async Task SessionIdHex_KnownSessionId_ReturnsUpperCaseHex()
    {
        // Given the session ID 0x0000001A as returned by the NVR after login
        var result = new LoginResult { SessionId = 0x0000001A };

        // When the hex representation is read
        var hex = result.SessionIdHex;

        // Then it matches the NVR's SessionID field format
        await Assert.That(hex).IsEqualTo("0x0000001A");
    }

    [Test]
    public async Task SessionIdHex_MaxUInt32_ReturnsFfff()
    {
        // Given the maximum possible session ID (all bits set)
        var result = new LoginResult { SessionId = uint.MaxValue };

        // When the hex representation is read
        var hex = result.SessionIdHex;

        // Then it is represented as 8 F digits
        await Assert.That(hex).IsEqualTo("0xFFFFFFFF");
    }

    [Test]
    public async Task SessionIdHex_AlwaysStartsWithPrefix()
    {
        // Given any login result
        var result = new LoginResult { SessionId = 42 };

        // When the hex representation is read
        var hex = result.SessionIdHex;

        // Then it always starts with the 0x prefix
        await Assert.That(hex.StartsWith("0x")).IsTrue();
    }

    [Test]
    public async Task SessionIdHex_AlwaysEightHexDigits()
    {
        // Given a session ID that would be short in naive formatting (e.g. 0x1B → "1B")
        var result = new LoginResult { SessionId = 0x1B };

        // When the hex representation is read
        var hex = result.SessionIdHex;

        // Then the hex part is always 8 characters wide (zero-padded)
        await Assert.That(hex.Length).IsEqualTo(10); // "0x" + 8 digits
        await Assert.That(hex).IsEqualTo("0x0000001B");
    }
}
