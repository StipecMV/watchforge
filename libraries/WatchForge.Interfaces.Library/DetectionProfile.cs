namespace WatchForge.Interfaces.Library;

/// <summary>Normalizovaný región (0..1 relatívne k 4K framu).</summary>
public readonly record struct NormalizedRegion(float X, float Y, float W, float H)
{
    public bool IsValid => X >= 0f && Y >= 0f && W > 0f && H > 0f && X + W <= 1.0001f && Y + H <= 1.0001f;
}

/// <summary>Detekčný profil kamery (FR-06). IGNORE zóny = masky, FOCUS zóny = focus regióny.</summary>
public sealed class DetectionProfile
{
    public int? CameraId { get; set; }          // null = zdieľaný profil
    public string ProfileName { get; set; } = "default";
    public string ProfileType { get; set; } = "per_camera"; // per_camera | shared
    public float Sensitivity { get; set; } = 0.5f;
    public float IntensityThreshold { get; set; } = 0.02f;
    public float MinContourArea { get; set; } = 0.001f;
    public List<NormalizedRegion> IgnoreZones { get; set; } = [];
    public List<NormalizedRegion> FocusZones { get; set; } = [];

    public static DetectionProfile Default => new();
}
