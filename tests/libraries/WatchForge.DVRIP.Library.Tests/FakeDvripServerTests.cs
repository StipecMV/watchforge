namespace WatchForge.DVRIP.Library.Tests;

using WatchForge.DVRIP.Library.Models;

/// <summary>
/// Spike S1-1: overuje, že fake NVR test server emuluje DVRIP handshake
/// natoľko dobre, že naň DvripClient dokáže úspešne nadviazať spojenie
/// (login → file query → playback claim → download data).
/// TDD: RED (trieda neexistuje) → GREEN (implementácia).
/// </summary>
public class FakeDvripServerTests
{
    [Test]
    public async Task Login_WithFakeServer_ReturnsSessionIdAndDeviceType()
    {
        // Given a fake NVR server running on an ephemeral port
        using var server = new FakeDvripServer(validUsername: "admin", validPassword: "password");
        await server.StartAsync();

        // When a real DvripClient logs in
        using var client = new DvripClient("127.0.0.1", server.Port, "admin", "password");
        var result = await client.LoginAsync();

        // Then the client gets a valid session and device info
        await Assert.That(result.SessionId).IsNotEqualTo(0u);
        await Assert.That(result.DeviceType).IsNotEmpty();
        await Assert.That(result.ChannelNum).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Login_WrongPassword_ThrowsInvalidOperation()
    {
        // Given a fake NVR that rejects bad credentials
        using var server = new FakeDvripServer(validUsername: "admin", validPassword: "secret");
        await server.StartAsync();

        // When a client logs in with wrong password
        using var client = new DvripClient("127.0.0.1", server.Port, "admin", "wrong");

        // Then login fails with code 203
#pragma warning disable CS8619 // TUnit odvodí Task<LoginResult> vs Task<LoginResult?> (test framework, nie bug)
        var ex = await Assert.That(() => client.LoginAsync())
            .Throws<InvalidOperationException>();
#pragma warning restore CS8619
        await Assert.That(ex!.Message).Contains("code 203");
    }

    [Test]
    public async Task QueryFiles_WithFakeServer_ReturnsConfiguredFiles()
    {
        // Given a fake NVR with one configured recording
        var fileEntry = new FakeDvripServer.RecordingEntry(
            FileName: "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            BeginTime: new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime: new DateTime(2026, 4, 5, 15, 15, 0),
            LengthBlocks: 100);
        using var server = new FakeDvripServer(recordings: [fileEntry]);
        await server.StartAsync();

        // When a client queries files for that period
        using var client = new DvripClient("127.0.0.1", server.Port, "admin", "secret");
        await client.LoginAsync();
        var files = await client.QueryFilesAsync(
            new DateTime(2026, 4, 5, 14, 0, 0),
            new DateTime(2026, 4, 5, 16, 0, 0),
            channel: 0);

        // Then the configured recording is returned with correct length
        await Assert.That(files).Count().IsEqualTo(1);
        await Assert.That(files[0].FileName).IsEqualTo(fileEntry.FileName);
        await Assert.That(files[0].FileLengthBytes).IsEqualTo(fileEntry.LengthBlocks * 1024L);
    }

    [Test]
    public async Task DownloadFile_WithFakeServer_WritesPayloadBytes()
    {
        // Given a fake NVR with a recording whose payload is a known pattern
        var fileEntry = new FakeDvripServer.RecordingEntry(
            FileName: "[Ch1]_2026-04-05_16.00.00-16.15.mkv",
            BeginTime: new DateTime(2026, 4, 5, 16, 0, 0),
            EndTime: new DateTime(2026, 4, 5, 16, 15, 0),
            LengthBlocks: 64); // 64 * 1024 = 65536 bytes of 0xAA
        using var server = new FakeDvripServer(recordings: [fileEntry], fillByte: 0xAA);
        await server.StartAsync();

        // When the client downloads the file
        var tmpDir = Path.Combine(Path.GetTempPath(), "watchforge-fake-nvr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var rawPath = Path.Combine(tmpDir, "download.raw");
            using var client = new DvripClient("127.0.0.1", server.Port, "admin", "secret");
            await client.LoginAsync();
            var files = await client.QueryFilesAsync(
                new DateTime(2026, 4, 5, 15, 0, 0),
                new DateTime(2026, 4, 5, 17, 0, 0),
                channel: 1);
            var nvrFile = files.Single();

            // Then the raw payload is written (ffmpeg not found → raw kept)
            var outputPath = await client.DownloadFileAsync(nvrFile, rawPath, outputFormat: "mkv");

            // And it contains exactly LengthBlocks*1024 bytes of the fill byte
            var bytes = await File.ReadAllBytesAsync(outputPath);
            await Assert.That(bytes.Length).IsEqualTo(65536);
            await Assert.That(bytes.All(b => b == 0xAA)).IsTrue();
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
