using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Contracts.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// GET /api/v1/recordings — záznamy (segmenty/event klipy) s filtrami (S5-3).
/// POST/DELETE /api/v1/recordings/{id}/persist — „zachovaj záznam" výnimka (S5-6):
/// persistnutý záznam retention NIKDY automaticky nemaže (PurgeJobHandler).
/// </summary>
[ApiController]
[Route("api/v1/recordings")]
public sealed class RecordingsController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    IRecordingRepository recordings,
    IDetectionRepository detections,
    IPersistRepository persists) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>
    /// GET /api/v1/recordings?cameraId=&amp;from=&amp;to=&amp;sourceType=
    /// Všetky filtre sú voliteľné; časy UTC ISO 8601.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecordingDto>>> Query(
        [FromQuery] int? cameraId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? sourceType,
        CancellationToken ct)
    {
        // Verejné čítanie (analýzy fungujú bez prihlásenia)
        var result = await recordings.QueryAsync(cameraId, from, to, sourceType, ct);
        return Ok(result.Select(r => new RecordingDto
        {
            RecordingId = r.RecordingId,
            NvrId = r.NvrId,
            CameraId = r.CameraId,
            SourceType = r.SourceType,
            NvrFilename = r.NvrFilename,
            BeginTime = r.BeginTime,
            EndTime = r.EndTime,
            DurationSec = r.DurationSec,
            SizeBytes = r.SizeBytes,
            Codec = r.Codec,
            Width = r.Width,
            Height = r.Height,
            Availability = r.Availability,
            AnalysisState = r.AnalysisState,
            Persisted = r.Persisted,
        }).ToList());
    }

    public sealed record PersistRequest(
        [Required] string Scope, // recording | range
        DateTime? RangeStart,
        DateTime? RangeEnd,
        string? Note);

    /// <summary>POST /api/v1/recordings/{id}/persist — označí záznam na zachovanie (persist výnimka).</summary>
    [HttpPost("{id:int}/persist")]
    public async Task<IActionResult> Persist(int id, [FromBody] PersistRequest request, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var recording = await recordings.GetByIdAsync(id, ct);
        if (recording is null)
            return NotFound(new { error = $"Recording {id} not found." });

        if (request.Scope == "range" && (request.RangeStart is null || request.RangeEnd is null || request.RangeEnd <= request.RangeStart))
            return BadRequest(new { error = "Range persist requires rangeStart < rangeEnd." });

        await persists.InsertAsync(new PersistFlag
        {
            RecordingId = id,
            UserId = user.UserId,
            Scope = request.Scope,
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd,
            Note = request.Note ?? "",
        }, ct);

        if (!recording.Persisted)
        {
            recording.Persisted = true;
            await recordings.UpdateAsync(recording, ct);
        }
        return Ok(new { ok = true, recordingId = id, persisted = true, scope = request.Scope });
    }

    /// <summary>DELETE /api/v1/recordings/{id}/persist — odznací persist (záznam sa opäť môže mazať).</summary>
    [HttpDelete("{id:int}/persist")]
    public async Task<IActionResult> Unpersist(int id, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var recording = await recordings.GetByIdAsync(id, ct);
        if (recording is null)
            return NotFound(new { error = $"Recording {id} not found." });

        await persists.RemoveAsync(id, ct);
        if (recording.Persisted)
        {
            recording.Persisted = false;
            await recordings.UpdateAsync(recording, ct);
        }
        return Ok(new { ok = true, recordingId = id, persisted = false });
    }

    /// <summary>
    /// S22l: GET /api/v1/recordings/analyzed-windows?windowMinutes=15&amp;maxWindows=4
    /// Analyzované 15-min okná (kvartály) — čo sa dá prehrať hneď (pre MCP/UI).
    /// Verejné čítanie.
    /// </summary>
    [HttpGet("analyzed-windows")]
    public async Task<ActionResult<IReadOnlyList<AnalyzedWindowDto>>> AnalyzedWindows(
        [FromQuery] int windowMinutes = 15,
        [FromQuery] int maxWindows = 4,
        CancellationToken ct = default)
    {
        var windows = await recordings.GetAnalyzedWindowsAsync(windowMinutes, DateTime.UtcNow, maxWindows, ct);
        return Ok(windows.Select(w => new AnalyzedWindowDto(
            w.WindowStart, w.WindowEnd, w.CamerasAnalyzed, w.TotalDetections, w.PersonFound)).ToList());
    }

    /// <summary>
    /// S22l: GET /api/v1/recordings/{id}/video — streamuje LOKÁLNE video nahrávky
    /// (tmp/{nvr_filename}.mp4 — po analýze ostáva pre rolling okná). Bez triggeru
    /// sťahovania: ak lokálny súbor neexistuje, 404 (analýza ešte neprebehla / okno odišlo).
    /// </summary>
    [HttpGet("{id:int}/video")]
    public async Task<IActionResult> Video(int id, CancellationToken ct)
    {
        var recording = await recordings.GetByIdAsync(id, ct);
        if (recording is null)
            return NotFound(new { error = $"Recording {id} not found." });

        if (string.IsNullOrWhiteSpace(recording.NvrFilename))
            return NotFound(new { error = "Recording has no local file." });

        var tempFile = Path.Combine(Options.MediaTempDir, Path.GetFileName(recording.NvrFilename) + ".mp4");
        if (!System.IO.File.Exists(tempFile))
            return NotFound(new { error = "Local video not available (analysis not finished or window expired)." });

        return PhysicalFile(tempFile, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>
    /// S22l: GET /api/v1/recordings/person-pending — nahrávky s nájdenou osobou
    /// čakajúce na potvrdenie (skip pre cleanup) — pre UI/MCP. Verejné čítanie.
    /// </summary>
    [HttpGet("person-pending")]
    public async Task<ActionResult<IReadOnlyList<RecordingDto>>> PersonPending(CancellationToken ct)
    {
        var result = await recordings.GetPersonPendingAsync(ct);
        return Ok(result.Select(r => new RecordingDto
        {
            RecordingId = r.RecordingId,
            NvrId = r.NvrId,
            CameraId = r.CameraId,
            SourceType = r.SourceType,
            NvrFilename = r.NvrFilename,
            BeginTime = r.BeginTime,
            EndTime = r.EndTime,
            DurationSec = r.DurationSec,
            SizeBytes = r.SizeBytes,
            Codec = r.Codec,
            Width = r.Width,
            Height = r.Height,
            Availability = r.Availability,
            AnalysisState = r.AnalysisState,
            Persisted = r.Persisted,
            PersonPending = r.PersonPending,
            WindowStartUtc = r.WindowStartUtc,
        }).ToList());
    }

    /// <summary>
    /// S22l: POST /api/v1/recordings/{id}/person-reviewed — užívateľ potvrdil kontrolu
    /// („skontrolované" v UI / agent po otázke) → zruší person_pending, purge môže zmazať.
    /// </summary>
    [HttpPost("{id:int}/person-reviewed")]
    public async Task<IActionResult> PersonReviewed(int id, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var recording = await recordings.GetByIdAsync(id, ct);
        if (recording is null)
            return NotFound(new { error = $"Recording {id} not found." });

        if (recording.PersonPending)
        {
            recording.PersonPending = false;
            await recordings.UpdateAsync(recording, ct);
        }
        return Ok(new { ok = true, recordingId = id, personPending = false });
    }

    /// <summary>
    /// S22l: DELETE /api/v1/recordings/{id} — vymazať nahrávku (UI/MCP po potvrdení).
    /// Admin; maže detekcie + lokálne video + záznam. (Pre person_pending flow „môžem zmazať?" → áno.)
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var recording = await recordings.GetByIdAsync(id, ct);
        if (recording is null)
            return NotFound(new { error = $"Recording {id} not found." });

        await deleteDetectionsAndVideoAsync(recording, ct);
        return Ok(new { ok = true, recordingId = id, deleted = true });
    }

    private async Task deleteDetectionsAndVideoAsync(Recording recording, CancellationToken ct)
    {
        // lokálne video (temp) ak existuje
        var tempDir = Options.MediaTempDir;
        if (!string.IsNullOrWhiteSpace(recording.NvrFilename) && !string.IsNullOrWhiteSpace(tempDir))
        {
            var tempFile = Path.Combine(tempDir, Path.GetFileName(recording.NvrFilename) + ".mp4");
            try { if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile); } catch (IOException) { }
        }
        // detekcie + záznam
        await detections.DeleteForRecordingAsync(recording.RecordingId, ct);
        await recordings.DeleteAsync(recording.RecordingId, ct);
    }

    public sealed record AnalyzedWindowDto(
        DateTime WindowStart, DateTime WindowEnd,
        int CamerasAnalyzed, int TotalDetections, bool PersonFound);
}
