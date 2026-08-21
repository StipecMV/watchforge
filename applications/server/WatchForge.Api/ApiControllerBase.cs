using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// Základ pre API controllery (S5-3+): spoločná auth logika
/// (session cookie alebo X-Api-Token) + pohodlné DTO mapovanie.
/// </summary>
[ApiController]
public abstract class ApiControllerBase(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options) : ControllerBase
{
    protected ApiOptions Options => options.Value;

    /// <summary>Repozitár používateľov (S12: dedičia ho používajú namiesto capture z primary ctor — CS9107).</summary>
    protected IUserRepository Users => users;

    /// <summary>Vráti autentifikovaného používateľa; null = neoverený request.</summary>
    protected Task<User?> AuthenticatedUserAsync(CancellationToken ct)
        => ApiAuth.GetAuthenticatedUserAsync(Request, dataProtection, Options, users, ct);

    /// <summary>
    /// 403 pre non-admin (Settings → Users/System). Explicitný StatusCode namiesto
    /// Forbid() — API nemá nakonfigurovaný authentication scheme (session cookie je
    /// vlastná cez ApiAuth), takže ForbidResult by hádzal InvalidOperationException.
    /// ObjectResult (nie IActionResult) — implicitná konverzia na ActionResult&lt;T&gt;.
    /// </summary>
    protected ObjectResult Forbidden(string error = "Administrator access required.")
        => StatusCode(StatusCodes.Status403Forbidden, new { error });
}
