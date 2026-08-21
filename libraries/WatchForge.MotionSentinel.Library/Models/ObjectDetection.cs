using WatchForge.Interfaces.Library;

namespace WatchForge.MotionSentinel.Library.Models;

/// <summary>
/// Detekcia objektu (FR-04): bounding box + trieda + confidence.
/// Region je normalizovaný 0..1 (rovnaký súradnicový priestor ako MotionRegion).
/// </summary>
public sealed record ObjectDetection
{
    /// <summary>Normalizovaný bounding box (0..1, relatívne k framu).</summary>
    public NormalizedRegion Region { get; init; }

    /// <summary>Trieda objektu: person | vehicle | animal | ...</summary>
    public string ObjectClass { get; init; } = "person";

    /// <summary>Confidence 0..1 (z HOG SVM skóre, clampované).</summary>
    public float Confidence { get; init; }
}
