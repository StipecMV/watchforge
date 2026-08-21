using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;
using WatchForge.MotionSentinel.Library.Detection;
using WatchForge.MotionSentinel.Library.Models;

namespace WatchForge.Api;

/// <summary>
/// /api/v1/identities — správa identít a face embeddings (FR-05, S10-4).
/// GET zoznam, POST create, POST learn (crop upload → embedding), DELETE.
/// </summary>
[ApiController]
[Route("api/v1/identities")]
public sealed class IdentitiesController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IIdentityRepository identities,
    IFaceRepository faces,
    IFaceRecognizer faceRecognizer) : ApiControllerBase(users, dataProtection, options)
{
    public sealed record IdentityDto(int IdentityId, string Name, DateTime CreatedAt, int FaceCount);

    /// <summary>GET /api/v1/identities — zoznam identít s počtom tvárí (prihlásený user).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IdentityDto>>> GetAll(CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        var result = new List<IdentityDto>();
        foreach (var identity in await identities.GetAllAsync(ct))
        {
            var faceCount = (await faces.GetByIdentityAsync(identity.IdentityId, ct)).Count;
            result.Add(new IdentityDto(identity.IdentityId, identity.Name, identity.CreatedAt, faceCount));
        }
        return Ok(result);
    }

    public sealed record CreateIdentityRequest(string Name);

    /// <summary>POST /api/v1/identities — vytvorenie identity (prihlásený user).</summary>
    [HttpPost]
    public async Task<ActionResult<IdentityDto>> Create(
        [FromBody] CreateIdentityRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        var id = await identities.CreateAsync(request.Name.Trim(), user.UserId, ct);
        return Ok(new IdentityDto(id, request.Name.Trim(), DateTime.UtcNow, 0));
    }

    public sealed record LearnRequest(int DetectionId, int IdentityId);

    /// <summary>
    /// POST /api/v1/identities/learn — priradenie existujúcej face detekcie
    /// (z Analyze) k identite + natrénovanie modelu (embedding sa už perzistoval
    /// v Analyze; tu sa len prepojí identity_id a aktualizuje LBPH model).
    /// </summary>
    [HttpPost("learn")]
    public async Task<IActionResult> Learn([FromBody] LearnRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        var identity = await identities.GetByIdAsync(request.IdentityId, ct);
        if (identity is null)
            return NotFound(new { error = "Identity not found." });

        var facesForDetection = await faces.GetByDetectionAsync(request.DetectionId, ct);
        if (facesForDetection.Count == 0)
            return NotFound(new { error = "No face record for this detection." });

        // Priradenie identity všetkým face záznamom detekcie; model sa potom
        // obnoví z DB (Reset + LoadState) — žiadny duplicitný INSERT embeddingu.
        foreach (var face in facesForDetection)
        {
            await faces.AssignIdentityAsync(face.FaceId, request.IdentityId, ct);
        }
        await faceRecognizer.ResetAsync(ct);
        await faceRecognizer.LoadStateAsync(faces, identities, ct);
        return Ok(new { assigned = facesForDetection.Count, identityId = request.IdentityId });
    }

    /// <summary>DELETE /api/v1/identities/{id} — vymazanie identity aj embeddings.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        await faces.DeleteByIdentityAsync(id, ct);
        var deleted = await identities.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound(new { error = "Identity not found." });

        // Reset modelu — embeddings identity sa odstránili z DB, model sa načíta odznova
        await faceRecognizer.ResetAsync(ct);
        if (faces is not null)
            await faceRecognizer.LoadStateAsync(faces, identities, ct);
        return Ok(new { deleted = true });
    }
}
