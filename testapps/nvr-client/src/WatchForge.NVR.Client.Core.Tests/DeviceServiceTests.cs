namespace WatchForge.NVR.Client.Core.Tests;

public class DeviceServiceTests
{
    private Mock<IOnvifClientAdapter> _mockAdapter = null!;
    private DeviceService _sut = null!;

    [Before(Test)]
    public void SetUp()
    {
        _mockAdapter = new Mock<IOnvifClientAdapter>();
        _sut = new DeviceService(_mockAdapter.Object, "192.168.1.1");
    }

    // ── GetDeviceInformationAsync ──────────────────────────────────────

    [Test]
    public async Task GetDeviceInformationAsync_Success_ReturnsMappedData()
    {
        _mockAdapter.Setup(x => x.GetDeviceInformationAsync())
            .ReturnsAsync(new GetDeviceInformationResponse
            {
                Manufacturer = "Hikvision",
                Model = "DS-2CD2143G2",
                FirmwareVersion = "V5.7.7",
                SerialNumber = "SN123",
                HardwareId = "HW456"
            });

        var result = await _sut.GetDeviceInformationAsync();

        await Assert.That(result.Manufacturer).IsEqualTo("Hikvision");
        await Assert.That(result.Model).IsEqualTo("DS-2CD2143G2");
        await Assert.That(result.FirmwareVersion).IsEqualTo("V5.7.7");
        await Assert.That(result.SerialNumber).IsEqualTo("SN123");
        await Assert.That(result.HardwareId).IsEqualTo("HW456");
    }

    [Test]
    public async Task GetDeviceInformationAsync_NullFields_FallsBackToUnknown()
    {
        _mockAdapter.Setup(x => x.GetDeviceInformationAsync())
            .ReturnsAsync(new GetDeviceInformationResponse
            {
                Manufacturer = null,
                Model = null,
                FirmwareVersion = null
            });

        var result = await _sut.GetDeviceInformationAsync();

        await Assert.That(result.Manufacturer).IsEqualTo("Unknown");
        await Assert.That(result.Model).IsEqualTo("Unknown");
        await Assert.That(result.FirmwareVersion).IsEqualTo("Unknown");
        await Assert.That(result.SerialNumber).IsNull();
        await Assert.That(result.HardwareId).IsNull();
    }

    [Test]
    public async Task GetDeviceInformationAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.GetDeviceInformationAsync())
            .ThrowsAsync(new InvalidOperationException("network error"));

        await Assert.That(async () => await _sut.GetDeviceInformationAsync())
            .Throws<OnvifConnectionException>();
    }

    // ── GetServicesAsync ───────────────────────────────────────────────

    [Test]
    public async Task GetServicesAsync_Success_ReturnsMappedServices()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service =
                [
                    new Service { Namespace = "http://www.onvif.org/ver10/device", XAddr = "http://192.168.1.1/device" },
                    new Service { Namespace = "http://www.onvif.org/ver10/media", XAddr = "http://192.168.1.1/media" }
                ]
            });

        var result = await _sut.GetServicesAsync();

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Namespace).IsEqualTo("http://www.onvif.org/ver10/device");
        await Assert.That(result[1].Namespace).IsEqualTo("http://www.onvif.org/ver10/media");
    }

    [Test]
    public async Task GetServicesAsync_NullNamespaceAndXAddr_FallsBackToEmpty()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = null, XAddr = null }]
            });

        var result = await _sut.GetServicesAsync();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Namespace).IsEqualTo(string.Empty);
        await Assert.That(result[0].XAddr).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetServicesAsync_NullServiceArray_ReturnsEmptyList()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse { Service = null });

        var result = await _sut.GetServicesAsync();

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetServicesAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ThrowsAsync(new InvalidOperationException("network error"));

        await Assert.That(async () => await _sut.GetServicesAsync())
            .Throws<OnvifConnectionException>();
    }

    // ── HasServiceAsync ────────────────────────────────────────────────

    [Test]
    public async Task HasServiceAsync_ServiceExists_ReturnsTrue()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = "http://www.onvif.org/ver20/recording/wsdl", XAddr = "http://host" }]
            });

        var result = await _sut.HasServiceAsync("http://www.onvif.org/ver20/recording/wsdl");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasServiceAsync_ServiceExistsCaseInsensitive_ReturnsTrue()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = "HTTP://WWW.ONVIF.ORG/VER20/RECORDING/WSDL", XAddr = "http://host" }]
            });

        var result = await _sut.HasServiceAsync("http://www.onvif.org/ver20/recording/wsdl");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasServiceAsync_ServiceNotExists_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = "http://other", XAddr = "http://host" }]
            });

        var result = await _sut.HasServiceAsync("http://www.onvif.org/ver20/recording/wsdl");

        await Assert.That(result).IsFalse();
    }

    // ── GetSystemDateTimeAsync ─────────────────────────────────────────

    [Test]
    public async Task GetSystemDateTimeAsync_Success_ReturnsDateTime()
    {
        var expected = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _mockAdapter.Setup(x => x.GetSystemDateAndTimeUtcAsync())
            .ReturnsAsync(expected);

        var result = await _sut.GetSystemDateTimeAsync();

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task GetSystemDateTimeAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.GetSystemDateAndTimeUtcAsync())
            .ThrowsAsync(new InvalidOperationException("network error"));

        await Assert.That(async () => await _sut.GetSystemDateTimeAsync())
            .Throws<OnvifConnectionException>();
    }

    // ── CancellationToken propagation ──────────────────────────────────

    [Test]
    public async Task GetDeviceInformationAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        _mockAdapter.Setup(x => x.GetDeviceInformationAsync())
            .ReturnsAsync(new GetDeviceInformationResponse { Manufacturer = "X", Model = "Y", FirmwareVersion = "Z" });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await _sut.GetDeviceInformationAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetServicesAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse { Service = [] });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await _sut.GetServicesAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetSystemDateTimeAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        _mockAdapter.Setup(x => x.GetSystemDateAndTimeUtcAsync())
            .ReturnsAsync(DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await _sut.GetSystemDateTimeAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }
}
