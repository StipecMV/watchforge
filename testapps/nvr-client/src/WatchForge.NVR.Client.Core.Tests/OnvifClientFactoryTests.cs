using Moq;

namespace WatchForge.NVR.Client.Core.Tests;

public class OnvifClientFactoryTests
{
    private static OnvifClientOptions ValidOptions() => new()
    {
        Host = "192.168.1.1",
        Port = 80,
        Username = "admin",
        Password = "password"
    };

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstanceWithExpectedProperties()
    {
        using var client = new OnvifClient(ValidOptions());

        await Assert.That(client).IsNotNull();
        await Assert.That(client.Host).IsEqualTo("192.168.1.1");
        await Assert.That(client.Device).IsNotNull();
        await Assert.That(client.Media).IsNotNull();
        await Assert.That(client.RecordingSearch).IsNotNull();
        await Assert.That(client.Events).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithInvalidOptions_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions { Host = "", Port = 80, Username = "admin", Password = "password" };

        await Assert.That(() => new OnvifClient(options))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsNullReferenceException()
    {
        await Assert.That(() => new OnvifClient(null!))
            .Throws<NullReferenceException>();
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes_DoesNotThrow()
    {
        var client = new OnvifClient(ValidOptions());

        await Assert.That(() => { client.Dispose(); client.Dispose(); }).ThrowsNothing();
    }

    [Test]
    public async Task UsingStatement_DisposesCleanly()
    {
        await Assert.That(() =>
        {
            using var client = new OnvifClient(ValidOptions());
        }).ThrowsNothing();
    }

    [Test]
    public async Task Host_MatchesOptionsHost()
    {
        var options = new OnvifClientOptions
        {
            Host = "example.com",
            Port = 443,
            Username = "user",
            Password = "pass"
        };
        using var client = new OnvifClient(options);

        await Assert.That(client.Host).IsEqualTo("example.com");
    }

    [Test]
    public async Task Options_ReturnsOriginalOptions()
    {
        var options = ValidOptions();
        using var client = new OnvifClient(options);

        await Assert.That(client.Options).IsEqualTo(options);
    }

    [Test]
    public async Task TestConnectionAsync_WithNoDevice_ReturnsFalse()
    {
        using var client = new OnvifClient(ValidOptions());

        var result = await client.TestConnectionAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TestConnectionAsync_WithCancellationToken_ReturnsBoolean()
    {
        using var client = new OnvifClient(ValidOptions());
        using var cts = new CancellationTokenSource();

        var result = await client.TestConnectionAsync(cts.Token);

        await Assert.That((object)result).IsTypeOf<bool>();
    }

    [Test]
    public async Task TestConnectionAsync_WhenDeviceSucceeds_ReturnsTrue()
    {
        var mockDevice = new Mock<IDeviceService>();
        mockDevice.Setup(d => d.GetDeviceInformationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceInformation("Hikvision", "DS-2CD", "1.0", null, null));
        using var client = new OnvifClient(ValidOptions(), mockDevice.Object);

        var result = await client.TestConnectionAsync();

        await Assert.That(result).IsTrue();
    }
}
