using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre jeden typ jobu (download, analyze, sync, purge, ...).
/// Handler je zodpovedný za konečný stav jobu (Completed / Failed).
/// </summary>
public interface IJobHandler
{
    JobType HandlesType { get; }

    Task ExecuteAsync(Job job, CancellationToken ct);
}
