using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Dispatch jobu na handler podľa typu. Handler vykoná prácu (môže priebežne
/// nastavovať job.Progress); o konečný stav (Completed/Failed) a persist sa
/// stará executor. Ak handler pre daný typ nie je registrovaný, job sa označí
/// ako Failed s jasnou správou (transparentné, nie tiché preskočenie).
/// </summary>
public sealed class JobExecutor(IEnumerable<IJobHandler> handlers, IJobRepository repository) : IJobExecutor
{
    private readonly Dictionary<JobType, IJobHandler> _handlers =
        handlers.ToDictionary(h => h.HandlesType);

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        if (_handlers.TryGetValue(job.Type, out var handler))
        {
            try
            {
                await handler.ExecuteAsync(job, ct);

                // Handler mohol rozhodnúť o stave sám (Queued = retry, Failed, Completed).
                // Ak nerozhodol (stále Running), job je hotový → Completed.
                if (job.Status == JobStatus.Running)
                {
                    job.Status = JobStatus.Completed;
                    job.Progress = 100;
                }
                if (job.Status != JobStatus.Queued)
                    job.FinishedAt = DateTime.UtcNow;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // S20 (FR-08): preempcia/shutdown — propagujeme nahor, RunnerService
                // rozhodne (preempt → interrupted + requeue; shutdown → recovery S4-7).
                // Job NESMIE byť Failed — prerušenie nie je chyba.
                throw;
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                job.FinishedAt = DateTime.UtcNow;
            }
            await repository.UpdateAsync(job, ct);
            return;
        }

        // Žiadny handler → Failed s dôvodom (job zostane v DB pre audit).
        job.Status = JobStatus.Failed;
        job.Error = $"No handler registered for job type '{job.Type}'.";
        job.FinishedAt = DateTime.UtcNow;
        await repository.UpdateAsync(job, ct);
    }
}
