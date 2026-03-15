namespace WatchForge.NVR.Client.Core.Tests;

public class OnvifClientOptionsTests
{
    [Test]
    public async Task DefaultConstructor_SetsExpectedDefaultValues()
    {
        var options = new OnvifClientOptions();

        await Assert.That(options.Host).IsEqualTo(string.Empty);
        await Assert.That(options.Port).IsEqualTo(80);
        await Assert.That(options.Username).IsEqualTo(string.Empty);
        await Assert.That(options.Password).IsEqualTo(string.Empty);
        await Assert.That(options.ServicePath).IsEqualTo("/onvif/device_service");
        await Assert.That(options.UseHttps).IsFalse();
        await Assert.That(options.TimeoutSeconds).IsEqualTo(30);
    }

    [Test]
    public async Task EndpointUrl_WithHttp_BuildsCorrectUrl()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 80,
            ServicePath = "/onvif/device_service",
            UseHttps = false
        };

        await Assert.That(options.EndpointUrl).IsEqualTo("http://192.168.1.1:80/onvif/device_service");
    }

    [Test]
    public async Task EndpointUrl_WithHttps_BuildsCorrectUrl()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 443,
            ServicePath = "/onvif/device_service",
            UseHttps = true
        };

        await Assert.That(options.EndpointUrl).IsEqualTo("https://192.168.1.1:443/onvif/device_service");
    }

    [Test]
    public async Task EndpointUrl_WithCustomPort_BuildsCorrectUrl()
    {
        var options = new OnvifClientOptions
        {
            Host = "camera.local",
            Port = 8080,
            ServicePath = "/onvif/device_service",
            UseHttps = false
        };

        await Assert.That(options.EndpointUrl).IsEqualTo("http://camera.local:8080/onvif/device_service");
    }

    [Test]
    public async Task Validate_WithValidOptions_DoesNotThrow()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 80,
            Username = "admin",
            Password = "password"
        };

        await Assert.That(() => options.Validate()).ThrowsNothing();
    }

    [Test]
    public async Task Validate_WithEmptyHost_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = string.Empty,
            Port = 80,
            Username = "admin",
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Host is required");
    }

    [Test]
    public async Task Validate_WithWhitespaceHost_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "   ",
            Port = 80,
            Username = "admin",
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Host is required");
    }

    [Test]
    public async Task Validate_WithEmptyUsername_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 80,
            Username = string.Empty,
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Username is required");
    }

    [Test]
    public async Task Validate_WithNullPassword_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 80,
            Username = "admin",
            Password = null!
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Password is required");
    }

    [Test]
    public async Task Validate_WithPortZero_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 0,
            Username = "admin",
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Port must be between 1 and 65535");
    }

    [Test]
    public async Task Validate_WithNegativePort_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = -1,
            Username = "admin",
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Port must be between 1 and 65535");
    }

    [Test]
    public async Task Validate_WithPortAboveMaximum_ThrowsInvalidOperationException()
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = 65536,
            Username = "admin",
            Password = "password"
        };

        var ex = await Assert.That(() => options.Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(ex.Message).Contains("Port must be between 1 and 65535");
    }

    [Test]
    [Arguments(1)]
    [Arguments(65535)]
    public async Task Validate_WithBoundaryPort_DoesNotThrow(int port)
    {
        var options = new OnvifClientOptions
        {
            Host = "192.168.1.1",
            Port = port,
            Username = "admin",
            Password = "password"
        };

        await Assert.That(() => options.Validate()).ThrowsNothing();
    }

    [Test]
    public async Task Properties_CanBeSet_AndReadBack()
    {
        var options = new OnvifClientOptions();

        options.Host = "192.168.1.100";
        options.Port = 8080;
        options.Username = "admin";
        options.Password = "pass123";
        options.ServicePath = "/custom/path";
        options.UseHttps = true;
        options.TimeoutSeconds = 60;

        await Assert.That(options.Host).IsEqualTo("192.168.1.100");
        await Assert.That(options.Port).IsEqualTo(8080);
        await Assert.That(options.Username).IsEqualTo("admin");
        await Assert.That(options.Password).IsEqualTo("pass123");
        await Assert.That(options.ServicePath).IsEqualTo("/custom/path");
        await Assert.That(options.UseHttps).IsTrue();
        await Assert.That(options.TimeoutSeconds).IsEqualTo(60);
    }
}
