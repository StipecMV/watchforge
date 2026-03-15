using SharpOnvifClient;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IRecordingSearchService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class RecordingSearchService : IRecordingSearchService
{
    private readonly SimpleOnvifClient _client;
    private readonly string _host;

    public RecordingSearchService(SimpleOnvifClient client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<IReadOnlyList<Recording>> SearchRecordingsAsync(
        DateTime startTime,
        DateTime endTime,
        string? recordingToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Note: Recording search requires the RecordingControl service
            // SharpOnvif SimpleOnvifClient doesn't expose recording search directly
            // This is a placeholder - actual implementation would require low-level client
            await Task.CompletedTask;
            return Array.Empty<Recording>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to search recordings: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsSearchSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the RecordingControl service is available
            var services = await _client.GetServicesAsync();
            return services.Service?.Any(s => 
                s.Namespace?.Contains("recording", StringComparison.OrdinalIgnoreCase) == true
            ) ?? false;
        }
        catch
        {
            return false;
        }
    }
}
