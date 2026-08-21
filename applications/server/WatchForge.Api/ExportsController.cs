using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Contracts.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// S20: Export vybranej časovej úsečky (dôkazový klip) — POST /api/v1/exports.
/// Používateľ vyberie interval na timeline (napr. 3 minúty) → vytvorí sa Request
/// (rovnaký mechanizmus ako RequestsController) + ExportRange job, ktorý
/// vystrihne presný interval z NVR záznamu (ffmpeg cut, H.264 MP4).
/// Polling stavu cez GET /api/v1/requests/{id}, stiahnutie cez GET /api/v1/clips/{id}.
/// </summary>
[ApiController]
[Route("api/v1/exports")]
public sealed class ExportsController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IRequestRepository requests,
    IRecordingRepository recordings,
    IJobRepository jobs) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>POST /api/v1/exports — export intervalu [fromTime, toTime] kamery ako klip.</summary>
    [HttpPost]
    public async Task<ActionResult<RequestStatusDto>> Create([FromBody] CreateRequestDto dto, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (dto.ToTime <= dto.FromTime)
            return BadRequest(new { error = "toTime must be after fromTime." });
        if (dto.ToTime - dto.FromTime > TimeSpan.FromHours(24))
            return BadRequest(new { error = "Export range is limited to 24 hours." });

        // Záznamy prekrývajúce interval (kamera povinná pre export)
        if (dto.CameraId is null)
            return BadRequest(new { error = "cameraId is required for export." });
        var candidates = await recordings.QueryAsync(dto.CameraId, dto.FromTime, dto.ToTime, null, ct);
        if (candidates.Count == 0)
            return NotFound(new { error = "No recordings found in the selected range." });

        var source = user.UserId == 0 ? JobSource.System : JobSource.WebUi;
        var priority = JobPriority.FromSource(source);

        var request = await requests.InsertAsync(new Request
        {
            Source = source.ToString().ToLowerInvariant(),
            Requester = user.Username,
            Query = $"export {dto.FromTime:yyyy-MM-dd HH:mm} – {dto.ToTime:yyyy-MM-dd HH:mm}",
            FromTime = dto.FromTime,
            ToTime = dto.ToTime,
            CameraId = dto.CameraId,
            DetectionTypeFilter = "export",
            ContextBeforeSec = 0,
            ContextAfterSec = 0,
            Status = "queued",
            Estimate = $"~{candidates.Count} záznamov",
        }, ct);

        // ExportRange job pre každý záznam v rozsahu (presný interval)
        foreach (var recording in candidates)
        {
            await jobs.EnqueueAsync(new Job
            {
                RecordingId = recording.RecordingId,
                RequestId = request.RequestId,
                Type = JobType.ExportRange,
                Priority = priority,
                Source = source,
                Status = JobStatus.Queued,
            }, ct);
        }

        return StatusCode(StatusCodes.Status202Accepted, ToStatusDto(request, []));
    }

    private static RequestStatusDto ToStatusDto(Request request, IReadOnlyList<int> clipIds) => new()
    {
        RequestId = request.RequestId,
        Status = request.Status,
        Estimate = request.Estimate,
        ClipIds = clipIds,
        Error = null,
    };
}
