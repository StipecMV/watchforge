using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace WatchForge.Api;

/// <summary>
/// Scaffold vzorka (S5-1) — demonštruje ApiController konvencie: automatická validácia
/// modelu → 400 ProblemDetails, JSON serializácia. Používa ju test ScaffoldTests
/// (error handling, validácia). Odstráni sa po nasadení reálnych endpointov (S5-3+).
/// </summary>
[ApiController]
[Route("api/v1/_scaffold")]
public sealed class ScaffoldSampleController : ControllerBase
{
    /// <summary>POST /api/v1/_scaffold/echo — overenie validácie + JSON kontraktu.</summary>
    [HttpPost("echo")]
    public ActionResult<EchoResponse> Echo([FromBody] EchoRequest request)
        => Ok(new EchoResponse(request.Value));

    /// <summary>GET /api/v1/_scaffold/boom — vyhodí výnimku (test 500 ProblemDetails).</summary>
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("Scaffold boom.");

    public sealed class EchoRequest
    {
        [Required(AllowEmptyStrings = false)]
        public string? Value { get; set; }
    }

    public sealed record EchoResponse(string? Value);
}
