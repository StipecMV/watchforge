namespace WatchForge.MotionSentinel.Server.Core.Services;

/// <summary>Abstracts the system clock to make time-dependent code testable.</summary>
public interface IDateTimeProvider
{
    /// <summary>Returns the current UTC date and time.</summary>
    DateTimeOffset UtcNow { get; }
}
