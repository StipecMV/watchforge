namespace WatchForge.NVR.Client.TestApp.Tests;

public class SofiaPasswordTests
{
    // ── ComputeSofiaHash ──────────────────────────────────────────────────────
    //
    // Algorithm:
    //   1. MD5(password) → 16-byte digest
    //   2. Take bytes at even indices: [0,2,4,6,8,10,12,14]
    //   3. Each byte b → (char)(b % 61 + 64)
    //   4. Concatenate → 8-char ASCII string

    [Test]
    public async Task ComputeSofiaHash_AlwaysReturnsEightCharString()
    {
        // Given any non-empty password
        var password = "anyPassword123";

        // When the hash is computed
        var hash = DvripClient.ComputeSofiaHash(password);

        // Then the result is exactly 8 characters
        await Assert.That(hash.Length).IsEqualTo(8);
    }

    [Test]
    public async Task ComputeSofiaHash_OutputCharsAreInValidRange()
    {
        // Given a password
        var hash = DvripClient.ComputeSofiaHash("testpassword");

        // When each character is inspected
        // Then every char is in the range [64, 124] (i.e. '@' through '|')
        // because: 0 % 61 + 64 = 64,  60 % 61 + 64 = 124
        foreach (var c in hash)
        {
            await Assert.That((int)c).IsGreaterThanOrEqualTo(64);
            await Assert.That((int)c).IsLessThanOrEqualTo(124);
        }
    }

    [Test]
    public async Task ComputeSofiaHash_SameInputProducesSameOutput()
    {
        // Given the same password hashed twice
        var first  = DvripClient.ComputeSofiaHash("deterministic");
        var second = DvripClient.ComputeSofiaHash("deterministic");

        // When compared
        // Then they are identical (hash is deterministic)
        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task ComputeSofiaHash_DifferentPasswordsProduceDifferentHashes()
    {
        // Given two different passwords
        var hash1 = DvripClient.ComputeSofiaHash("password1");
        var hash2 = DvripClient.ComputeSofiaHash("password2");

        // When compared
        // Then they differ (collision in 8 chars is extremely unlikely for close inputs)
        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task ComputeSofiaHash_KnownInput_ReturnsExpectedHash()
    {
        // Given the well-known password "password"
        // MD5("password") = 5f4dcc3b5aa765d61d8327deb882cf99
        // Even-index bytes: 0x5f,0xcc,0x5a,0x65,0x1d,0x27,0xb8,0xcf
        //   = [95, 204, 90, 101, 29, 39, 184, 207]
        // % 61 + 64:
        //   95%61=34 → 98='b', 204%61=21 → 85='U', 90%61=29 → 93=']'
        //   101%61=40 → 104='h', 29%61=29 → 93=']', 39%61=39 → 103='g'
        //   184%61=1  → 65='A', 207%61=24 → 88='X'
        // Expected: "bU]h]gAX"
        var expected = "bU]h]gAX";

        // When the hash is computed
        var hash = DvripClient.ComputeSofiaHash("password");

        // Then it matches the manually verified expected value
        await Assert.That(hash).IsEqualTo(expected);
    }

    [Test]
    public async Task ComputeSofiaHash_EmptyPassword_DoesNotThrow()
    {
        // Given an empty password (edge case)
        // When hashed
        var hash = DvripClient.ComputeSofiaHash("");

        // Then we get an 8-char result without throwing
        await Assert.That(hash.Length).IsEqualTo(8);
    }
}
