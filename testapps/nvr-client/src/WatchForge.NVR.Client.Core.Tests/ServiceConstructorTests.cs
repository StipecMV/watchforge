namespace WatchForge.NVR.Client.Core.Tests;

public class ServiceConstructorTests
{
    private static IOnvifClientAdapter MockAdapter() => new Mock<IOnvifClientAdapter>().Object;

    [Test]
    public async Task DeviceService_Constructor_NullClient_ThrowsArgumentNullException()
    {
        await Assert.That(() => new DeviceService(null!, "host"))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DeviceService_Constructor_NullHost_ThrowsArgumentNullException()
    {
        await Assert.That(() => new DeviceService(MockAdapter(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DeviceService_Constructor_ValidParams_CreatesInstance()
    {
        var service = new DeviceService(MockAdapter(), "192.168.1.1");

        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task MediaService_Constructor_NullClient_ThrowsArgumentNullException()
    {
        await Assert.That(() => new MediaService(null!, "host"))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MediaService_Constructor_NullHost_ThrowsArgumentNullException()
    {
        await Assert.That(() => new MediaService(MockAdapter(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MediaService_Constructor_ValidParams_CreatesInstance()
    {
        var service = new MediaService(MockAdapter(), "192.168.1.1");

        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task RecordingSearchService_Constructor_NullClient_ThrowsArgumentNullException()
    {
        await Assert.That(() => new RecordingSearchService(null!, "host"))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task RecordingSearchService_Constructor_NullHost_ThrowsArgumentNullException()
    {
        await Assert.That(() => new RecordingSearchService(MockAdapter(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task RecordingSearchService_Constructor_ValidParams_CreatesInstance()
    {
        var service = new RecordingSearchService(MockAdapter(), "192.168.1.1");

        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task EventService_Constructor_NullClient_ThrowsArgumentNullException()
    {
        await Assert.That(() => new EventService(null!, "host"))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EventService_Constructor_NullHost_ThrowsArgumentNullException()
    {
        await Assert.That(() => new EventService(MockAdapter(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EventService_Constructor_ValidParams_CreatesInstance()
    {
        var service = new EventService(MockAdapter(), "192.168.1.1");

        await Assert.That(service).IsNotNull();
    }
}
