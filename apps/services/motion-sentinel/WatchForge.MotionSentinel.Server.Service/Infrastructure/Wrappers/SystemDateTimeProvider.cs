namespace WatchForge.MotionSentinel.Server.Service.Infrastructure.Wrappers;

/// <summary>Production implementation of <see cref="IDateTimeProvider"/> that delegates to <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
