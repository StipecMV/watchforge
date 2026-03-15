using Microsoft.Extensions.DependencyInjection;
using SharpOnvifClient;

namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Extension methods for registering WatchForge NVR Client services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds WatchForge NVR Client services to the service collection.
    /// </summary>
    public static IServiceCollection AddWatchForgeNvrClient(
        this IServiceCollection services,
        OnvifClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<SimpleOnvifClient>(sp =>
        {
            var opts = sp.GetRequiredService<OnvifClientOptions>();
            return new SimpleOnvifClient(opts.EndpointUrl, opts.Username, opts.Password);
        });

        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IRecordingSearchService, RecordingSearchService>();
        services.AddSingleton<IEventService, EventService>();
        services.AddSingleton<IOnvifClient, OnvifClient>();

        return services;
    }

    /// <summary>
    /// Adds WatchForge NVR Client services with configuration action.
    /// </summary>
    public static IServiceCollection AddWatchForgeNvrClient(
        this IServiceCollection services,
        Action<OnvifClientOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new OnvifClientOptions();
        configureOptions(options);
        
        return services.AddWatchForgeNvrClient(options);
    }
}
