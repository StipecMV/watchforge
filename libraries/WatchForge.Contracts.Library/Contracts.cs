using System.Text.Json.Serialization;

namespace WatchForge.Contracts.Library;

/// <summary>
/// Zdieľané DTO kontrakty medzi API, service a web UI (DRY).
/// Všetky DTO serializujú camelCase (frontend konvencia) — explicitne cez JsonPropertyName.
/// Časy sú v UTC (ISO 8601).
/// Koordináty regiónov sú normalizované 0..1 relatívne k 4K framu.
/// </summary>
public static class Contracts
{
    public const string DefaultContentType = "application/json";
}

/// <summary>Kamera z NVR (GET /api/v1/cameras).</summary>
public sealed class CameraDto
{
    [JsonPropertyName("cameraId")] public int CameraId { get; set; }
    [JsonPropertyName("nvrId")] public int NvrId { get; set; }
    [JsonPropertyName("channel")] public int Channel { get; set; }
    [JsonPropertyName("friendlyName")] public string FriendlyName { get; set; } = "";
    [JsonPropertyName("iconId")] public string IconId { get; set; } = "";
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
}

/// <summary>Záznam (segment/event klip) z NVR (GET /api/v1/recordings).</summary>
public sealed class RecordingDto
{
    [JsonPropertyName("recordingId")] public int RecordingId { get; set; }
    [JsonPropertyName("nvrId")] public int NvrId { get; set; }
    [JsonPropertyName("cameraId")] public int CameraId { get; set; }
    [JsonPropertyName("sourceType")] public string SourceType { get; set; } = "segment"; // segment | event_clip
    [JsonPropertyName("nvrFilename")] public string NvrFilename { get; set; } = "";
    [JsonPropertyName("beginTime")] public DateTime BeginTime { get; set; }
    [JsonPropertyName("endTime")] public DateTime EndTime { get; set; }
    [JsonPropertyName("durationSec")] public int DurationSec { get; set; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("codec")] public string Codec { get; set; } = "hevc";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("availability")]
    public string Availability { get; set; } = "available"; // available | unavailable | missed_during_outage
    [JsonPropertyName("analysisState")]
    public string AnalysisState { get; set; } = "queued"; // queued|downloading|analyzing|completed|failed|interrupted
    [JsonPropertyName("persisted")] public bool Persisted { get; set; }
    /// <summary>S22l: analýza našla osobu — skip pre cleanup (chránená pred purge do potvrdenia).</summary>
    [JsonPropertyName("personPending")] public bool PersonPending { get; set; }
    /// <summary>S22n: začiatok 15-min okna, na ktoré je video trimnuté (player 0 = toto;
    /// null = celý segment bez orezania). Pre sync udalostí s prehrávaním.</summary>
    [JsonPropertyName("windowStartUtc")] public DateTime? WindowStartUtc { get; set; }
}

/// <summary>Detekcia (motion/person/vehicle/animal/face) — GET /api/v1/detections.</summary>
public sealed class DetectionDto
{
    [JsonPropertyName("detectionId")] public int DetectionId { get; set; }
    [JsonPropertyName("recordingId")] public int RecordingId { get; set; }
    [JsonPropertyName("cameraId")] public int CameraId { get; set; }
    [JsonPropertyName("detectionType")]
    public string DetectionType { get; set; } = "motion"; // motion | person | vehicle | animal | face
    [JsonPropertyName("timestampMs")] public int TimestampMs { get; set; }
    [JsonPropertyName("durationMs")] public int DurationMs { get; set; }
    [JsonPropertyName("confidence")] public float Confidence { get; set; }
    [JsonPropertyName("algorithmVersion")] public string AlgorithmVersion { get; set; } = "";
    [JsonPropertyName("configVersionId")] public int ConfigVersionId { get; set; }
    [JsonPropertyName("regionX")] public float RegionX { get; set; }
    [JsonPropertyName("regionY")] public float RegionY { get; set; }
    [JsonPropertyName("regionW")] public float RegionW { get; set; }
    [JsonPropertyName("regionH")] public float RegionH { get; set; }
    [JsonPropertyName("intensity")] public float Intensity { get; set; }
    [JsonPropertyName("objectClass")] public string ObjectClass { get; set; } = "";
    [JsonPropertyName("flag")] public string Flag { get; set; } = "none"; // none | flagged | false_positive
}

/// <summary>Vytvorenie interaktívnej požiadavky (POST /api/v1/requests).</summary>
public sealed class CreateRequestDto
{
    [JsonPropertyName("fromTime")] public DateTime FromTime { get; set; }
    [JsonPropertyName("toTime")] public DateTime ToTime { get; set; }
    [JsonPropertyName("cameraId")] public int? CameraId { get; set; } // null = všetky
    [JsonPropertyName("detectionTypeFilter")]
    public string? DetectionTypeFilter { get; set; } // null = motion
    [JsonPropertyName("contextBeforeSec")] public int ContextBeforeSec { get; set; } = 15;
    [JsonPropertyName("contextAfterSec")] public int ContextAfterSec { get; set; } = 15;
}

/// <summary>Stav požiadavky pre agenta/web (GET /api/v1/requests/{id}).</summary>
public sealed class RequestStatusDto
{
    [JsonPropertyName("requestId")] public int RequestId { get; set; }
    [JsonPropertyName("status")]
    public string Status { get; set; } = "queued"; // queued|processing|completed|failed|priority_missed
    [JsonPropertyName("estimate")] public string Estimate { get; set; } = "";
    [JsonPropertyName("clipIds")] public IReadOnlyList<int> ClipIds { get; set; } = [];
    [JsonPropertyName("error")] public string? Error { get; set; }
}
