namespace WatchForge.NVR.Client.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    private static OnvifClientOptions ValidOptions() => new()
    {
        Host = "192.168.1.1",
        Port = 80,
        Username = "admin",
        Password = "password"
    };

    [Test]
    public async Task AddWatchForgeNvrClient_WithOptions_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddWatchForgeNvrClient(ValidOptions());
        var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetService<IOnvifClient>()).IsNotNull();
        await Assert.That(provider.GetService<IDeviceService>()).IsNotNull();
        await Assert.That(provider.GetService<IMediaService>()).IsNotNull();
        await Assert.That(provider.GetService<IRecordingSearchService>()).IsNotNull();
        await Assert.That(provider.GetService<IEventService>()).IsNotNull();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        await Assert.That(() => services.AddWatchForgeNvrClient(ValidOptions()))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithNullOptions_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddWatchForgeNvrClient((OnvifClientOptions)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithInvalidOptions_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var options = new OnvifClientOptions { Host = string.Empty, Port = 80, Username = "admin", Password = "password" };

        await Assert.That(() => services.AddWatchForgeNvrClient(options))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithConfigureAction_RegistersIOnvifClient()
    {
        var services = new ServiceCollection();

        services.AddWatchForgeNvrClient(opts =>
        {
            opts.Host = "192.168.1.1";
            opts.Port = 80;
            opts.Username = "admin";
            opts.Password = "password";
        });

        var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetService<IOnvifClient>()).IsNotNull();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithNullServiceCollection_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        await Assert.That(() => services.AddWatchForgeNvrClient(opts => { }))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithNullAction_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddWatchForgeNvrClient((Action<OnvifClientOptions>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithInvalidConfigureAction_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddWatchForgeNvrClient(opts => { opts.Host = string.Empty; }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_RegistersServicesAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddWatchForgeNvrClient(ValidOptions());
        var provider = services.BuildServiceProvider();

        var client1 = provider.GetService<IOnvifClient>();
        var client2 = provider.GetService<IOnvifClient>();

        await Assert.That(client1).IsSameReferenceAs(client2);
    }

    [Test]
    public async Task AddWatchForgeNvrClient_RegistersOptions()
    {
        var services = new ServiceCollection();
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 8080,
            Username = "admin",
            Password = "password",
            UseHttps = true,
            TimeoutSeconds = 60
        };

        services.AddWatchForgeNvrClient(options);
        var provider = services.BuildServiceProvider();

        var retrieved = provider.GetService<OnvifClientOptions>();
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved!.Host).IsEqualTo("192.168.1.1");
        await Assert.That(retrieved.Port).IsEqualTo(8080);
        await Assert.That(retrieved.UseHttps).IsTrue();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddWatchForgeNvrClient(ValidOptions());

        await Assert.That(result).IsSameReferenceAs(services);
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithConfigureAction_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddWatchForgeNvrClient(opts =>
        {
            opts.Host = "192.168.1.1";
            opts.Port = 80;
            opts.Username = "admin";
            opts.Password = "password";
        });

        await Assert.That(result).IsSameReferenceAs(services);
    }

    [Test]
    public async Task AddWatchForgeNvrClient_AllServiceTypesAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddWatchForgeNvrClient(ValidOptions());
        var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetService(typeof(IOnvifClient))).IsNotNull();
        await Assert.That(provider.GetService(typeof(IDeviceService))).IsNotNull();
        await Assert.That(provider.GetService(typeof(IMediaService))).IsNotNull();
        await Assert.That(provider.GetService(typeof(IRecordingSearchService))).IsNotNull();
        await Assert.That(provider.GetService(typeof(IEventService))).IsNotNull();
        await Assert.That(provider.GetService(typeof(OnvifClientOptions))).IsNotNull();
    }

    [Test]
    public async Task AddWatchForgeNvrClient_CalledTwice_LastRegistrationWins()
    {
        var services = new ServiceCollection();
        var options1 = new OnvifClientOptions { Host = "192.168.1.1", Port = 80, Username = "admin", Password = "password" };
        var options2 = new OnvifClientOptions { Host = "192.168.1.2", Port = 8080, Username = "admin", Password = "password" };

        services.AddWatchForgeNvrClient(options1);
        services.AddWatchForgeNvrClient(options2);

        var provider = services.BuildServiceProvider();
        var retrieved = provider.GetService<OnvifClientOptions>();
        await Assert.That(retrieved!.Host).IsEqualTo("192.168.1.2");
        await Assert.That(retrieved.Port).IsEqualTo(8080);
    }

    [Test]
    public async Task AddWatchForgeNvrClient_IOnvifClientDevice_IsSameInstanceAs_IDeviceService()
    {
        // Verifies the DI graph is unified — IOnvifClient.Device must be the same
        // singleton that is also registered as IDeviceService, not a second copy.
        var services = new ServiceCollection();
        services.AddWatchForgeNvrClient(ValidOptions());
        var provider = services.BuildServiceProvider();

        var client        = provider.GetRequiredService<IOnvifClient>();
        var deviceService = provider.GetRequiredService<IDeviceService>();

        await Assert.That(client.Device).IsSameReferenceAs(deviceService);
    }

    [Test]
    public async Task AddWatchForgeNvrClient_WithConfigureAction_AppliesAllOptions()
    {
        var services = new ServiceCollection();

        services.AddWatchForgeNvrClient(opts =>
        {
            opts.Host = "camera.local";
            opts.Port = 443;
            opts.Username = "user";
            opts.Password = "pass";
            opts.UseHttps = true;
            opts.TimeoutSeconds = 120;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<OnvifClientOptions>();

        await Assert.That(options!.Host).IsEqualTo("camera.local");
        await Assert.That(options.Port).IsEqualTo(443);
        await Assert.That(options.Username).IsEqualTo("user");
        await Assert.That(options.Password).IsEqualTo("pass");
        await Assert.That(options.UseHttps).IsTrue();
        await Assert.That(options.TimeoutSeconds).IsEqualTo(120);
    }
}
