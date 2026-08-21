using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// Handler pre JobType.Purge (S4-5, S22l) — retention/cleanup:
/// 1. expirované klipy (CLIPS) — súbor + DB riadok,
/// 2. lokálne videá + DB záznamy staršie ako retention okná (default 4×15 min = 60 min),
///    persist (persisted=1) a person_pending=1 (skip pre cleanup) sa NIKDY automaticky nemažú,
/// 3. zvyšky .downloading staršie ako 24 h (media cache cleanup).
/// Video aj DB záznamy idú spolu — konzistentne s oknami.
/// </summary>
public sealed class PurgeJobHandler(
    IRecordingRepository recordingRepository,
    IDetectionRepository detectionRepository,
    IClipRepository clipRepository,
    IJobRepository jobRepository,
    IClock clock,
    DownloadOptions downloadOptions) : IJobHandler
{
    /// <summary>Vek .downloading súboru, po ktorom sa považuje za zvyšok (raw HEVC po konverzii).</summary>
    public static readonly TimeSpan StaleDownloadMaxAge = TimeSpan.FromHours(24);

    /// <summary>Dĺžka okna (default 15 min).</summary>
    public int WindowMinutes { get; init; } = 15;

    /// <summary>Počet okien držaných lokálne (default 4 = 60 min; pribudnú disky → zvýšiť).</summary>
    public int RetentionWindows { get; init; } = 4;

    public JobType HandlesType => JobType.Purge;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        var now = clock.UtcNow;
        int purgedRecordings = 0, purgedClips = 0, deletedFiles = 0;

        // 1. Expirované klipy — súbor + riadok
        foreach (var clip in await clipRepository.GetExpiredAsync(now, ct))
        {
            if (DeleteFileIfExists(clip.FilePath)) deletedFiles++;
            await clipRepository.DeleteAsync(clip.ClipId, ct);
            purgedClips++;
        }

        // 2. Lokálne videá + DB záznamy staršie ako retention okná.
        //    Hranica = now - (RetentionWindows × WindowMinutes). Nahrávky začínajúce pred
        //    hranicou sa mažú (vrátane detekcií) — POKIAĽ nie sú persisted alebo person_pending.
        var cutoff = now.AddMinutes(-RetentionWindows * WindowMinutes);
        foreach (var recording in await recordingRepository.GetExpiredForWindowRetentionAsync(cutoff, ct))
        {
            // persist (polícia) + person_pending (skip pre cleanup) → chránené
            if (recording.Persisted || recording.PersonPending) continue;

            // lokálne video (temp) — ak existuje, zmaž
            if (!string.IsNullOrWhiteSpace(recording.NvrFilename))
            {
                var tempFile = Path.Combine(downloadOptions.TempDir, Path.GetFileName(recording.NvrFilename) + ".mp4");
                if (DeleteFileIfExists(tempFile)) deletedFiles++;
            }

            // clipy (FK na RECORDINGS) — súbor + riadky, inak by purge padol na FK constraint
            foreach (var clip in await clipRepository.GetByRecordingAsync(recording.RecordingId, ct))
            {
                if (DeleteFileIfExists(clip.FilePath)) deletedFiles++;
                await clipRepository.DeleteAsync(clip.ClipId, ct);
                purgedClips++;
            }

            // joby (FK na RECORDINGS) — analyze/klip joby patriace záznamu
            await jobRepository.DeleteForRecordingAsync(recording.RecordingId, ct);

            await detectionRepository.DeleteForRecordingAsync(recording.RecordingId, ct); // + anotácie
            await recordingRepository.DeleteAsync(recording.RecordingId, ct);
            purgedRecordings++;
        }

        // 3. Zvyšky .downloading v media cache (staršie ako 24 h — raw HEVC po konverzii)
        if (Directory.Exists(downloadOptions.TempDir))
        {
            foreach (var file in Directory.EnumerateFiles(downloadOptions.TempDir, "*.downloading"))
            {
                try
                {
                    if (now - File.GetLastWriteTimeUtc(file) > StaleDownloadMaxAge && DeleteFileIfExists(file))
                        deletedFiles++;
                }
                catch (IOException) { /* súbor práve píše worker — preskoč */ }
            }
        }

        job.Progress = 100;
        job.Error = $"recordings={purgedRecordings}, clips={purgedClips}, files={deletedFiles}";
    }

    private static bool DeleteFileIfExists(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
    }
}
