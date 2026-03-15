namespace WatchForge.NVR.Client.Core.Tests;

public class RecordingSearchServiceTests
{
    private Mock<IOnvifClientAdapter> _mockAdapter = null!;
    private RecordingSearchService _sut = null!;

    [Before(Test)]
    public void SetUp()
    {
        _mockAdapter = new Mock<IOnvifClientAdapter>();
        _sut = new RecordingSearchService(_mockAdapter.Object, "192.168.1.1");
    }

    // ── SearchRecordingsAsync ──────────────────────────────────────────

    [Test]
    public async Task SearchRecordingsAsync_ReturnsEmptyArray()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;

        var result = await _sut.SearchRecordingsAsync(start, end);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SearchRecordingsAsync_WithToken_ReturnsEmptyArray()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;

        var result = await _sut.SearchRecordingsAsync(start, end, "token123");

        await Assert.That(result.Count).IsEqualTo(0);
    }

    // ── IsSearchSupportedAsync ─────────────────────────────────────────

    [Test]
    public async Task IsSearchSupportedAsync_RecordingServicePresent_ReturnsTrue()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = "http://www.onvif.org/ver10/recording/wsdl" }]
            });

        var result = await _sut.IsSearchSupportedAsync();

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsSearchSupportedAsync_NoRecordingService_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = "http://www.onvif.org/ver10/media" }]
            });

        var result = await _sut.IsSearchSupportedAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSearchSupportedAsync_NullServiceArray_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse { Service = null });

        var result = await _sut.IsSearchSupportedAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSearchSupportedAsync_NullNamespace_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ReturnsAsync(new GetServicesResponse
            {
                Service = [new Service { Namespace = null }]
            });

        var result = await _sut.IsSearchSupportedAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSearchSupportedAsync_ClientThrows_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.GetServicesAsync())
            .ThrowsAsync(new InvalidOperationException("network error"));

        var result = await _sut.IsSearchSupportedAsync();

        await Assert.That(result).IsFalse();
    }
}
