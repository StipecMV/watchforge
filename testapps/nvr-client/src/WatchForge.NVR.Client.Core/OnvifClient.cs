using SharpOnvifClient;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Main ONVIF client implementation.
/// Follows Composite Reuse Principle - composed of smaller services.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class OnvifClient : IOnvifClient
{
    private readonly SimpleOnvifClient _innerClient;
    private bool _disposed;

    public OnvifClient(OnvifClientOptions options)
    {
        options.Validate();
        
        Options = options;
        Host = options.Host;
        _innerClient = new SimpleOnvifClient(options.EndpointUrl, options.Username, options.Password);
        
        // Initialize services (Dependency Injection)
        Device = new DeviceService(_innerClient, Host);
        Media = new MediaService(_innerClient, Host);
        
        // Optional services - may be null if not supported
        RecordingSearch = new RecordingSearchService(_innerClient, Host);
        Events = new EventService(_innerClient, Host);
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
            _innerClient?.Dispose();
        }
        
        _disposed = true;
    }

    ~OnvifClient()
    {
        Dispose(false);
    }
}
