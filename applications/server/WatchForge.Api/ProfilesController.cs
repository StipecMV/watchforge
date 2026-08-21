using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// Detekčné profily (S5-7): GET aktívny profil pre kameru (per-camera + shared fallback),
/// PUT nová verzia (per-camera alebo shared). Zmena platí pre nové analýzy —
/// staré záznamy si držia config_version_id.
/// </summary>
[ApiController]
[Route("api/v1/profiles")]
public sealed class ProfilesController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IConfigVersionRepository configs) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>
    /// Zóna detekčného profilu (FR-06). X/Y/W/H = normalizovaný obdĺžnik 0..1
    /// (bbox — jediná geometria, ktorú analýza aplikuje). Name = meno zóny pre UI,
    /// Shape = kresliaci nástroj ("rect" | "polygon" | "freehand"), Points = body
    /// nakresleného tvaru (normalizované, len pre polygon/freehand — S6-6).
    /// </summary>
    public sealed record ZoneDto(
        float X, float Y, float W, float H,
        string? Name = null,
        string? Shape = null,
        IReadOnlyList<IReadOnlyList<float>>? Points = null);

    /// <summary>Seriálizácia zón: null name/shape/points sa do JSON nezapíšu (spätná kompatibilita).</summary>
    private static readonly JsonSerializerOptions ZoneJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed record ProfileDto(
        int ConfigVersionId, int? CameraId, string ProfileType,
        float Sensitivity, float IntensityThreshold, float MinContourArea,
        IReadOnlyList<ZoneDto> IgnoreZones, IReadOnlyList<ZoneDto> FocusZones);

    public sealed record ProfileRequest(
        float Sensitivity, float IntensityThreshold, float MinContourArea,
        IReadOnlyList<ZoneDto>? IgnoreZones, IReadOnlyList<ZoneDto>? FocusZones);

    /// <summary>GET /api/v1/profiles?cameraId=N — aktívny profil (per-camera, fallback shared).</summary>
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetActive([FromQuery] int cameraId, CancellationToken ct)
    {
        if (await AuthenticatedUserAsync(ct) is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        var version = await configs.GetActiveForCameraAsync(cameraId, ct);
        if (version is null)
            return NotFound(new { error = "No active profile (seed a shared profile first)." });
        return Ok(ToDto(version));
    }

    /// <summary>PUT /api/v1/profiles/{cameraId} — nová verzia per-camera profilu.</summary>
    [HttpPut("{cameraId:int}")]
    public async Task<ActionResult<ProfileDto>> PutPerCamera(int cameraId, [FromBody] ProfileRequest request, CancellationToken ct)
    {
        if (await AuthenticatedUserAsync(ct) is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (!ValidateRequest(request, out var error))
            return BadRequest(new { error });

        var id = await configs.InsertAsync(new ConfigVersion
        {
            CameraId = cameraId,
            ProfileName = "default",
            ProfileType = "per_camera",
            Sensitivity = request.Sensitivity,
            IntensityThreshold = request.IntensityThreshold,
            MinContourArea = request.MinContourArea,
            IgnoreZonesJson = JsonSerializer.Serialize(request.IgnoreZones ?? [], ZoneJson),
            FocusZonesJson = JsonSerializer.Serialize(request.FocusZones ?? [], ZoneJson),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        }, ct);

        var created = await configs.GetActiveForCameraAsync(cameraId, ct);
        return Ok(ToDto(created!));
    }

    /// <summary>PUT /api/v1/profiles/shared — nová verzia zdieľaného profilu.</summary>
    [HttpPut("shared")]
    public async Task<ActionResult<ProfileDto>> PutShared([FromBody] ProfileRequest request, CancellationToken ct)
    {
        if (await AuthenticatedUserAsync(ct) is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (!ValidateRequest(request, out var error))
            return BadRequest(new { error });

        await configs.InsertAsync(new ConfigVersion
        {
            CameraId = null,
            ProfileName = "default",
            ProfileType = "shared",
            Sensitivity = request.Sensitivity,
            IntensityThreshold = request.IntensityThreshold,
            MinContourArea = request.MinContourArea,
            IgnoreZonesJson = JsonSerializer.Serialize(request.IgnoreZones ?? [], ZoneJson),
            FocusZonesJson = JsonSerializer.Serialize(request.FocusZones ?? [], ZoneJson),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        }, ct);

        var created = await configs.GetActiveSharedAsync(ct);
        return Ok(ToDto(created!));
    }

    private static bool ValidateRequest(ProfileRequest request, out string? error)
    {
        if (request.Sensitivity is < 0f or > 1f) { error = "Sensitivity must be in 0..1."; return false; }
        if (request.IntensityThreshold is < 0f or > 1f) { error = "IntensityThreshold must be in 0..1."; return false; }
        if (request.MinContourArea is < 0f or > 1f) { error = "MinContourArea must be in 0..1 (normalized)."; return false; }
        foreach (var zone in (request.IgnoreZones ?? []).Concat(request.FocusZones ?? []))
        {
            var region = new NormalizedRegion(zone.X, zone.Y, zone.W, zone.H);
            if (!region.IsValid) { error = "Zones must be within 0..1 with positive width/height."; return false; }
            if (zone.Name is { Length: > 64 }) { error = "Zone name must be at most 64 characters."; return false; }
            if (zone.Shape is not (null or "rect" or "polygon" or "freehand"))
            {
                error = "Zone shape must be rect, polygon or freehand.";
                return false;
            }
            if (zone.Shape is "polygon" or "freehand" && zone.Points is null or { Count: 0 })
            {
                error = "Zone shape polygon/freehand requires points.";
                return false;
            }
            if (zone.Points is not null)
            {
                foreach (var point in zone.Points)
                {
                    if (point.Count != 2 || point[0] is < 0f or > 1f || point[1] is < 0f or > 1f)
                    {
                        error = "Zone points must be [x,y] pairs within 0..1.";
                        return false;
                    }
                }
            }
        }
        error = null;
        return true;
    }

    private static ProfileDto ToDto(ConfigVersion v)
    {
        static List<ZoneDto> Parse(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<ZoneDto>>(json) ?? [];
            }
            catch (JsonException) { return []; }
        }

        return new ProfileDto(
            v.ConfigVersionId, v.CameraId, v.ProfileType,
            v.Sensitivity, v.IntensityThreshold, v.MinContourArea,
            Parse(v.IgnoreZonesJson), Parse(v.FocusZonesJson));
    }
}
