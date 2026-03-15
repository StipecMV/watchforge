namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Wraps SimpleOnvifClient and implements IOnvifClientAdapter.
/// Extracts primitive values from WSDL types where needed (e.g., subscription address).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin adapter over non-virtual SimpleOnvifClient; requires integration tests with real ONVIF device.")]
public sealed class OnvifClientAdapter : IOnvifClientAdapter
{
    private readonly SimpleOnvifClient _client;
    private bool _disposed;

    public OnvifClientAdapter(SimpleOnvifClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<GetDeviceInformationResponse> GetDeviceInformationAsync()
        => _client.GetDeviceInformationAsync();

    public Task<GetServicesResponse> GetServicesAsync()
        => _client.GetServicesAsync();

    public Task<DateTime> GetSystemDateAndTimeUtcAsync()
        => _client.GetSystemDateAndTimeUtcAsync();

    public Task<GetProfilesResponse> GetProfilesAsync()
        => _client.GetProfilesAsync();

    public Task<MediaUri> GetStreamUriAsync(string profileToken)
        => _client.GetStreamUriAsync(profileToken);

    public async Task<string?> PullPointSubscribeAsync(int initialTerminationTimeInSeconds)
    {
        var response = await _client.PullPointSubscribeAsync(initialTerminationTimeInSeconds);
        return response.SubscriptionReference?.Address?.ToString();
    }

    public Task<PullMessagesResponse> PullPointPullMessagesAsync(
        string subscriptionAddress,
        int timeoutSeconds,
        int messageLimit)
        => _client.PullPointPullMessagesAsync(subscriptionAddress, timeoutSeconds, messageLimit);

    public Task PullPointUnsubscribeAsync(string subscriptionAddress)
        => _client.PullPointUnsubscribeAsync(subscriptionAddress);

    public void Dispose()
    {
        if (_disposed) return;
        _client.Dispose();
        _disposed = true;
    }
}
