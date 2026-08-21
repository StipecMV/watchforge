using WatchForge.Interfaces.Library;

namespace WatchForge.MotionSentinel.Library.Models;

/// <summary>
/// Detekcia tváre (FR-05): bounding box + rozpoznaná identita + confidence.
/// Region je normalizovaný 0..1 (rovnaký súradnicový priestor ako MotionRegion),
/// takže platí priamo pre 4K fram (face crop z originálu = samostatná etapa).
/// </summary>
public sealed record FaceDetection
{
    /// <summary>Normalizovaný bounding box tváre (0..1).</summary>
    public NormalizedRegion Region { get; init; }

    /// <summary>ID rozpoznanej identity (null = neznáma tvár).</summary>
    public int? IdentityId { get; init; }

    /// <summary>Meno identity (pre report; prázdne = neznáma).</summary>
    public string IdentityName { get; init; } = "";

    /// <summary>Confidence 0..1 (LBPH distance preškálovaná).</summary>
    public float Confidence { get; init; }
}
