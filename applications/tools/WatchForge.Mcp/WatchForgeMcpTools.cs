using System.Text.Json;
using ModelContextProtocol.Server;
using WatchForge.Mcp;

namespace WatchForge.Mcp;

/// <summary>
/// MCP nástroje WatchForge (S7-1): mapujú REST API v1 (WatchForge.Api) —
/// agent/LLM cez MCP vie hľadať pohyb, pýtať si klipy a spúšťať spracovanie.
/// Konfigurácia: WATCHFORGE_API_URL (default http://localhost:5000),
/// WATCHFORGE_API_TOKEN (X-Api-Token, default prázdny = session-only API).
/// </summary>
public sealed class WatchForgeMcpTools
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly HttpClient _http;

    /// <summary>
    /// Konštruktor bez DI — McpServer vytvára inštanciu aktivátorom, takže
    /// BaseAddress sa nastaví priamo (S22h: inak by HttpClient nemal base URL).
    /// </summary>
    public WatchForgeMcpTools()
    {
        var apiUrl = Environment.GetEnvironmentVariable("WATCHFORGE_API_URL") ?? "http://localhost:5000";
        var apiToken = Environment.GetEnvironmentVariable("WATCHFORGE_API_TOKEN") ?? "";
        _http = new HttpClient { BaseAddress = new Uri(apiUrl.EndsWith('/') ? apiUrl : apiUrl + "/") };
        if (!string.IsNullOrEmpty(apiToken))
            _http.DefaultRequestHeaders.Add("X-Api-Token", apiToken);
    }

    /// <summary>DI konštruktor (testy / DI kontajner) — explicitný HttpClient.</summary>
    public WatchForgeMcpTools(HttpClient http)
    {
        _http = http;
    }

    /// <summary>search_motion — detekcie pohybu v časovom rozsahu (event-first: najprv nájdi, potom klip).</summary>
    [McpServerTool(Name = "search_motion")]
    public async Task<string> SearchMotionAsync(
        string fromTime,
        string toTime,
        int? cameraId = null,
        string? detectionType = null,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"from={Uri.EscapeDataString(fromTime)}", $"to={Uri.EscapeDataString(toTime)}" };
        if (cameraId is not null) query.Add($"cameraId={cameraId}");
        if (!string.IsNullOrEmpty(detectionType)) query.Add($"detectionType={Uri.EscapeDataString(detectionType)}");

        using var response = await _http.GetAsync($"/api/v1/detections?{string.Join('&', query)}", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {response.StatusCode}: {(int)response.StatusCode}" });
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        // API vracia { total, items: [...] }, ale testy/fake vracajú priamo pole — podporujeme oba
        var detections = root.ValueKind == JsonValueKind.Array
            ? root
            : (root.TryGetProperty("items", out var items) ? items : root);
        return JsonSerializer.Serialize(new
        {
            count = detections.GetArrayLength(),
            detections = detections.EnumerateArray().Select(d => new
            {
                d = d.GetProperty("detectionId").GetInt32(),
                recordingId = d.GetProperty("recordingId").GetInt32(),
                cameraId = d.GetProperty("cameraId").GetInt32(),
                type = d.GetProperty("detectionType").GetString(),
                timestampMs = d.GetProperty("timestampMs").GetInt32(),
                confidence = d.GetProperty("confidence").GetDouble(),
                region = new { x = d.GetProperty("regionX").GetDouble(), y = d.GetProperty("regionY").GetDouble(), w = d.GetProperty("regionW").GetDouble(), h = d.GetProperty("regionH").GetDouble() },
            }),
        }, Json);
    }

    /// <summary>get_clip — URL + metadáta klipu (video/fotka) podľa clipId.</summary>
    [McpServerTool(Name = "get_clip")]
    public async Task<string> GetClipAsync(int clipId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/api/v1/clips/{clipId}", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}" });
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var size = response.Content.Headers.ContentLength ?? 0;
        return JsonSerializer.Serialize(new
        {
            clipId,
            kind = contentType.Contains("jpeg") ? "photo" : "video",
            contentType,
            sizeBytes = size,
            url = $"{_http.BaseAddress}api/v1/clips/{clipId}",
        }, Json);
    }

    /// <summary>
    /// S22l: trigger_processing — ZAKÁZANÉ (žiadny trigger analýzy). Analýza beží
    /// automaticky v 15-min oknách; MCP/UI len čítajú. Odpoveď: prečo nie + čo je k dispozícii.
    /// </summary>
    [McpServerTool(Name = "trigger_processing")]
    public async Task<string> TriggerProcessingAsync(
        string fromTime,
        string toTime,
        int? cameraId = null,
        int? contextBeforeSec = null,
        int? contextAfterSec = null,
        CancellationToken ct = default)
    {
        // Žiadne sťahovanie/analýza na požiadanie — analýza beží automaticky v oknách.
        // Vrátime dostupné analyzované okná, aby agent vedel, čo sa dá prehrať hneď.
        try
        {
            using var response = await _http.GetAsync("/api/v1/recordings/analyzed-windows", ct);
            if (response.IsSuccessStatusCode)
            {
                var windows = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                return JsonSerializer.Serialize(new
                {
                    allowed = false,
                    reason = "Analýza sa nespúšťa na požiadanie — beží automaticky v 15-min oknách (rolling 60 min). Pre tento úsek nemám analýzu a treba dlho čakať (systém spracúva najnovšie okno).",
                    analyzedWindows = windows.EnumerateArray().Select(w => new
                    {
                        windowStart = w.GetProperty("windowStart").GetString(),
                        windowEnd = w.GetProperty("windowEnd").GetString(),
                        camerasAnalyzed = w.GetProperty("camerasAnalyzed").GetInt32(),
                        totalDetections = w.GetProperty("totalDetections").GetInt32(),
                    }),
                    hint = "Použi search_motion/search_recordings s časom v rámci analyzedWindows, alebo get_clip pre hotový klip.",
                }, Json);
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { allowed = false, error = ex.Message });
        }
        return JsonSerializer.Serialize(new { allowed = false, reason = "Analýza sa nespúšťa na požiadanie." });
    }

    /// <summary>S22l: analyzed_windows — aké 15-min okná sú analyzované (čo sa dá prehrať hneď).</summary>
    [McpServerTool(Name = "analyzed_windows")]
    public async Task<string> AnalyzedWindowsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/v1/recordings/analyzed-windows", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}" });
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var windows = root.ValueKind == JsonValueKind.Array ? root : (root.TryGetProperty("items", out var items) ? items : root);
        return JsonSerializer.Serialize(new
        {
            count = windows.GetArrayLength(),
            windows = windows.EnumerateArray().Select(w => new
            {
                windowStart = w.GetProperty("windowStart").GetString(),
                windowEnd = w.GetProperty("windowEnd").GetString(),
                camerasAnalyzed = w.GetProperty("camerasAnalyzed").GetInt32(),
                totalDetections = w.GetProperty("totalDetections").GetInt32(),
            }),
        }, Json);
    }

    /// <summary>S22l: person_pending — nahrávky s nájdenou osobou čakajúce na potvrdenie (skip pre cleanup).</summary>
    [McpServerTool(Name = "person_pending")]
    public async Task<string> PersonPendingAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/v1/recordings/person-pending", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}" });
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = root.ValueKind == JsonValueKind.Array ? root : (root.TryGetProperty("items", out var it) ? it : root);
        return JsonSerializer.Serialize(new
        {
            count = items.GetArrayLength(),
            recordings = items.EnumerateArray().Select(r => new
            {
                recordingId = r.GetProperty("recordingId").GetInt32(),
                cameraId = r.GetProperty("cameraId").GetInt32(),
                beginTime = r.GetProperty("beginTime").GetString(),
                endTime = r.GetProperty("endTime").GetString(),
            }),
            hint = "Pre potvrdenie pošli úsek užívateľovi a po jeho súhlase zavolaj person_reviewed {recordingId}.",
        }, Json);
    }

    /// <summary>S22l: person_reviewed — potvrdiť kontrolu nahrávky s osobou („skontrolované") → purge ju môže zmazať.</summary>
    [McpServerTool(Name = "person_reviewed")]
    public async Task<string> PersonReviewedAsync(int recordingId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"/api/v1/recordings/{recordingId}/person-reviewed", new { }, ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}" });
        return JsonSerializer.Serialize(new { ok = true, recordingId, personPending = false });
    }

    /// <summary>S22l: delete_recording — vymazať nahrávku (len po potvrdení užívateľa).</summary>
    [McpServerTool(Name = "delete_recording")]
    public async Task<string> DeleteRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"/api/v1/recordings/{recordingId}", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}" });
        return JsonSerializer.Serialize(new { ok = true, recordingId, deleted = true });
    }

    /// <summary>get_request_status — stav požiadavky + clipIds (poll po trigger_processing).</summary>
    [McpServerTool(Name = "get_request_status")]
    public async Task<string> GetRequestStatusAsync(int requestId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/api/v1/requests/{requestId}", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}" });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return JsonSerializer.Serialize(new
        {
            requestId,
            status = result.GetProperty("status").GetString(),
            estimate = result.GetProperty("estimate").GetString(),
            clipIds = result.GetProperty("clipIds").EnumerateArray().Select(e => e.GetInt32()),
            error = result.TryGetProperty("error", out var err) ? err.GetString() : null,
        }, Json);
    }

    /// <summary>search_recordings — záznamy v časovom rozsahu (segmenty, 15 min chunky).</summary>
    [McpServerTool(Name = "search_recordings")]
    public async Task<string> SearchRecordingsAsync(
        string fromTime,
        string toTime,
        int? cameraId = null,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"from={Uri.EscapeDataString(fromTime)}", $"to={Uri.EscapeDataString(toTime)}" };
        if (cameraId is not null) query.Add($"cameraId={cameraId}");

        using var response = await _http.GetAsync($"/api/v1/recordings?{string.Join('&', query)}", ct);
        if (!response.IsSuccessStatusCode)
            return JsonSerializer.Serialize(new { error = $"API {(int)response.StatusCode}" });
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var recordings = root.ValueKind == JsonValueKind.Array
            ? root
            : (root.TryGetProperty("items", out var items) ? items : root);
        return JsonSerializer.Serialize(new
        {
            count = recordings.GetArrayLength(),
            recordings = recordings.EnumerateArray().Select(r => new
            {
                recordingId = r.GetProperty("recordingId").GetInt32(),
                cameraId = r.GetProperty("cameraId").GetInt32(),
                beginTime = r.GetProperty("beginTime").GetString(),
                endTime = r.GetProperty("endTime").GetString(),
                durationSec = r.GetProperty("durationSec").GetInt32(),
                analysisState = r.GetProperty("analysisState").GetString(),
            }),
        }, Json);
    }
}
