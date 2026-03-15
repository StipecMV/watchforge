using SharpOnvifClient;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Implementation of IMediaService using SharpOnvifClient.
/// Follows Dependency Inversion Principle - depends on abstractions.
/// </summary>
public class MediaService : IMediaService
{
    private readonly SimpleOnvifClient _client;
    private readonly string _host;

    public MediaService(SimpleOnvifClient client, string host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public async Task<IReadOnlyList<MediaProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetProfilesAsync();
            var profiles = new List<MediaProfile>();

            if (response.Profiles != null)
            {
                foreach (var p in response.Profiles)
                {
                    profiles.Add(new MediaProfile(
                        Token: p.token ?? string.Empty,
                        Name: p.Name ?? string.Empty,
                        IsFixed: p.@fixed
                    ));
                }
            }

            return profiles.AsReadOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to get media profiles: {ex.Message}", ex);
        }
    }

    public async Task<MediaProfile?> GetProfileByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var profiles = await GetProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(p => p.Token.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<StreamUri> GetStreamUriAsync(
        string profileToken,
        StreamType streamType = StreamType.RTPUnicast,
        TransportProtocol protocol = TransportProtocol.RTSP,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // SimpleOnvifClient provides a direct GetStreamUriAsync(token) call.
            var response = await _client.GetStreamUriAsync(profileToken);

            return new StreamUri(
                Uri: response?.Uri ?? string.Empty,
                InvalidAfterConnect: response?.InvalidAfterConnect.ToString(),
                InvalidAfterReboot: response?.InvalidAfterReboot.ToString(),
                Timeout: response?.Timeout
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OnvifConnectionException(_host, $"Failed to get stream URI: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetVideoSourcesAsync(CancellationToken cancellationToken = default)
    {
        // SharpOnvif doesn't expose GetVideoSources directly in SimpleOnvifClient
        await Task.CompletedTask;
        return Array.Empty<string>();
    }
}
