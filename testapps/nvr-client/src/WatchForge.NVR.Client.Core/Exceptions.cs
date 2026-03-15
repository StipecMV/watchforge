namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Base exception for WatchForge NVR Client operations.
/// </summary>
public class WatchForgeNvrException : Exception
{
    public WatchForgeNvrException() : base() { }
    public WatchForgeNvrException(string message) : base(message) { }
    public WatchForgeNvrException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when connection to an ONVIF device fails.
/// </summary>
public class OnvifConnectionException : WatchForgeNvrException
{
    public string Host { get; }

    public OnvifConnectionException(string host, string message) : base(message)
    {
        Host = host;
    }

    public OnvifConnectionException(string host, string message, Exception innerException) : base(message, innerException)
    {
        Host = host;
    }
}

/// <summary>
/// Exception thrown when authentication with an ONVIF device fails.
/// </summary>
public class OnvifAuthenticationException : WatchForgeNvrException
{
    public string Username { get; }

    public OnvifAuthenticationException(string username, string message) : base(message)
    {
        Username = username;
    }

    public OnvifAuthenticationException(string username, string message, Exception innerException) : base(message, innerException)
    {
        Username = username;
    }
}

/// <summary>
/// Exception thrown when an ONVIF service is not available.
/// </summary>
public class OnvifServiceNotAvailableException : WatchForgeNvrException
{
    public string ServiceNamespace { get; }

    public OnvifServiceNotAvailableException(string serviceNamespace) 
        : base($"The ONVIF service '{serviceNamespace}' is not available on this device.")
    {
        ServiceNamespace = serviceNamespace;
    }
}

/// <summary>
/// Exception thrown when a media profile is not found.
/// </summary>
public class MediaProfileNotFoundException : WatchForgeNvrException
{
    public string ProfileToken { get; }

    public MediaProfileNotFoundException(string profileToken) 
        : base($"Media profile with token '{profileToken}' was not found.")
    {
        ProfileToken = profileToken;
    }
}
