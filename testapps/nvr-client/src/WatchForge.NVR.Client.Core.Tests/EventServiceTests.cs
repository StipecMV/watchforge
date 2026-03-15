using Moq;
using SharpOnvifClient.Events;

namespace WatchForge.NVR.Client.Core.Tests;

public class EventServiceTests
{
    private Mock<IOnvifClientAdapter> _mockAdapter = null!;
    private EventService _sut = null!;

    private sealed class ThrowingEventService(IOnvifClientAdapter adapter, string host)
        : EventService(adapter, host)
    {
        protected override bool? TryGetIsMotionDetected(NotificationMessageHolderType notification)
            => throw new InvalidOperationException("parse error");
    }

    [Before(Test)]
    public void SetUp()
    {
        _mockAdapter = new Mock<IOnvifClientAdapter>();
        _sut = new EventService(_mockAdapter.Object, "192.168.1.1");
    }

    // ── SubscribeToPullPointAsync ──────────────────────────────────────

    [Test]
    public async Task SubscribeToPullPointAsync_Success_ReturnsNonEmptyReference()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(300))
            .ReturnsAsync("http://192.168.1.1/subscription/1");

        var reference = await _sut.SubscribeToPullPointAsync();

        await Assert.That(reference).IsNotNull();
        await Assert.That(reference.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task SubscribeToPullPointAsync_EmptyAddress_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync(string.Empty);

        await Assert.That(async () => await _sut.SubscribeToPullPointAsync())
            .Throws<OnvifConnectionException>();
    }

    [Test]
    public async Task SubscribeToPullPointAsync_NullAddress_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync((string?)null);

        await Assert.That(async () => await _sut.SubscribeToPullPointAsync())
            .Throws<OnvifConnectionException>();
    }

    [Test]
    public async Task SubscribeToPullPointAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("connection error"));

        await Assert.That(async () => await _sut.SubscribeToPullPointAsync())
            .Throws<OnvifConnectionException>();
    }

    // ── PullMessagesAsync ──────────────────────────────────────────────

    [Test]
    public async Task PullMessagesAsync_UnknownSubscription_ThrowsInvalidOperationException()
    {
        await Assert.That(async () => await _sut.PullMessagesAsync("unknown-ref"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PullMessagesAsync_EmptyMessages_ReturnsEmptyList()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointPullMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PullMessagesResponse { NotificationMessage = null });

        var reference = await _sut.SubscribeToPullPointAsync();
        var result = await _sut.PullMessagesAsync(reference);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PullMessagesAsync_WithMessages_ReturnsMappedMotionEvents()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointPullMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PullMessagesResponse
            {
                NotificationMessage = [new NotificationMessageHolderType { Topic = null }]
            });

        var reference = await _sut.SubscribeToPullPointAsync();
        var result = await _sut.PullMessagesAsync(reference);

        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PullMessagesAsync_WithNonNullTopic_SetsTopic()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointPullMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PullMessagesResponse
            {
                NotificationMessage = [new NotificationMessageHolderType { Topic = new TopicExpressionType() }]
            });

        var reference = await _sut.SubscribeToPullPointAsync();
        var result = await _sut.PullMessagesAsync(reference);

        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PullMessagesAsync_IsMotionDetectedThrows_TreatsAsNoMotion()
    {
        var sut = new ThrowingEventService(_mockAdapter.Object, "192.168.1.1");
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointPullMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PullMessagesResponse
            {
                NotificationMessage = [new NotificationMessageHolderType { Topic = null }]
            });

        var reference = await sut.SubscribeToPullPointAsync();
        var result = await sut.PullMessagesAsync(reference);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].IsMotion).IsFalse();
    }

    [Test]
    public async Task PullMessagesAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointPullMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("pull error"));

        var reference = await _sut.SubscribeToPullPointAsync();

        await Assert.That(async () => await _sut.PullMessagesAsync(reference))
            .Throws<OnvifConnectionException>();
    }

    // ── UnsubscribeAsync ───────────────────────────────────────────────

    [Test]
    public async Task UnsubscribeAsync_UnknownReference_ReturnsWithoutThrowing()
    {
        await Assert.That(async () => await _sut.UnsubscribeAsync("unknown-ref"))
            .ThrowsNothing();
    }

    [Test]
    public async Task UnsubscribeAsync_KnownReference_UnsubscribesSuccessfully()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointUnsubscribeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var reference = await _sut.SubscribeToPullPointAsync();

        await Assert.That(async () => await _sut.UnsubscribeAsync(reference))
            .ThrowsNothing();

        _mockAdapter.Verify(x => x.PullPointUnsubscribeAsync("http://host/subscription"), Times.Once);
    }

    [Test]
    public async Task UnsubscribeAsync_ClientThrows_ThrowsOnvifConnectionException()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointUnsubscribeAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("unsub error"));

        var reference = await _sut.SubscribeToPullPointAsync();

        await Assert.That(async () => await _sut.UnsubscribeAsync(reference))
            .Throws<OnvifConnectionException>();
    }

    // ── IsPullPointSupportedAsync ──────────────────────────────────────

    [Test]
    public async Task IsPullPointSupportedAsync_SubscribeReturnsAddress_ReturnsTrue()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync("http://host/subscription");
        _mockAdapter.Setup(x => x.PullPointUnsubscribeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.IsPullPointSupportedAsync();

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPullPointSupportedAsync_SubscribeReturnsEmptyAddress_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync(string.Empty);

        var result = await _sut.IsPullPointSupportedAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPullPointSupportedAsync_SubscribeReturnsNull_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ReturnsAsync((string?)null);

        var result = await _sut.IsPullPointSupportedAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPullPointSupportedAsync_ClientThrows_ReturnsFalse()
    {
        _mockAdapter.Setup(x => x.PullPointSubscribeAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("error"));

        var result = await _sut.IsPullPointSupportedAsync();

        await Assert.That(result).IsFalse();
    }
}
