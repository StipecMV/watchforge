using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.DVRIP.Library;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// S19: Live view — GET /api/v1/live/{cameraId}/frame (jeden JPEG z cache)
/// a GET /api/v1/live/{cameraId}/stream (MJPEG multipart, plynulý obraz).
/// S22o: adaptívny stream — frame/stream akceptujú ?w=&amp;fps= (rozlíšenie/fps podľa
/// režimu view 1 / view 8). Rôzne režimy = rôzne cache streamy; pri prepnutí sa
/// nepotrebný uvoľní (POST /release). Default (bez parametrov) = view 1 (1080p).
/// Obe používajú LiveFrameCache — persistentný OPMonitor stream na (kamera × rozlíšenie × fps),
/// takže prvý frame príde okamžite. MJPEG podporujú Chrome/Edge/Firefox; iOS Safari nie,
/// preto UI vie prepnúť na snapshot polling (GET .../frame).
/// </summary>
[ApiController]
[Route("api/v1/live")]
public sealed class LiveController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    ICameraRepository cameras,
    INvrRepository nvrs,
    LiveFrameCache frameCache) : ApiControllerBase(users, dataProtection, options)
{
    private const string MjpegBoundary = "ffmpeg";

    /// <summary>Rozlíšenie/fps pre GET frame/stream (view 1 default).</summary>
    private (int Width, int Fps) ResolveStreamParams(int? w, int? fps)
    {
        var width = w is > 0 ? w.Value : Options.Live.View1Width;
        var f = fps is > 0 ? fps.Value : Options.Live.View1Fps;
        return (width, Math.Clamp(f, 1, 25));
    }

    /// <summary>GET /api/v1/live/{cameraId}/frame?w=&amp;fps= — jeden JPEG frame (posledný z cache).</summary>
    [HttpGet("{cameraId:int}/frame")]
    public async Task Frame(int cameraId, [FromQuery] int? w = null, [FromQuery] int? fps = null, CancellationToken ct = default)
    {
        var resolved = await ResolveTargetAsync(cameraId, ct);
        if (resolved is not null)
        {
            var (width, frameFps) = ResolveStreamParams(w, fps);
            var jpeg = await frameCache.GetFrameAsync(cameraId, resolved.Options, resolved.Channel, width, frameFps, ct);
            if (jpeg is not null)
            {
                Response.ContentType = "image/jpeg";
                Response.Headers.CacheControl = "no-store";
                await Response.Body.WriteAsync(jpeg, ct);
                return;
            }
            Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await Response.WriteAsJsonAsync(new { error = "No frame received from NVR." }, ct);
            return;
        }
        await WriteErrorAsync(Response, resolved, ct);
    }

    /// <summary>
    /// GET /api/v1/live/{cameraId}/stream?w=&amp;fps=N — MJPEG multipart stream (multipart/x-mixed-replace).
    /// Posiela JPEG framy z cache v slučke. S22o: w/fps určujú rozlíšenie streamu (view 1 / view 8).
    /// Stream beží, kým klient neodpojí (ct zrušený).
    /// </summary>
    [HttpGet("{cameraId:int}/stream")]
    public async Task Stream(int cameraId, [FromQuery] int? w = null, [FromQuery] int? fps = null, CancellationToken ct = default)
    {
        var resolved = await ResolveTargetAsync(cameraId, ct);
        if (resolved is null)
        {
            await WriteErrorAsync(Response, resolved, ct);
            return;
        }

        var (width, frameFps) = ResolveStreamParams(w, fps);
        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / frameFps);

        Response.ContentType = $"multipart/x-mixed-replace; boundary={MjpegBoundary}";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Connection = "keep-alive";

        // Prvý frame — počkáme naň (stream sa spustí, ak ešte nebeží), potom
        // posielame ďalšie framy z cache v slučke (žiadne čakanie na keyframe).
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var jpeg = await frameCache.GetFrameAsync(cameraId, resolved.Options, resolved.Channel, width, frameFps, ct);
                if (jpeg is not null)
                {
                    var header = Encoding.ASCII.GetBytes(
                        $"--{MjpegBoundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
                    await Response.Body.WriteAsync(header, ct);
                    await Response.Body.WriteAsync(jpeg, ct);
                    await Response.Body.WriteAsync("\r\n"u8.ToArray(), ct);
                    await Response.Body.FlushAsync(ct);
                }
                await Task.Delay(frameDelay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // klient odpojil — normálne ukončenie
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[live-stream {cameraId}] error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// POST /api/v1/live/{cameraId}/release?w=&amp;fps= — zastaví stream kamery.
    /// S22o: ak sú zadané w/fps, uvoľní len ten režim; bez parametrov uvoľní všetky
    /// režimy kamery (pri zmene view 1↔8 — šetrí sieť/CPU, NVR upload je úzky).
    /// Pri ďalšom GET frame sa stream reštartuje.
    /// </summary>
    [HttpPost("{cameraId:int}/release")]
    public async Task<IActionResult> Release(int cameraId, [FromQuery] int? w = null, [FromQuery] int? fps = null, CancellationToken ct = default)
    {
        // Verejné — UI uvoľňuje streamy pri zmene view bez prihlásenia
        if (w is > 0 && fps is > 0)
            frameCache.Release(cameraId, w.Value, fps.Value);
        else
            frameCache.ReleaseAll(cameraId);
        return Ok(new { released = cameraId });
    }

    private sealed record ResolvedTarget(int Channel, DvripClientOptions Options);

    /// <summary>Auth + camera + NVR lookup + heslo z env. Vráti null, ak niečo chýba (Response nastavený).</summary>
    private async Task<ResolvedTarget?> ResolveTargetAsync(int cameraId, CancellationToken ct)
    {
        // Live view je VEREJNÝ (neprihlásený používateľ vidí živý obraz — žiadne nastavenia)
        var camera = (await cameras.GetAllAsync(null, ct)).FirstOrDefault(c => c.CameraId == cameraId);
        if (camera is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { error = $"Camera {cameraId} not found." }, ct);
            return null;
        }

        var nvr = (await nvrs.GetAllAsync(ct)).FirstOrDefault(n => n.NvrId == camera.NvrId);
        if (nvr is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { error = $"NVR for camera {cameraId} not found." }, ct);
            return null;
        }

        var password = string.IsNullOrEmpty(nvr.PasswordSecretEnv)
            ? null
            : Environment.GetEnvironmentVariable(nvr.PasswordSecretEnv);
        if (string.IsNullOrEmpty(password))
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(new { error = $"NVR password env '{nvr.PasswordSecretEnv}' not set." }, ct);
            return null;
        }

        return new ResolvedTarget(camera.Channel, new DvripClientOptions
        {
            Host = nvr.Host, Port = nvr.Port,
            Username = nvr.Username, Password = password,
            ReadTimeoutSeconds = 10,
        });
    }

    private static async Task WriteErrorAsync(HttpResponse response, ResolvedTarget? resolved, CancellationToken ct)
    {
        // resolved == null → chyba už bola zapísaná v ResolveTargetAsync
        if (resolved is not null)
        {
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new { error = "Unexpected error." }, ct);
        }
    }
}
