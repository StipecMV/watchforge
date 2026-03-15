namespace WatchForge.NVR.Client.Core.Tests;

public class ExceptionsTests
{
    [Test]
    public async Task WatchForgeNvrException_DefaultConstructor_CreatesInstance()
    {
        var ex = new WatchForgeNvrException();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex.Message).IsNotNull();
    }

    [Test]
    public async Task WatchForgeNvrException_WithMessage_SetsMessage()
    {
        var message = "Test error message";

        var ex = new WatchForgeNvrException(message);

        await Assert.That(ex.Message).IsEqualTo(message);
    }

    [Test]
    public async Task WatchForgeNvrException_WithMessageAndInnerException_SetsBoth()
    {
        var message = "Test error";
        var innerEx = new InvalidOperationException("Inner error");

        var ex = new WatchForgeNvrException(message, innerEx);

        await Assert.That(ex.Message).IsEqualTo(message);
        await Assert.That(ex.InnerException).IsEqualTo(innerEx);
    }

    [Test]
    public async Task OnvifConnectionException_Constructor_SetsHostAndMessage()
    {
        var host = "192.168.1.1";
        var message = "Connection failed";

        var ex = new OnvifConnectionException(host, message);

        await Assert.That(ex.Host).IsEqualTo(host);
        await Assert.That(ex.Message).IsEqualTo(message);
        await Assert.That(ex).IsAssignableTo<WatchForgeNvrException>();
    }

    [Test]
    public async Task OnvifConnectionException_WithInnerException_SetsAll()
    {
        var host = "192.168.1.1";
        var message = "Connection failed";
        var innerEx = new HttpRequestException("Network error");

        var ex = new OnvifConnectionException(host, message, innerEx);

        await Assert.That(ex.Host).IsEqualTo(host);
        await Assert.That(ex.Message).IsEqualTo(message);
        await Assert.That(ex.InnerException).IsEqualTo(innerEx);
    }

    [Test]
    public async Task OnvifAuthenticationException_Constructor_SetsUsernameAndMessage()
    {
        var username = "admin";
        var message = "Authentication failed";

        var ex = new OnvifAuthenticationException(username, message);

        await Assert.That(ex.Username).IsEqualTo(username);
        await Assert.That(ex.Message).IsEqualTo(message);
        await Assert.That(ex).IsAssignableTo<WatchForgeNvrException>();
    }

    [Test]
    public async Task OnvifAuthenticationException_WithInnerException_SetsAll()
    {
        var username = "admin";
        var message = "Authentication failed";
        var innerEx = new UnauthorizedAccessException("Invalid credentials");

        var ex = new OnvifAuthenticationException(username, message, innerEx);

        await Assert.That(ex.Username).IsEqualTo(username);
        await Assert.That(ex.Message).IsEqualTo(message);
        await Assert.That(ex.InnerException).IsEqualTo(innerEx);
    }

    [Test]
    public async Task OnvifServiceNotAvailableException_Constructor_SetsServiceNamespace()
    {
        var service = "http://www.onvif.org/ver20/recording/wsdl";

        var ex = new OnvifServiceNotAvailableException(service);

        await Assert.That(ex.ServiceNamespace).IsEqualTo(service);
        await Assert.That(ex.Message).Contains(service);
        await Assert.That(ex).IsAssignableTo<WatchForgeNvrException>();
    }

    [Test]
    public async Task MediaProfileNotFoundException_Constructor_SetsProfileToken()
    {
        var token = "profile_token_123";

        var ex = new MediaProfileNotFoundException(token);

        await Assert.That(ex.ProfileToken).IsEqualTo(token);
        await Assert.That(ex.Message).Contains(token);
        await Assert.That(ex).IsAssignableTo<WatchForgeNvrException>();
    }

    [Test]
    public async Task OnvifConnectionException_IsSubclassOfException()
    {
        var ex = new OnvifConnectionException("192.168.1.1", "Test message");

        await Assert.That(ex).IsAssignableTo<WatchForgeNvrException>();
        await Assert.That(ex).IsAssignableTo<Exception>();
    }
}
