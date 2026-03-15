namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IDeviceService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class DeviceService : IDeviceService
{
    private readonly IOnvifClientAdapter _client;
    private readonly string _host;

    public DeviceService(IOnvifClientAdapter client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<DeviceInformation> GetDeviceInformationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetDeviceInformationAsync();
            return new DeviceInformation(
                Manufacturer: response.Manufacturer ?? "Unknown",
                Model: response.Model ?? "Unknown",
                FirmwareVersion: response.FirmwareVersion ?? "Unknown",
                SerialNumber: response.SerialNumber,
                HardwareId: response.HardwareId
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to get device information: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<OnvifService>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetServicesAsync();
            var services = new List<OnvifService>();
            
            if (response.Service != null)
            {
                foreach (var s in response.Service)
                {
                    services.Add(new OnvifService(
                        Namespace: s.Namespace ?? string.Empty,
                        XAddr: s.XAddr ?? string.Empty,
                        Version: null // Version parsing simplified
                    ));
                }
            }
            
            return services.AsReadOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to get services: {ex.Message}", ex);
        }
    }

    public async Task<bool> HasServiceAsync(string serviceNamespace, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        return services.Any(s => s.Namespace.Equals(serviceNamespace, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DateTime> GetSystemDateTimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.GetSystemDateAndTimeUtcAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to get system date/time: {ex.Message}", ex);
        }
    }
}
