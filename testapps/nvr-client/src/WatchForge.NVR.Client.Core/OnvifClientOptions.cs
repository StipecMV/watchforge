namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Configuration options for connecting to an ONVIF device.
/// </summary>
public class OnvifClientOptions
{
    /// <summary>
    /// The host address of the ONVIF device (IP or hostname).
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The port number for ONVIF communication (default: 80).
    /// </summary>
    public int Port { get; set; } = 80;

    /// <summary>
    /// The username for authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password for authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The ONVIF service endpoint path (default: /onvif/device_service).
    /// </summary>
    public string ServicePath { get; set; } = "/onvif/device_service";

    /// <summary>
    /// Whether to use HTTPS instead of HTTP.
    /// </summary>
    public bool UseHttps { get; set; } = false;

    /// <summary>
    /// Connection timeout in seconds (default: 30).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets the full endpoint URL.
    /// </summary>
    public string EndpointUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{Port}{ServicePath}";

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("Host is required.");

        if (string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("Username is required.");

        if (Password is null)
            throw new InvalidOperationException("Password is required.");

        if (Port <= 0 || Port > 65535)
            throw new InvalidOperationException("Port must be between 1 and 65535.");
    }
}
