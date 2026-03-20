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

        services.AddSingleton<IOnvifClientAdapter>(sp =>
        {
            var opts = sp.GetRequiredService<OnvifClientOptions>();
            return new OnvifClientAdapter(
                new SimpleOnvifClient(opts.EndpointUrl, opts.Username, opts.Password));
        });

        services.AddSingleton<IDeviceService>(sp =>
            new DeviceService(
                sp.GetRequiredService<IOnvifClientAdapter>(),
                sp.GetRequiredService<OnvifClientOptions>().Host));

        services.AddSingleton<IMediaService>(sp =>
            new MediaService(
                sp.GetRequiredService<IOnvifClientAdapter>(),
                sp.GetRequiredService<OnvifClientOptions>().Host));

        services.AddSingleton<IRecordingSearchService>(sp =>
            new RecordingSearchService(
                sp.GetRequiredService<IOnvifClientAdapter>(),
                sp.GetRequiredService<OnvifClientOptions>().Host));

        services.AddSingleton<IEventService>(sp =>
            new EventService(
                sp.GetRequiredService<IOnvifClientAdapter>(),
                sp.GetRequiredService<OnvifClientOptions>().Host));

        services.AddSingleton<IOnvifClient>(sp => new OnvifClient(
            sp.GetRequiredService<OnvifClientOptions>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<IMediaService>(),
            sp.GetRequiredService<IRecordingSearchService>(),
            sp.GetRequiredService<IEventService>()));

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
