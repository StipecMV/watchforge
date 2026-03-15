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

    internal OnvifClient(OnvifClientOptions options, IDeviceService device)
    {
        Options = options;
        Host = options.Host;
        Device = device;
        Media = null!;
        RecordingSearch = null;
        Events = null;
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _adapter?.Dispose();
        }

        _disposed = true;
    }

    ~OnvifClient()
    {
        Dispose(false);
    }
}
