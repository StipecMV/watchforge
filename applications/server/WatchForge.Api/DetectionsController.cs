using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Contracts.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// GET /api/v1/detections — detekcie s filtrami (S5-3). Event-first: web UI
/// najprv ukáže detekcie, potom si pýta klip.
/// POST /{id}/flag — flagged / false_positive (S5-6).
/// POST+GET /{id}/annotations — user anotácie (S5-6).
/// </summary>
[ApiController]
[Route("api/v1/detections")]
public sealed class DetectionsController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IDetectionRepository detections,
    IAnnotationRepository annotations,
    IClock clock) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>
    /// GET /api/v1/detections?cameraId=&amp;from=&amp;to=&amp;detectionType=&amp;flag=
    /// Regióny sú normalizované 0..1 (4K fram).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DetectionListDto>> Query(
        [FromQuery] int? cameraId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? detectionType,
        [FromQuery] string? flag,
        CancellationToken ct)
    {
        // Verejné (analýzy fungujú bez prihlásenia) — items = max 2000 najnovších (inak 400k detekcií = 130 MB JSON a UI zamrzne),
        // total = presný počet pre štatistiku.
        var result = await detections.QueryAsync(cameraId, from, to, detectionType, flag, ct);
        var total = await detections.CountAsync(cameraId, from, to, detectionType, flag, ct);
        return Ok(new DetectionListDto(total, result.Select(d => new DetectionDto
        {
            DetectionId = d.DetectionId,
            RecordingId = d.RecordingId,
            CameraId = d.CameraId,
            DetectionType = d.DetectionType,
            TimestampMs = d.TimestampMs,
            DurationMs = d.DurationMs,
            Confidence = d.Confidence,
            AlgorithmVersion = d.AlgorithmVersion,
            ConfigVersionId = d.ConfigVersionId ?? 0,
            RegionX = d.Region.X,
            RegionY = d.Region.Y,
            RegionW = d.Region.W,
            RegionH = d.Region.H,
            Intensity = d.Intensity,
            ObjectClass = d.ObjectClass,
            Flag = d.Flag,
        }).ToList()));
    }

    public sealed record DetectionListDto(int Total, IReadOnlyList<DetectionDto> Items);

    public sealed record FlagRequest([Required] string Flag); // none | flagged | false_positive

    /// <summary>POST /api/v1/detections/{id}/flag — označí detekciu (flagged / false positive).</summary>
    [HttpPost("{id:int}/flag")]
    public async Task<IActionResult> SetFlag(int id, [FromBody] FlagRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        if (request.Flag is not ("none" or "flagged" or "false_positive"))
            return BadRequest(new { error = "Flag must be one of: none, flagged, false_positive." });

        await detections.SetFlagAsync(id, request.Flag, user.UserId, clock.UtcNow, ct);
        return Ok(new { ok = true, detectionId = id, flag = request.Flag });
    }

    public sealed record AnnotationRequest(
        [Range(0f, 1f)] float RegionX,
        [Range(0f, 1f)] float RegionY,
        [Range(0f, 1f)] float RegionW,
        [Range(0f, 1f)] float RegionH,
        string? Label);

    public sealed record AnnotationDto(int AnnotationId, int DetectionId, int UserId,
        float RegionX, float RegionY, float RegionW, float RegionH, string Label, DateTime CreatedAt);

    /// <summary>POST /api/v1/detections/{id}/annotations — pridá anotáciu detekcie.</summary>
    [HttpPost("{id:int}/annotations")]
    public async Task<ActionResult<AnnotationDto>> AddAnnotation(int id, [FromBody] AnnotationRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var region = new NormalizedRegion(request.RegionX, request.RegionY, request.RegionW, request.RegionH);
        if (!region.IsValid)
            return BadRequest(new { error = "Region must be within 0..1 and have positive width/height." });

        var annotation = new Annotation
        {
            DetectionId = id,
            UserId = user.UserId,
            Region = region,
            Label = request.Label ?? "",
        };
        await annotations.InsertAsync(annotation, ct);
        return Ok(ToDto(annotation));
    }

    /// <summary>GET /api/v1/detections/{id}/annotations — zoznam anotácií detekcie.</summary>
    [HttpGet("{id:int}/annotations")]
    public async Task<ActionResult<IReadOnlyList<AnnotationDto>>> GetAnnotations(int id, CancellationToken ct)
    {
        // Verejné čítanie (flag screen funguje bez prihlásenia)
        var result = await annotations.GetByDetectionAsync(id, ct);
        return Ok(result.Select(ToDto).ToList());
    }

    /// <summary>DELETE /api/v1/detections/{id}/annotations — „Clear my drawings" (FR-16):
    /// maže LEN anotácie aktuálneho používateľa, systémové detekcie a cudzie kresby ostávajú.</summary>
    [HttpDelete("{id:int}/annotations")]
    public async Task<IActionResult> ClearMyAnnotations(int id, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var removed = await annotations.DeleteByDetectionAndUserAsync(id, user.UserId, ct);
        return Ok(new { ok = true, detectionId = id, removed });
    }

    /// <summary>PUT /api/v1/detections/{id}/annotations/{annotationId} — úprava vlastnej
    /// anotácie (posun/resize/label na flag screen, S6-7). Cudzia anotácia → 404.</summary>
    [HttpPut("{id:int}/annotations/{annotationId:int}")]
    public async Task<ActionResult<AnnotationDto>> UpdateAnnotation(int id, int annotationId, [FromBody] AnnotationRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var existing = await annotations.GetByIdAsync(annotationId, ct);
        if (existing is null || existing.DetectionId != id || existing.UserId != user.UserId)
            return NotFound(new { error = "Annotation not found or not owned by the current user." });

        var region = new NormalizedRegion(request.RegionX, request.RegionY, request.RegionW, request.RegionH);
        if (!region.IsValid)
            return BadRequest(new { error = "Region must be within 0..1 and have positive width/height." });

        existing.Region = region;
        existing.Label = request.Label ?? "";
        await annotations.UpdateAsync(existing, ct);
        return Ok(ToDto(existing));
    }

    private static AnnotationDto ToDto(Annotation a) => new(
        a.AnnotationId, a.DetectionId, a.UserId,
        a.Region.X, a.Region.Y, a.Region.W, a.Region.H, a.Label, a.CreatedAt);
}
