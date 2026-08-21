namespace WatchForge.Interfaces.Library;

/// <summary>NVR zariadenie (NVRS tabuľka).</summary>
public sealed class Nvr
{
    public int NvrId { get; set; }
    public string SiteId { get; set; } = "site-a";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Username { get; set; } = "";
    public string PasswordSecretEnv { get; set; } = "";
}

/// <summary>Kamera pripojená k NVR (CAMERAS tabuľka).</summary>
public sealed class Camera
{
    public int CameraId { get; set; }
    public int NvrId { get; set; }
    public int Channel { get; set; }
    public string FriendlyName { get; set; } = "";
    public string IconId { get; set; } = "camera";
    public bool IsActive { get; set; } = true;
}

/// <summary>Záznam (segment/event klip) z NVR.</summary>
public sealed class Recording
{
    public int RecordingId { get; set; }
    public int NvrId { get; set; }
    public int CameraId { get; set; }
    public string SourceType { get; set; } = "segment"; // segment | event_clip
    public string NvrFilename { get; set; } = "";
    public DateTime BeginTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationSec { get; set; }
    public long SizeBytes { get; set; }
    public string Codec { get; set; } = "hevc";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Availability { get; set; } = "available"; // available | unavailable | missed_during_outage
    public DateTime? UnavailableSince { get; set; }
    public DateTime? PurgeAt { get; set; }
    public bool Persisted { get; set; }
    /// <summary>S22l: analýza našla osobu — nahrávka je chránená pred purge (skip pre cleanup),
    /// kým užívateľ neoznačí „skontrolované" (UI/MCP).</summary>
    public bool PersonPending { get; set; }
    /// <summary>S22n: začiatok 15-min okna, na ktoré sa segment oreže (pre UI sync udalostí
    /// s prehrávaním trimnutého videa; null = celý segment bez orezania).</summary>
    public DateTime? WindowStartUtc { get; set; }
    public int? ConfigVersionId { get; set; }
    public string AnalysisState { get; set; } = "queued"; // queued|downloading|analyzing|completed|failed|interrupted
    public DateTime? AnalysisStartedAt { get; set; }
    public DateTime? AnalysisCompletedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>Detekcia (motion/person/vehicle/animal/face).</summary>
public sealed class Detection
{
    public int DetectionId { get; set; }
    public int RecordingId { get; set; }
    public int CameraId { get; set; }
    public string DetectionType { get; set; } = "motion";
    public int TimestampMs { get; set; }
    public int DurationMs { get; set; }
    public float Confidence { get; set; }
    public string AlgorithmVersion { get; set; } = "";
    public int? ConfigVersionId { get; set; }
    public NormalizedRegion Region { get; set; }
    public float Intensity { get; set; }
    public string ObjectClass { get; set; } = "";
    public string Flag { get; set; } = "none"; // none | flagged | false_positive
    public int? FlaggedBy { get; set; }
    public DateTime? FlaggedAt { get; set; }
}

/// <summary>Identita človeka (FR-05 — databáza tvárí).</summary>
public sealed class Identity
{
    public int IdentityId { get; set; }
    public string Name { get; set; } = "";
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Rozpoznaná tvár naviazaná na detekciu (FR-05 — embeddings perzistencia).</summary>
public sealed class FaceRecord
{
    public int FaceId { get; set; }
    public int DetectionId { get; set; }
    public int? IdentityId { get; set; }
    public byte[]? Embedding { get; set; }
    public double Confidence { get; set; }
    public string TempCropPath { get; set; } = "";
}

/// <summary>Job (jednotka práce — download, analyze, clip_extract, sync, purge).</summary>
public sealed class Job
{
    public int JobId { get; set; }
    public int? RecordingId { get; set; }
    public int? RequestId { get; set; }
    public JobType Type { get; set; }
    public int Priority { get; set; }
    public JobSource Source { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string? Payload { get; set; }
    public int Progress { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>Používateľ web UI (FR-12).</summary>
public sealed class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";        // fixný, unikátny
    public string PasswordHash { get; set; } = "";    // prázdny = ešte nenastavené heslo
    public string Role { get; set; } = "standard";    // admin | standard
    public int AvatarId { get; set; }
    public string Locale { get; set; } = "sk";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Verzia konfigurácie profilu (FR-06).</summary>
public sealed class ConfigVersion
{
    public int ConfigVersionId { get; set; }
    public int? CameraId { get; set; }               // null = zdieľaný profil
    public string ProfileName { get; set; } = "default";
    public string ProfileType { get; set; } = "per_camera";
    public float Sensitivity { get; set; } = 0.5f;
    public float IntensityThreshold { get; set; } = 0.02f;
    public float MinContourArea { get; set; } = 0.001f;
    public string IgnoreZonesJson { get; set; } = "[]"; // JSON [{x,y,w,h,shape}]
    public string FocusZonesJson { get; set; } = "[]";  // JSON [{x,y,w,h,shape}]
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Vygenerovaný klip (CLIPS) — dočasné video/fotka pre doručenie (S4-5).</summary>
public sealed class Clip
{
    public int ClipId { get; set; }
    public int RequestId { get; set; }
    public int? RecordingId { get; set; }
    public DateTime RangeStart { get; set; }
    public DateTime RangeEnd { get; set; }
    public string FilePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = "video"; // video | photo
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Interaktívna požiadavka (REQUESTS) — „hľadaj pohyb 14:00–16:00" (S5-4).</summary>
public sealed class Request
{
    public int RequestId { get; set; }
    public string Source { get; set; } = "webui"; // webui | telegram | whatsapp | system
    public string Requester { get; set; } = "";
    public string Query { get; set; } = "";
    public DateTime FromTime { get; set; }
    public DateTime ToTime { get; set; }
    public int? CameraId { get; set; }             // null = všetky kamery
    public string? DetectionTypeFilter { get; set; } // null = motion
    public int ContextBeforeSec { get; set; } = 15;
    public int ContextAfterSec { get; set; } = 15;
    public string Status { get; set; } = "queued"; // queued|processing|completed|failed
    public string Estimate { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Persist flag (PERSIST_FLAGS) — user označil záznam/úsek na zachovanie (S5-6).</summary>
public sealed class PersistFlag
{
    public int PersistId { get; set; }
    public int RecordingId { get; set; }
    public int UserId { get; set; }
    public string Scope { get; set; } = "recording"; // recording | range
    public DateTime? RangeStart { get; set; }
    public DateTime? RangeEnd { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}

/// <summary>Anotácia detekcie (ANNOTATIONS) — user poznámka/oprava regiónu (S5-6).</summary>
public sealed class Annotation
{
    public int AnnotationId { get; set; }
    public int DetectionId { get; set; }
    public int UserId { get; set; }
    public NormalizedRegion Region { get; set; }
    public string Label { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
