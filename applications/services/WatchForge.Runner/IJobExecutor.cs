using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>Vykonáva joby (dispatch podľa typu).</summary>
public interface IJobExecutor
{
    Task ExecuteAsync(Job job, CancellationToken ct);
}
