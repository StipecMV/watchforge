using System.Threading;

namespace WatchForge.Interfaces.Library;

/// <summary>Úložisko NVR zariadení (NVRS).</summary>
public interface INvrRepository
{
    Task<IReadOnlyList<Nvr>> GetAllAsync(CancellationToken ct = default);
    Task<bool> UpdateHostAsync(int nvrId, string host, CancellationToken ct = default);
}

/// <summary>Úložisko kamier (CAMERAS).</summary>
public interface ICameraRepository
{
    Task<IReadOnlyList<Camera>> GetAllAsync(int? nvrId = null, CancellationToken ct = default);

    /// <summary>
    /// Upraví profil kamery (S6-5 Settings → Cameras): friendly_name, icon_id, is_active.
    /// Channel/NvrId sú systémové identifikátory — nemenia sa. Vráti false, ak kamera neexistuje.
    /// </summary>
    Task<bool> UpdateAsync(Camera camera, CancellationToken ct = default);
}

/// <summary>Úložisko záznamov (RECORDINGS).</summary>
public interface IRecordingRepository
{
    Task<Recording?> GetByIdAsync(int recordingId, CancellationToken ct = default);
    Task<Recording?> GetByNvrFilenameAsync(string nvrFilename, CancellationToken ct = default);
    Task<IReadOnlyList<Recording>> QueryAsync(int? cameraId, DateTime? from, DateTime? to,
        string? sourceType, CancellationToken ct = default);
    Task<int> InsertAsync(Recording recording, CancellationToken ct = default);
    Task UpdateAsync(Recording recording, CancellationToken ct = default);
    Task<IReadOnlyList<Recording>> GetByAvailabilityAsync(string availability, CancellationToken ct = default);
    Task<IReadOnlyList<Recording>> GetExpiredForPurgeAsync(DateTime now, CancellationToken ct = default);
    /// <summary>S22l: recordings mimo rolling okien (begin_time < cutoff) — lokálna retencia.</summary>
    Task<IReadOnlyList<Recording>> GetExpiredForWindowRetentionAsync(DateTime cutoff, CancellationToken ct = default);
    Task DeleteAsync(int recordingId, CancellationToken ct = default);
    Task<int> CountBacklogAsync(CancellationToken ct = default);
    Task<(int Total, int Completed)> CountAllAndCompletedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Recording>> GetPendingAnalysisAsync(int limit, CancellationToken ct = default);
    Task<DateTime?> GetLastSyncAsync(CancellationToken ct = default);

    /// <summary>S22l: záznamy najnovšieho DOKONČENÉHO okna (15-min kvartál), naprieč kamerami.
    /// Okno = [windowStart, windowEnd); kamera offline v okne = nemá záznam.</summary>
    Task<IReadOnlyList<Recording>> GetLatestWindowAsync(int windowMinutes, DateTime now, int maxWindows,
        CancellationToken ct = default);

    /// <summary>S22l: analyzované okná (aspoň 1 detekcia) — zoznam pre MCP/UI „čo je k dispozícii".</summary>
    Task<IReadOnlyList<AnalyzedWindow>> GetAnalyzedWindowsAsync(int windowMinutes, DateTime now, int maxWindows,
        CancellationToken ct = default);

    /// <summary>S22l: nahrávky čakajúce na potvrdenie osoby (person_pending=1) — pre UI/MCP „skontrolované".</summary>
    Task<IReadOnlyList<Recording>> GetPersonPendingAsync(CancellationToken ct = default);
}

/// <summary>S22l: analyzované 15-min okno (kvartál) — zhrnutie pre MCP/UI.</summary>
public sealed record AnalyzedWindow(
    DateTime WindowStart, DateTime WindowEnd,
    int CamerasAnalyzed, int TotalDetections, bool PersonFound);

