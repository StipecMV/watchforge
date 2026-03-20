namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IRecordingSearchService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class RecordingSearchService : IRecordingSearchService
{
    private readonly IOnvifClientAdapter _client;
    private readonly string _host;

    public RecordingSearchService(IOnvifClientAdapter client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public Task<IReadOnlyList<Recording>> SearchRecordingsAsync(
        DateTime startTime,
        DateTime endTime,
        string? recordingToken = null,
        CancellationToken cancellationToken = default)
    {
        // SharpOnvif SimpleOnvifClient does not expose recording search.
        throw new NotSupportedException(
            "Recording search is not supported by the SharpOnvif backend. " +
            "Use IsSearchSupportedAsync() to check capability before calling this method.");
    }

    public async Task<bool> IsSearchSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the RecordingControl service is available
            var services = await _client.GetServicesAsync().ConfigureAwait(false);
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
