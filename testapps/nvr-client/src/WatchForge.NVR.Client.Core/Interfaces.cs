namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Interface for ONVIF device management operations.
/// Follows Single Responsibility Principle - only handles device-level operations.
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// Gets basic device information.
    /// </summary>
    Task<DeviceInformation> GetDeviceInformationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available ONVIF services on the device.
    /// </summary>
    Task<IReadOnlyList<OnvifService>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific service is available on the device.
    /// </summary>
    Task<bool> HasServiceAsync(string serviceNamespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the device system date and time.
    /// </summary>
    Task<DateTime> GetSystemDateTimeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for ONVIF media operations.
/// Follows Single Responsibility Principle - only handles media/profile operations.
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Gets all media profiles from the device.
    /// </summary>
    Task<IReadOnlyList<MediaProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a media profile by its token.
    /// </summary>
    Task<MediaProfile?> GetProfileByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the stream URI for a specific profile.
    /// </summary>
    Task<StreamUri> GetStreamUriAsync(
        string profileToken,
        StreamType streamType = StreamType.RTPUnicast,
        TransportProtocol protocol = TransportProtocol.RTSP,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all video sources from the device.
    /// </summary>
    Task<IReadOnlyList<string>> GetVideoSourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for ONVIF recording search operations.
/// Follows Single Responsibility Principle - only handles recording search.
/// </summary>
public interface IRecordingSearchService
{
    /// <summary>
    /// Searches for recordings within a time range.
    /// </summary>
    Task<IReadOnlyList<Recording>> SearchRecordingsAsync(
        DateTime startTime,
        DateTime endTime,
        string? recordingToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the recording search capabilities.
    /// </summary>
    Task<bool> IsSearchSupportedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for ONVIF event subscription operations.
/// Follows Single Responsibility Principle - only handles event subscriptions.
/// </summary>
public interface IEventService
{
    /// <summary>
    /// Subscribes to pull point events.
    /// </summary>
    Task<string> SubscribeToPullPointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls messages from the pull point subscription.
    /// </summary>
    Task<IReadOnlyList<MotionEvent>> PullMessagesAsync(
        string subscriptionReference,
        int timeoutSeconds = 5,
        int messageLimit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a pull point subscription.
    /// </summary>
    Task UnsubscribeAsync(string subscriptionReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if pull point events are supported.
    /// </summary>
    Task<bool> IsPullPointSupportedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Main interface for ONVIF client operations.
/// Follows Interface Segregation Principle - composed of smaller interfaces.
/// </summary>
public interface IOnvifClient : IDisposable
{
    /// <summary>
    /// Gets the device service.
    /// </summary>
    IDeviceService Device { get; }

    /// <summary>
    /// Gets the media service.
    /// </summary>
    IMediaService Media { get; }

    /// <summary>
    /// Gets the recording search service.
    /// </summary>
    IRecordingSearchService? RecordingSearch { get; }

    /// <summary>
    /// Gets the event service.
    /// </summary>
    IEventService? Events { get; }

    /// <summary>
    /// Gets the device host address.
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Tests the connection to the device.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