/// <summary>Úložisko detekcií (DETECTIONS, ANNOTATIONS).</summary>
/// <summary>Repozitár pre identity (FR-05 — databáza tvárí).</summary>
public interface IIdentityRepository
{
    Task<IReadOnlyList<Identity>> GetAllAsync(CancellationToken ct = default);
    Task<Identity?> GetByIdAsync(int identityId, CancellationToken ct = default);
    Task<int> CreateAsync(string name, int? createdBy, CancellationToken ct = default);
    Task<bool> RenameAsync(int identityId, string name, CancellationToken ct = default);
    Task<bool> DeleteAsync(int identityId, CancellationToken ct = default);
}

/// <summary>Repozitár pre face records (FR-05 — embeddings perzistencia).</summary>
public interface IFaceRepository
{
    Task<int> InsertAsync(FaceRecord face, CancellationToken ct = default);
    Task<IReadOnlyList<FaceRecord>> GetByDetectionAsync(int detectionId, CancellationToken ct = default);
    Task<IReadOnlyList<FaceRecord>> GetByIdentityAsync(int identityId, CancellationToken ct = default);
    Task<IReadOnlyList<FaceRecord>> GetAllWithIdentityAsync(CancellationToken ct = default);
    Task<bool> AssignIdentityAsync(int faceId, int? identityId, CancellationToken ct = default);
    Task DeleteByIdentityAsync(int identityId, CancellationToken ct = default);
}

public interface IDetectionRepository
{
    Task<int> InsertAsync(Detection detection, CancellationToken ct = default);
    Task InsertManyAsync(IReadOnlyList<Detection> detections, CancellationToken ct = default);
    Task<IReadOnlyList<Detection>> QueryAsync(int? cameraId, DateTime? from, DateTime? to,
        string? detectionType, string? flag, CancellationToken ct = default);
    Task<int> CountAsync(int? cameraId, DateTime? from, DateTime? to,
        string? detectionType, string? flag, CancellationToken ct = default);
    Task<IReadOnlyList<Detection>> GetByRecordingAsync(int recordingId, CancellationToken ct = default);
    Task SetFlagAsync(int detectionId, string flag, int? userId, DateTime flaggedAt, CancellationToken ct = default);
    Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default);
}

/// <summary>Úložisko jobov (JOBS) — job orchestration.</summary>
public interface IJobRepository
{
    Task<Job> EnqueueAsync(Job job, CancellationToken ct = default);
    Task<Job?> ClaimNextAsync(int maxPriority = int.MaxValue, CancellationToken ct = default);

    /// <summary>
    /// Claimne najvyššiu prioritu jobu DANÉHO typu (worker sloty: analyze vs download).
    /// Ak pre daný typ nie je žiadny queued job, vráti null.
    /// </summary>
    Task<Job?> ClaimNextByTypeAsync(JobType type, int maxPriority = int.MaxValue, CancellationToken ct = default);

    Task<Job?> GetByIdAsync(int jobId, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetRunningAsync(CancellationToken ct = default);
    Task<bool> HasActiveJobAsync(JobType type, CancellationToken ct = default);
    /// <summary>S22l: vymaže joby patriace záznamu (pre purge — FK mazanie pred recordings).</summary>
    Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default);
    Task<DateTime?> GetLastCompletedAtAsync(JobType type, CancellationToken ct = default);

    /// <summary>
    /// S20 (FR-08): pozrie najvyššiu prioritu queued jobu DANÉHO typu BEZ claimu
    /// (žiadna zmena stavu) — pre rozhodnutie o preempcii v RunnerService.
    /// </summary>
    Task<Job?> PeekNextByTypeAsync(JobType type, CancellationToken ct = default);

    /// <summary>S20 (FR-08): preempcia — jeden prerušený job sa vráti do frontu (interrupted → queued).</summary>
    Task RequeueInterruptedJobAsync(int jobId, CancellationToken ct = default);

    /// <summary>Počty jobov podľa stavu (pre /internal/jobs a web UI).</summary>
    Task<JobStatusSummary> GetStatusSummaryAsync(CancellationToken ct = default);

