namespace WatchForge.MotionSentinel.Server.Service.Infrastructure.Wrappers;

/// <summary>Production implementation of <see cref="IAppInfoProvider"/> that reads the entry-assembly version.</summary>
public sealed class AssemblyAppInfoProvider : IAppInfoProvider
{
    /// <inheritdoc/>
    public string Version =>
        typeof(AssemblyAppInfoProvider).Assembly
            .GetName().Version?.ToString() ?? "0.0.0";
}
