using SharpOnvifClient;
using SharpOnvifClient.Events;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IEventService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class EventService : IEventService
{
    private readonly SimpleOnvifClient _client;
    private readonly string _host;
    private readonly Dictionary<string, string> _subscriptions = new();

    public EventService(SimpleOnvifClient client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<string> SubscribeToPullPointAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Using the library API: initial termination time in seconds is required (e.g., 300s)
            var subscription = await _client.PullPointSubscribeAsync(300);
            var subscriptionAddress = subscription.SubscriptionReference?.Address?.ToString();
            if (string.IsNullOrWhiteSpace(subscriptionAddress))
            {
                throw new OnvifConnectionException(_host, "Failed to obtain pull point subscription address.", null);
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

            // PullMessagesResponse contains NotificationMessage array
            var notificationMessages = response.NotificationMessage ?? Array.Empty<NotificationMessageHolderType>();

            foreach (var notification in notificationMessages)
            {
                // Not all fields might be available in every message; keep defaults.
                bool isMotion = false;
                try
                {
                    // Some versions may include OnvifEvents helper
                    isMotion = OnvifEvents.IsMotionDetected(notification) ?? false;
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

    public async Task<bool> IsPullPointSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to create a subscription and immediately unsubscribe
            var subscription = await _client.PullPointSubscribeAsync(300);
            var address = subscription.SubscriptionReference?.Address?.ToString();
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
