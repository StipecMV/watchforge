namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IEventService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class EventService : IEventService
{
    private readonly IOnvifClientAdapter _client;
    private readonly string _host;
    private readonly Dictionary<string, string> _subscriptions = new();

    public EventService(IOnvifClientAdapter client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<string> SubscribeToPullPointAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionAddress = await _client.PullPointSubscribeAsync(300);
            if (string.IsNullOrWhiteSpace(subscriptionAddress))
            {
                throw new OnvifConnectionException(_host, "Failed to obtain pull point subscription address.");
            }

            var subscriptionReference = Guid.NewGuid().ToString("N");
            _subscriptions[subscriptionReference] = subscriptionAddress;
            return subscriptionReference;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to subscribe to pull point: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<MotionEvent>> PullMessagesAsync(
        string subscriptionReference,
        int timeoutSeconds = 5,
        int messageLimit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.ContainsKey(subscriptionReference))
        {
            throw new InvalidOperationException($"Subscription '{subscriptionReference}' not found. Call SubscribeToPullPointAsync first.");
        }

        try
        {
            var response = await _client.PullPointPullMessagesAsync(
                _subscriptions[subscriptionReference],
                timeoutSeconds,
                messageLimit);

            var motionEvents = new List<MotionEvent>();

            var notificationMessages = response.NotificationMessage ?? Array.Empty<NotificationMessageHolderType>();

            foreach (var notification in notificationMessages)
            {
                bool isMotion = false;
                try
                {
                    isMotion = TryGetIsMotionDetected(notification) ?? false;
                }
                catch
                {
                    isMotion = false;
                }

                motionEvents.Add(new MotionEvent(
                    Timestamp: DateTime.UtcNow,
                    IsMotion: isMotion,
                    Source: notification?.Topic?.ToString(),
                    Description: notification?.Topic?.ToString()));
            }

            return motionEvents.AsReadOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to pull messages: {ex.Message}", ex);
        }
    }

    public async Task UnsubscribeAsync(string subscriptionReference, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.ContainsKey(subscriptionReference))
        {
            return;
        }

        try
        {
            await _client.PullPointUnsubscribeAsync(_subscriptions[subscriptionReference]);
            _subscriptions.Remove(subscriptionReference);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to unsubscribe: {ex.Message}", ex);
        }
    }

    protected virtual bool? TryGetIsMotionDetected(NotificationMessageHolderType notification)
        => OnvifEvents.IsMotionDetected(notification);

    public async Task<bool> IsPullPointSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var address = await _client.PullPointSubscribeAsync(300);
            if (!string.IsNullOrEmpty(address))
            {
                await _client.PullPointUnsubscribeAsync(address);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
