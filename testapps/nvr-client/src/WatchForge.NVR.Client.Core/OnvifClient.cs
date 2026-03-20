namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Main ONVIF client implementation.
/// Follows Composite Reuse Principle - composed of smaller services.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class OnvifClient : IOnvifClient
{
    private readonly IOnvifClientAdapter? _adapter;
    private bool _disposed;

    public OnvifClient(OnvifClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        Options = options;
        Host = options.Host;
        _adapter = new OnvifClientAdapter(
            new SimpleOnvifClient(options.EndpointUrl, options.Username, options.Password));

        Device = new DeviceService(_adapter, Host);
        Media = new MediaService(_adapter, Host);
        RecordingSearch = new RecordingSearchService(_adapter, Host);
        Events = new EventService(_adapter, Host);
    }

    /// <summary>
    /// DI constructor — services are owned and disposed by the container.
    /// Also used as test helper: pass mocks for the services under test.
    /// </summary>
    internal OnvifClient(
        OnvifClientOptions options,
        IDeviceService device,
        IMediaService media,
        IRecordingSearchService recordingSearch,
        IEventService events)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(recordingSearch);
        ArgumentNullException.ThrowIfNull(events);
        Options = options;
        Host = options.Host;
        // _adapter stays null — owned by the DI container, not by this instance
        Device = device;
        Media = media;
        RecordingSearch = recordingSearch;
        Events = events;
    }

    public OnvifClientOptions Options { get; }

    public string Host { get; }

    public IDeviceService Device { get; }

    public IMediaService Media { get; }

    public IRecordingSearchService? RecordingSearch { get; }

    public IEventService? Events { get; }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Device.GetDeviceInformationAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _adapter?.Dispose();
        _disposed = true;
    }
}
