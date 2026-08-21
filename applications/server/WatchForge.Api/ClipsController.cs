using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// GET /api/v1/clips/{id} — doručenie klipu (S5-5): video MP4 alebo fotka JPG
/// (fotka má vykreslený detekčný obdĺžnik — vyrobil ho service pri ClipExtract).
/// Súbor sa streamuje priamo z CLIPS záznamu; expirované/zmazané → 404.
/// </summary>
[ApiController]
[Route("api/v1/clips")]
public sealed class ClipsController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IClipRepository clips) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>GET /api/v1/clips/{id} — vráti súbor klipu (video alebo fotka). Verejné — prehrávanie funguje bez prihlásenia.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var clip = await clips.GetByIdAsync(id, ct);
        if (clip is null || !System.IO.File.Exists(clip.FilePath))
            return NotFound(new { error = $"Clip {id} not found (or expired)." });

        var contentType = clip.Kind == "photo"
            ? "image/jpeg"
            : "video/mp4";
        return PhysicalFile(clip.FilePath, contentType, enableRangeProcessing: true);
    }
}