    Task MarkInterruptedAsync(CancellationToken ct = default);
    Task RequeueInterruptedAsync(CancellationToken ct = default);
}

/// <summary>Počty jobov podľa stavu.</summary>
public sealed record JobStatusSummary(int Queued, int Running, int Completed, int Failed, int Interrupted, int Cancelled)
{
    public int Total => Queued + Running + Completed + Failed + Interrupted + Cancelled;
}

/// <summary>Úložisko verzií konfigurácie (CONFIG_VERSIONS).</summary>
public interface IConfigVersionRepository
{
    Task<ConfigVersion?> GetActiveForCameraAsync(int cameraId, CancellationToken ct = default);
    Task<ConfigVersion?> GetActiveSharedAsync(CancellationToken ct = default);
    Task<int> InsertAsync(ConfigVersion version, CancellationToken ct = default);
    Task DeactivateAsync(int configVersionId, CancellationToken ct = default);
}

/// <summary>Úložisko používateľov (USERS).</summary>
public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task<int> InsertAsync(User user, CancellationToken ct = default);
    Task UpdatePasswordHashAsync(int userId, string passwordHash, CancellationToken ct = default);
    Task ResetPasswordAsync(int userId, CancellationToken ct = default);

    /// <summary>Upraví profil používateľa (S6-5 Settings → Profile): avatar_id + locale.</summary>
    Task UpdateProfileAsync(int userId, int avatarId, string locale, CancellationToken ct = default);
}

/// <summary>Úložisko vygenerovaných klipov (CLIPS) — pre retention/cleanup (S4-5).</summary>
public interface IClipRepository
{
    Task<IReadOnlyList<Clip>> GetExpiredAsync(DateTime now, CancellationToken ct = default);
    Task<IReadOnlyList<Clip>> GetByRequestAsync(int requestId, CancellationToken ct = default);
    /// <summary>S22l: clipy patriace záznamu (pre purge — FK mazanie pred recordings).</summary>
    Task<IReadOnlyList<Clip>> GetByRecordingAsync(int recordingId, CancellationToken ct = default);
    Task<Clip?> GetByIdAsync(int clipId, CancellationToken ct = default);
    Task<int> InsertAsync(Clip clip, CancellationToken ct = default);
    Task DeleteAsync(int clipId, CancellationToken ct = default);
}

/// <summary>Úložisko interaktívnych požiadaviek (REQUESTS) — S5-4.</summary>
public interface IRequestRepository
{
    Task<Request> InsertAsync(Request request, CancellationToken ct = default);
    Task<Request?> GetByIdAsync(int requestId, CancellationToken ct = default);
    Task UpdateAsync(Request request, CancellationToken ct = default);
}

/// <summary>Úložisko persist flagov (PERSIST_FLAGS) — S5-6.</summary>
public interface IPersistRepository
{
    Task<int> InsertAsync(PersistFlag flag, CancellationToken ct = default);
    Task RemoveAsync(int recordingId, CancellationToken ct = default);
    Task<IReadOnlyList<PersistFlag>> GetActiveByRecordingAsync(int recordingId, CancellationToken ct = default);
}

/// <summary>Úložisko anotácií (ANNOTATIONS) — S5-6, rozšírené v S6-7 (clear/update).</summary>
public interface IAnnotationRepository
{
    Task<int> InsertAsync(Annotation annotation, CancellationToken ct = default);
    Task<IReadOnlyList<Annotation>> GetByDetectionAsync(int detectionId, CancellationToken ct = default);
    Task<Annotation?> GetByIdAsync(int annotationId, CancellationToken ct = default);
    /// <summary>Updatuje región + label anotácie (vlastníctvo kontroluje volajúci).</summary>
    Task<bool> UpdateAsync(Annotation annotation, CancellationToken ct = default);
    /// <summary>Zmäže všetky anotácie daného používateľa pre detekciu („Clear my drawings", FR-16).</summary>
    Task<int> DeleteByDetectionAndUserAsync(int detectionId, int userId, CancellationToken ct = default);
}
