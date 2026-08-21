using System.Net;
using System.Text.Json;
using WatchForge.Mcp;

namespace WatchForge.Mcp.Tests;

/// <summary>
/// S7-1: WatchForgeMcpTools — mapovanie MCP nástrojov na REST API v1.
/// Unit testy s mock HttpClient (bez reálneho API).
/// </summary>
public class McpToolsUnitTests
{
    /// <summary>Testovací HTTP handler — vráti vopred pripravené odpovede API.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private static WatchForgeMcpTools CreateTools(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        return new WatchForgeMcpTools(client);
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
    };

    [Test]
    public async Task SearchMotion_ReturnsDetectionsSummary()
    {
        // Given API vráti dve detekcie
        HttpRequestMessage? captured = null;
        var tools = CreateTools(request =>
        {
            captured = request;
            return JsonResponse(new[]
            {
                new { detectionId = 11, recordingId = 3, cameraId = 1, detectionType = "motion",
                      timestampMs = 5000, confidence = 0.92,
                      regionX = 0.33, regionY = 0.48, regionW = 0.11, regionH = 0.36 },
                new { detectionId = 12, recordingId = 3, cameraId = 1, detectionType = "motion",
                      timestampMs = 61000, confidence = 0.71,
                      regionX = 0.5, regionY = 0.5, regionW = 0.2, regionH = 0.2 },
            });
        });

        // When search_motion
        var result = await tools.SearchMotionAsync("2026-04-05T14:00:00Z", "2026-04-05T16:00:00Z", cameraId: 1, detectionType: "motion");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        // Then volá GET /api/v1/detections s filtrami
        await Assert.That(captured!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(captured.RequestUri!.PathAndQuery).IsEqualTo(
            "/api/v1/detections?from=2026-04-05T14%3A00%3A00Z&to=2026-04-05T16%3A00%3A00Z&cameraId=1&detectionType=motion");

        // A count + zjednodušené detekcie (pre agenta)
        await Assert.That(json.GetProperty("count").GetInt32()).IsEqualTo(2);
        var first = json.GetProperty("detections")[0];
        await Assert.That(first.GetProperty("type").GetString()).IsEqualTo("motion");
        await Assert.That(first.GetProperty("confidence").GetDouble()).IsEqualTo(0.92);
        await Assert.That(first.GetProperty("region").GetProperty("w").GetDouble()).IsEqualTo(0.11);
    }

    [Test]
    public async Task TriggerProcessing_RejectsAndReturnsAnalyzedWindows()
    {
        // S22l: trigger_processing už NESpúšťa analýzu — GET analyzed-windows + odmietnutie
        HttpRequestMessage? captured = null;
        var tools = CreateTools(request =>
        {
            captured = request;
            return JsonResponse(new[]
            {
                new { windowStart = "2026-04-05T14:45:00Z", windowEnd = "2026-04-05T15:00:00Z",
                      camerasAnalyzed = 8, totalDetections = 12 },
            });
        });

        // When trigger_processing (agent sa snaží spustiť analýzu)
        var result = await tools.TriggerProcessingAsync("2026-04-05T14:00:00Z", "2026-04-05T16:00:00Z", cameraId: 1);

        // Then GET /api/v1/recordings/analyzed-windows (žiadny POST) a allowed=false
        await Assert.That(captured!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(captured.RequestUri!.PathAndQuery).IsEqualTo("/api/v1/recordings/analyzed-windows");

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        await Assert.That(json.GetProperty("allowed").GetBoolean()).IsFalse();
        await Assert.That(json.GetProperty("reason").GetString()).IsNotEmpty();
        await Assert.That(json.GetProperty("analyzedWindows")[0].GetProperty("camerasAnalyzed").GetInt32()).IsEqualTo(8);
    }

    [Test]
    public async Task GetClip_ReturnsMetadataAndUrl()
    {
        // Given API vráti MP4 stream (hlavičky)
        var tools = CreateTools(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            response.Content.Headers.ContentLength = 3;
            return response;
        });

        var result = await tools.GetClipAsync(42);
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        await Assert.That(json.GetProperty("clipId").GetInt32()).IsEqualTo(42);
        await Assert.That(json.GetProperty("kind").GetString()).IsEqualTo("video");
        await Assert.That(json.GetProperty("contentType").GetString()).IsEqualTo("video/mp4");
        await Assert.That(json.GetProperty("sizeBytes").GetInt64()).IsEqualTo(3);
        await Assert.That(json.GetProperty("url").GetString()).IsEqualTo("http://api.test/api/v1/clips/42");
    }

    [Test]
    public async Task GetRequestStatus_ReturnsClipIds()
    {
        // Given API vráti completed request s clipmi
        var tools = CreateTools(_ => JsonResponse(new
        {
            requestId = 7, status = "completed", estimate = "~1 min",
            clipIds = new[] { 42, 43 }, error = (string?)null,
        }));

        var result = await tools.GetRequestStatusAsync(7);
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        await Assert.That(json.GetProperty("status").GetString()).IsEqualTo("completed");
        await Assert.That(json.GetProperty("clipIds").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task SearchMotion_ApiError_ReturnsErrorObject()
    {
        // Given API vráti 401 (token chýba)
        var tools = CreateTools(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await tools.SearchMotionAsync("2026-04-05T14:00:00Z", "2026-04-05T16:00:00Z");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        await Assert.That(json.GetProperty("error").GetString()).Contains("401");
    }
}
