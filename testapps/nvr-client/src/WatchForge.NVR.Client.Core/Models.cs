namespace WatchForge.NVR.Client.Core;

/// <summary>
/// Represents device information from an ONVIF device.
/// </summary>
public record DeviceInformation(
    string Manufacturer,
    string Model,
    string FirmwareVersion,
    string? SerialNumber = null,
    string? HardwareId = null
);

/// <summary>
/// Represents a media profile from an ONVIF device.
/// </summary>
public record MediaProfile(
    string Token,
    string Name,
    bool? IsFixed = null
);

/// <summary>
/// Represents a stream URI configuration.
/// </summary>
public record StreamUri(
    string Uri,
    string? InvalidAfterConnect = null,
    string? InvalidAfterReboot = null,
    string? Timeout = null
);

/// <summary>
/// Represents an ONVIF service endpoint.
/// </summary>
public record OnvifService(
    string Namespace,
    string XAddr,
    string? Version = null
);

/// <summary>
/// Represents a recording search result.
/// </summary>
public record Recording(
    string Token,
    string? Description = null,
    string? Source = null
);

/// <summary>
/// Represents a motion detection event.
/// </summary>
public record MotionEvent(
    DateTime Timestamp,
    bool IsMotion,
    string? Source = null,
    string? Description = null
);

/// <summary>
/// Stream types available for ONVIF streaming.
/// </summary>
public enum StreamType
{
    RTPUnicast,
    RTPMulticast
}

/// <summary>
/// Transport protocols available for ONVIF streaming.
/// </summary>
public enum TransportProtocol
{
    RTSP,
    RTP,
    UDP,
    TCP
}
