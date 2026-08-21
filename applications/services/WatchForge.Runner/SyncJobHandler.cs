using System.Text.Json;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre JobType.Sync (S4-2): backlog synchronizácia nových nahrávok,
/// detekcia segmentov prepísaných počas výpadku a kontrola dostupnosti originálov.
/// Payload (voliteľný): JSON {"from":"yyyy-MM-ddTHH:mm:ssZ","to":"..."} — rozsah backlogu;
/// ak chýba, berie sa len RETENČNÉ OKNÁ (S22l: RetentionWindows × WindowMinutes + rezerva) —
/// staršie NVR záznamy sa ignorujú (lokálna cache = rolling okná).
/// </summary>
public sealed class SyncJobHandler(NvrSynchronizer synchronizer, IClock clock, RunnerOptions options) : IJobHandler
{
    public JobType HandlesType => JobType.Sync;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        var (from, to) = ParseRange(job.Payload, clock.UtcNow);

        var backlog = await synchronizer.SyncBacklogAsync(from, to, ct);
        job.Progress = 40;

        var missed = await synchronizer.DetectMissedDuringOutageAsync(from, to, ct);
        job.Progress = 70;

        var availability = await synchronizer.CheckAvailabilityAsync(clock.UtcNow, ct);
        job.Progress = 100;

        job.Error = $"inserted={backlog.Inserted}, unavailable={availability.Unavailable}, missed={missed.MissedDuringOutage}";
    }

    public (DateTime From, DateTime To) ParseRange(string? payload, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("from", out var fromEl) && root.TryGetProperty("to", out var toEl)
                    && fromEl.TryGetDateTime(out var from) && toEl.TryGetDateTime(out var to)
                    && to > from)
                {
                    return (from, to);
                }
            }
            catch (JsonException) { /* fallback na default */ }
        }

        var toDefault = now;
        // S22l: len retenčné okná (+ 1 okno rezerva na segmenty presahujúce hranicu)
        var fromDefault = now - TimeSpan.FromMinutes((options.RetentionWindows + 1) * options.WindowMinutes);
        return (fromDefault, toDefault);
    }
}
