using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Contracts.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>GET /api/v1/cameras — zoznam kamier (S5-3).</summary>
[ApiController]
[Route("api/v1/cameras")]
public sealed class CamerasController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    ICameraRepository cameras) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>GET /api/v1/cameras — všetky kamery (aj neaktívne, filter na strane UI). Verejné — live view/analýzy fungujú bez prihlásenia.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CameraDto>>> GetAll(CancellationToken ct)
    {
        var result = await cameras.GetAllAsync(null, ct);
        return Ok(result.Select(c => new CameraDto
        {
            CameraId = c.CameraId,
            NvrId = c.NvrId,
            Channel = c.Channel,
            FriendlyName = c.FriendlyName,
            IconId = c.IconId,
            IsActive = c.IsActive,
        }).ToList());
    }

    public sealed record UpdateCameraRequest(string? FriendlyName, string? IconId, bool IsActive = true);

    /// <summary>
    /// PUT /api/v1/cameras/{id} — úprava profilu kamery (S6-5 Settings → Cameras):
    /// friendly_name, icon_id (40 lokácií), is_active. Channel/NvrId sú systémové ID — nemenia sa.
    /// Zmena platí pre celé UI; záznamy ostávajú (camera_id sa nemení).
    /// </summary>
    [HttpPut("{cameraId:int}")]
    public async Task<ActionResult<CameraDto>> Update(int cameraId, [FromBody] UpdateCameraRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var friendlyName = request.FriendlyName?.Trim() ?? "";
        var iconId = request.IconId?.Trim() ?? "";
        if (friendlyName.Length == 0)
            return BadRequest(new { error = "FriendlyName is required." });
        if (friendlyName.Length > 80)
            return BadRequest(new { error = "FriendlyName must be at most 80 characters." });
        if (iconId.Length == 0 || iconId.Length > 40)
            return BadRequest(new { error = "IconId must be 1..40 characters." });

        var all = await cameras.GetAllAsync(null, ct);
        var existing = all.FirstOrDefault(c => c.CameraId == cameraId);
        if (existing is null)
            return NotFound(new { error = "Camera not found." });

        var updated = new Camera
        {
            CameraId = existing.CameraId,
            NvrId = existing.NvrId,
            Channel = existing.Channel,
            FriendlyName = friendlyName,
            IconId = iconId,
            IsActive = request.IsActive,
        };
        await cameras.UpdateAsync(updated, ct);

        return Ok(new CameraDto
        {
            CameraId = updated.CameraId,
            NvrId = updated.NvrId,
            Channel = updated.Channel,
            FriendlyName = updated.FriendlyName,
            IconId = updated.IconId,
            IsActive = updated.IsActive,
        });
    }
}
