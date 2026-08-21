using WatchForge.DVRIP.Library;
using WatchForge.DVRIP.Library.Models;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Výsledok synchronizačného behu (S4-2).
/// </summary>
public sealed record SyncResult(int Inserted, int Unavailable, int MissedDuringOutage);

/// <summary>
/// Synchronizácia s NVR (S4-2):
/// - <see cref="SyncBacklogAsync"/> — dobehne nové nahrávky z NVR do RECORDINGS (idempotentne),
/// - <see cref="CheckAvailabilityAsync"/> — označí záznamy, ktoré NVR už nemá (unavailable),
/// - <see cref="DetectMissedDuringOutageAsync"/> — označí segmenty, ktoré prepísal NVR
///   počas výpadku systému (missed_during_outage — nikdy sa nebudú analyzovať).
/// NVR a kamery sa čítajú z DB (NVRS, CAMERAS); heslo NVR cez secret env var.
/// </summary>
#pragma warning disable CS9113 // clock je DI infraštruktúra (rezerva)
public sealed class NvrSynchronizer(
    INvrRepository nvrRepository,
    ICameraRepository cameraRepository,
    IRecordingRepository recordingRepository,
    IClock clock,
    Func<DvripClientOptions, IDvripClient> clientFactory)
#pragma warning restore CS9113
{
    /// <summary>Retencia pre unavailable záznamy (default 30 dní — čaká na potvrdenie).</summary>
    public TimeSpan UnavailableRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Hranica segment vs event klip (kratšie = event klip).</summary>
    public static readonly TimeSpan EventClipThreshold = TimeSpan.FromMinutes(10);

    /// <summary>Dĺžka očakávaného segmentu pre missed detection.</summary>
    public static readonly TimeSpan SegmentLength = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Dobehne nové nahrávky z NVR (backlog). Pre každú aktívnu kameru query na [from,to],
    /// insert idempotentne podľa nvr_filename. Existujúce záznamy sa nemenia.
    /// </summary>
    public async Task<SyncResult> SyncBacklogAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var inserted = 0;
        foreach (var nvr in await nvrRepository.GetAllAsync(ct))
        {
            var password = ResolvePassword(nvr);
            if (password is null) continue;

            foreach (var camera in (await cameraRepository.GetAllAsync(nvr.NvrId, ct)).Where(c => c.IsActive))
            {
                using var client = clientFactory(new DvripClientOptions
                {
                    Host = nvr.Host, Port = nvr.Port, Username = nvr.Username, Password = password
                });
                await client.LoginAsync(ct);
                var files = await client.QueryFilesAsync(from, to, camera.Channel, ct);
                foreach (var file in files)
                {
                    var recording = MapToRecording(file, nvr.NvrId, camera.CameraId);
                    if (recording is null) continue;
                    if (await recordingRepository.GetByNvrFilenameAsync(file.FileName, ct) is null)
                        inserted++;
                    await recordingRepository.InsertAsync(recording, ct);
                }
            }
        }
        return new SyncResult(inserted, 0, 0);
    }

    /// <summary>
    /// Skontroluje dostupnosť originálov na NVR (každohodinová synchronizácia).
    /// Záznamy, ktoré NVR už nemá, sa označia unavailable + unavailable_since + purge_at.
    /// </summary>
    public async Task<SyncResult> CheckAvailabilityAsync(DateTime now, CancellationToken ct = default)
    {
        var unavailable = 0;
        foreach (var nvr in await nvrRepository.GetAllAsync(ct))
        {
            var password = ResolvePassword(nvr);
            if (password is null) continue;

            foreach (var camera in (await cameraRepository.GetAllAsync(nvr.NvrId, ct)).Where(c => c.IsActive))
            {
                var availableRecordings = (await recordingRepository.QueryAsync(
                    camera.CameraId, from: null, to: null, sourceType: null, ct))
                    .Where(r => r.Availability == "available")
                    .ToList();
                if (availableRecordings.Count == 0) continue;

                var from = availableRecordings.Min(r => r.BeginTime);
                var to = availableRecordings.Max(r => r.EndTime);

                using var client = clientFactory(new DvripClientOptions
                {
                    Host = nvr.Host, Port = nvr.Port, Username = nvr.Username, Password = password
                });
                await client.LoginAsync(ct);
                var nvrFiles = await client.QueryFilesAsync(from, to, camera.Channel, ct);
                var nvrNames = nvrFiles.Select(f => f.FileName).ToHashSet();

                foreach (var recording in availableRecordings)
                {
                    if (nvrNames.Contains(recording.NvrFilename)) continue;

                    recording.Availability = "unavailable";
                    recording.UnavailableSince = now;
                    recording.PurgeAt = now + UnavailableRetention;
                    await recordingRepository.UpdateAsync(recording, ct);
                    unavailable++;
                }
            }
        }
        return new SyncResult(0, unavailable, 0);
    }

    /// <summary>
    /// Označí segmenty, ktoré NVR prepísal počas výpadku systému. Pre každú kameru:
    /// ak NVR v danom okne naozaj nahrával (aspoň 1 záznam), pre každý očakávaný
    /// 15-min slot, ktorý neprekrýva žiadny NVR záznam a nie je v DB → missed_during_outage.
    /// </summary>
    public async Task<SyncResult> DetectMissedDuringOutageAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var missed = 0;
        foreach (var nvr in await nvrRepository.GetAllAsync(ct))
        {
            var password = ResolvePassword(nvr);
            if (password is null) continue;

            foreach (var camera in (await cameraRepository.GetAllAsync(nvr.NvrId, ct)).Where(c => c.IsActive))
            {
                using var client = clientFactory(new DvripClientOptions
                {
                    Host = nvr.Host, Port = nvr.Port, Username = nvr.Username, Password = password
                });
                await client.LoginAsync(ct);
                var files = await client.QueryFilesAsync(from, to, camera.Channel, ct);
                if (files.Count == 0) continue; // NVR v tomto okne nenahrával → nie je čo označiť

                // Missed hľadáme len v rámci aktívneho okna NVR (medzi prvým a posledným záznamom),
                // nie za okrajmi (tam NVR jednoducho nenahrával).
                var gridFrom = files.Min(f => f.BeginTime);
                var gridTo = files.Max(f => f.EndTime);

                var known = (await recordingRepository.QueryAsync(camera.CameraId, from, to, null, ct))
                    .Where(r => r.Availability != "missed_during_outage")
                    .ToList();

                for (var slotStart = TruncateToSegment(gridFrom); slotStart < gridTo; slotStart += SegmentLength)
                {
                    var slotEnd = slotStart + SegmentLength;
                    if (slotEnd <= gridFrom) continue;

                    var coveredByNvr = files.Any(f => Overlaps(f, slotStart, slotEnd));
                    var coveredByDb = known.Any(r => r.BeginTime < slotEnd && r.EndTime > slotStart);
                    if (coveredByNvr || coveredByDb) continue;

                    await recordingRepository.InsertAsync(new Recording
                    {
                        NvrId = nvr.NvrId,
                        CameraId = camera.CameraId,
                        SourceType = "segment",
                        NvrFilename = $"[Ch{camera.Channel}]_{slotStart:yyyy-MM-dd_HH.mm.ss}-{slotEnd:HH.mm}.mkv",
                        BeginTime = slotStart,
                        EndTime = slotEnd,
                        DurationSec = (int)SegmentLength.TotalSeconds,
                        Availability = "missed_during_outage",
                        AnalysisState = "skipped",
                    }, ct);
                    missed++;
                }
            }
        }
        return new SyncResult(0, 0, missed);
    }

    private static Recording? MapToRecording(NvrFile file, int nvrId, int cameraId)
    {
        if (file.FileLengthBytes <= 0) return null;

        var duration = file.EndTime - file.BeginTime;
        if (duration <= TimeSpan.Zero) return null;

        return new Recording
        {
            NvrId = nvrId,
            CameraId = cameraId,
            SourceType = duration < EventClipThreshold ? "event_clip" : "segment",
            NvrFilename = file.FileName,
            BeginTime = file.BeginTime,
            EndTime = file.EndTime,
            DurationSec = (int)duration.TotalSeconds,
            SizeBytes = file.FileLengthBytes,
            Codec = "hevc",
            Width = 3840,
            Height = 2160,
            Availability = "available",
            AnalysisState = "queued",
        };
    }

    private static bool Overlaps(NvrFile f, DateTime slotStart, DateTime slotEnd)
        => f.BeginTime < slotEnd && f.EndTime > slotStart;

    private static DateTime TruncateToSegment(DateTime t)
    {
        var minute = (t.Minute / 15) * 15;
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, minute, 0, t.Kind);
    }

    private static string? ResolvePassword(Nvr nvr)
        => string.IsNullOrEmpty(nvr.PasswordSecretEnv) ? null : Environment.GetEnvironmentVariable(nvr.PasswordSecretEnv);
}
