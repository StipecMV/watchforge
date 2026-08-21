using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Contracts.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// Interaktívne požiadavky (S5-4): „hľadaj pohyb 14:00–16:00".
/// POST vytvorí REQUEST + prioritné joby (Analyze pre nespracované záznamy,
/// ClipExtract pre hotové), GET vráti stav + odhad + clipIds.
/// </summary>
[ApiController]
[Route("api/v1/requests")]
public sealed class RequestsController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IRequestRepository requests,
    IRecordingRepository recordings,
    IJobRepository jobs,
    IClipRepository clips) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>Odhad: ~2 min spracovania na záznam (download + analýza).</summary>
    private static readonly TimeSpan EstimatePerRecording = TimeSpan.FromMinutes(2);

    /// <summary>POST /api/v1/requests — vytvorí požiadavku + prioritné joby (202 + stav).</summary>
    [HttpPost]
    public async Task<ActionResult<RequestStatusDto>> Create([FromBody] CreateRequestDto dto, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (dto.ToTime <= dto.FromTime)
            return BadRequest(new { error = "toTime must be after fromTime." });
        if (dto.ToTime - dto.FromTime > TimeSpan.FromHours(48))
            return BadRequest(new { error = "Request range is limited to 48 hours." });

        // Záznamy v rozsahu (kamera alebo všetky)
        var candidates = await recordings.QueryAsync(dto.CameraId, dto.FromTime, dto.ToTime, null, ct);
        var estimate = EstimateFor(candidates.Count);
        var source = user.UserId == 0 ? JobSource.System : JobSource.WebUi;
        var priority = JobPriority.FromSource(source);

        var request = await requests.InsertAsync(new Request
        {
            Source = source.ToString().ToLowerInvariant(),
            Requester = user.Username,
            Query = $"motion {dto.FromTime:yyyy-MM-dd HH:mm} – {dto.ToTime:yyyy-MM-dd HH:mm}",
            FromTime = dto.FromTime,
            ToTime = dto.ToTime,
            CameraId = dto.CameraId,
            DetectionTypeFilter = dto.DetectionTypeFilter ?? "motion",
            ContextBeforeSec = dto.ContextBeforeSec,
            ContextAfterSec = dto.ContextAfterSec,
            Status = "queued",
            Estimate = estimate,
        }, ct);

        // Prioritné joby: Analyze pre nespracované, ClipExtract pre hotové záznamy
        foreach (var recording in candidates)
        {
            var type = recording.AnalysisState == "completed" ? JobType.ClipExtract : JobType.Analyze;
            await jobs.EnqueueAsync(new Job
            {
                RecordingId = recording.RecordingId,
                RequestId = request.RequestId,
                Type = type,
                Priority = priority,
                Source = source,
                Status = JobStatus.Queued,
            }, ct);
        }

        return StatusCode(StatusCodes.Status202Accepted, ToStatusDto(request, []));
    }

    /// <summary>GET /api/v1/requests/{id} — stav + odhad + clipIds.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RequestStatusDto>> GetById(int id, CancellationToken ct)
    {
        if (await AuthenticatedUserAsync(ct) is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        var request = await requests.GetByIdAsync(id, ct);
        if (request is null)
            return NotFound(new { error = $"Request {id} not found." });

        var clipIds = (await clips.GetByRequestAsync(id, ct)).Select(c => c.ClipId).ToList();
        return Ok(ToStatusDto(request, clipIds));
    }

    private static string EstimateFor(int recordingCount)
    {
        if (recordingCount == 0) return "0 záznamov v rozsahu";
        var total = recordingCount * EstimatePerRecording;
        return $"~{recordingCount} záznamov, odhad {total.TotalMinutes:F0} min";
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
