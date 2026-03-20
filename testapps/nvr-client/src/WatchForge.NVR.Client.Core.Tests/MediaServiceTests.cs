namespace WatchForge.NVR.Client.Core.Tests;

public class MediaServiceTests
{
    private Mock<IOnvifClientAdapter> _mockAdapter = null!;
    private MediaService _sut = null!;

    [Before(Test)]
    public void SetUp()
    {
        _mockAdapter = new Mock<IOnvifClientAdapter>();
        _sut = new MediaService(_mockAdapter.Object, "192.168.1.1");
    }

    // ── GetProfilesAsync ───────────────────────────────────────────────

    [Test]
    public async Task GetProfilesAsync_Success_ReturnsMappedProfiles()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse
            {
                Profiles =
                [
                    new Profile { token = "profile1", Name = "Main", @fixed = false },
                    new Profile { token = "profile2", Name = "Sub",  @fixed = true  }
                ]
            });

        var result = await _sut.GetProfilesAsync();

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Token).IsEqualTo("profile1");
        await Assert.That(result[0].Name).IsEqualTo("Main");
        await Assert.That(result[0].IsFixed).IsFalse();
        await Assert.That(result[1].Token).IsEqualTo("profile2");
        await Assert.That(result[1].IsFixed).IsTrue();
    }

    [Test]
    public async Task GetProfilesAsync_NullProfiles_ReturnsEmptyList()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse { Profiles = null });

        var result = await _sut.GetProfilesAsync();

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetProfilesAsync_NullTokenAndName_FallsBackToEmpty()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse
            {
                Profiles = [new Profile { token = null, Name = null }]
            });

        var result = await _sut.GetProfilesAsync();

        await Assert.That(result[0].Token).IsEqualTo(string.Empty);
        await Assert.That(result[0].Name).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetProfilesAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ThrowsAsync(new InvalidOperationException("network error"));

        await Assert.That(async () => await _sut.GetProfilesAsync())
            .Throws<OnvifConnectionException>();
    }

    // ── GetProfileByTokenAsync ─────────────────────────────────────────

    [Test]
    public async Task GetProfileByTokenAsync_TokenExists_ReturnsProfile()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse
            {
                Profiles = [new Profile { token = "profile1", Name = "Main" }]
            });

        var result = await _sut.GetProfileByTokenAsync("profile1");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Token).IsEqualTo("profile1");
    }

    [Test]
    public async Task GetProfileByTokenAsync_TokenExistsCaseInsensitive_ReturnsProfile()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse
            {
                Profiles = [new Profile { token = "PROFILE1", Name = "Main" }]
            });

        var result = await _sut.GetProfileByTokenAsync("profile1");

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetProfileByTokenAsync_TokenNotFound_ReturnsNull()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse
            {
                Profiles = [new Profile { token = "other", Name = "Other" }]
            });

        var result = await _sut.GetProfileByTokenAsync("profile1");

        await Assert.That(result).IsNull();
    }

    // ── GetStreamUriAsync ──────────────────────────────────────────────

    [Test]
    public async Task GetStreamUriAsync_Success_ReturnsMappedUri()
    {
        _mockAdapter.Setup(x => x.GetStreamUriAsync("profile1"))
            .ReturnsAsync(new MediaUri
            {
                Uri = "rtsp://192.168.1.1/stream",
                InvalidAfterConnect = false,
                InvalidAfterReboot = true,
                Timeout = null
            });

        var result = await _sut.GetStreamUriAsync("profile1");

        await Assert.That(result.Uri).IsEqualTo("rtsp://192.168.1.1/stream");
        await Assert.That(result.InvalidAfterConnect).IsEqualTo(false.ToString());
        await Assert.That(result.InvalidAfterReboot).IsEqualTo(true.ToString());
    }

    [Test]
    public async Task GetStreamUriAsync_NullResponse_ReturnsEmptyUri()
    {
        _mockAdapter.Setup(x => x.GetStreamUriAsync(It.IsAny<string>()))
            .ReturnsAsync((MediaUri)null!);

        var result = await _sut.GetStreamUriAsync("profile1");

        await Assert.That(result.Uri).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetStreamUriAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.GetStreamUriAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        await Assert.That(async () => await _sut.GetStreamUriAsync("profile1"))
            .Throws<OnvifConnectionException>();
    }

    // ── GetVideoSourcesAsync ───────────────────────────────────────────

    [Test]
    public async Task GetVideoSourcesAsync_ThrowsNotSupportedException()
    {
        await Assert.That(async () => await _sut.GetVideoSourcesAsync())
            .Throws<NotSupportedException>();
    }

    // ── CancellationToken propagation ──────────────────────────────────

    [Test]
    public async Task GetProfilesAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        _mockAdapter.Setup(x => x.GetProfilesAsync())
            .ReturnsAsync(new GetProfilesResponse { Profiles = [] });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await _sut.GetProfilesAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetStreamUriAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        _mockAdapter.Setup(x => x.GetStreamUriAsync(It.IsAny<string>()))
            .ReturnsAsync(new MediaUri { Uri = "rtsp://host/stream" });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await _sut.GetStreamUriAsync("profile1", cancellationToken: cts.Token))
            .Throws<OperationCanceledException>();
    }
}
