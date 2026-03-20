namespace WatchForge.MotionSentinel.Server.Core.Services;

/// <summary>Exposes application metadata for embedding in detection output.</summary>
public interface IAppInfoProvider
{
    /// <summary>Version string of the running application, e.g. <c>1.2.3.0</c>.</summary>
    string Version { get; }
}
