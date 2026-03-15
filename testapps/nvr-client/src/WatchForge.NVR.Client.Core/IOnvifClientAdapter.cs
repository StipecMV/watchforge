using SharpOnvifClient.DeviceMgmt;
using SharpOnvifClient.Events;
using SharpOnvifClient.Media;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Abstraction over SimpleOnvifClient to enable unit testing.
/// All methods mirror the corresponding SimpleOnvifClient methods.
/// </summary>
public interface IOnvifClientAdapter : IDisposable
{
    Task<GetDeviceInformationResponse> GetDeviceInformationAsync();
    Task<GetServicesResponse> GetServicesAsync();
    Task<global::System.DateTime> GetSystemDateAndTimeUtcAsync();
    Task<GetProfilesResponse> GetProfilesAsync();
    Task<MediaUri> GetStreamUriAsync(string profileToken);

    /// <summary>
    /// Creates a pull point subscription.
    /// </summary>
    /// <returns>The subscription endpoint address, or null if unavailable.</returns>
    Task<string?> PullPointSubscribeAsync(int initialTerminationTimeInSeconds);

    Task<PullMessagesResponse> PullPointPullMessagesAsync(
        string subscriptionAddress,
        int timeoutSeconds,
        int messageLimit);

    Task PullPointUnsubscribeAsync(string subscriptionAddress);
}
