namespace WatchForge.DVRIP.Library.Models;

/// <summary>
/// Výsledok parsovania názvu nahrávky z NVR (S3-1).
/// </summary>
public readonly record struct NvrFileName(string Channel, DateTime BeginTime, DateTime EndTime)
{
    public static NvrFileName Invalid => new("", DateTime.MinValue, DateTime.MinValue);

    public bool IsValid => Channel is { Length: > 0 } && BeginTime != DateTime.MinValue && EndTime > BeginTime;

    public TimeSpan Duration => EndTime - BeginTime;
}
