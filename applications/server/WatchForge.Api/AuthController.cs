using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

namespace WatchForge.Api;

/// <summary>
/// Autentifikácia (S5-2):
/// - POST /login — username + heslo (PBKDF2) → 200 + session cookie; 401 zlé prihlásenie;
///   428 ak používateľ ešte nemá nastavené heslo (prvý login).
/// - POST /set-password — prvé nastavenie hesla (len ak hash je prázdny).
/// - POST /reset-password — admin: vymaže hash (používateľ si nastaví nové pri ďalšom logine).
/// - POST /logout — zruší session cookie.
/// - GET /me — aktuálny používateľ (session cookie alebo X-Api-Token).
/// Agent (MCP) sa autentifikuje X-Api-Token headerom — žiadna session.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options) : ControllerBase
{
    private ApiOptions Options => options.Value;

    public sealed record LoginRequest([Required] string? Username, [Required] string? Password);

    public sealed record SetPasswordRequest([Required] string? Username, [Required, MinLength(8)] string? NewPassword);

    public sealed record ResetPasswordRequest([Required] string? Username);

    public sealed record UpdateProfileRequest([Range(1, 10)] int AvatarId, [RegularExpression("^(sk|en)$")] string? Locale);

    public sealed record ChangePasswordRequest([Required] string? CurrentPassword, [Required, MinLength(8)] string? NewPassword);

    public sealed record UserDto(int UserId, string Username, string Role, int AvatarId, string Locale);

    /// <summary>POST /api/v1/auth/login — prihlásenie (session cookie).</summary>
    [HttpPost("login")]
    public async Task<ActionResult<UserDto>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(request.Username ?? "", ct);
        if (user is null)
            return Unauthorized(new { error = "Invalid username or password." });

        // Prvý login: heslo ešte nebolo nastavené (hash prázdny) — treba set-password
        if (string.IsNullOrEmpty(user.PasswordHash))
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                new { error = "Password not set. Use POST /auth/set-password first.", username = user.Username });

        if (!UserRepository.VerifyPassword(request.Password ?? "", user.PasswordHash))
            return Unauthorized(new { error = "Invalid username or password." });

        SetSessionCookie(user.UserId);
        return Ok(ToDto(user));
    }

    /// <summary>POST /api/v1/auth/set-password — prvé nastavenie hesla (hash prázdny).</summary>
    [HttpPost("set-password")]
    public async Task<ActionResult<UserDto>> SetPassword([FromBody] SetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(request.Username ?? "", ct);
        if (user is null)
            return NotFound(new { error = "User not found." });
        if (!string.IsNullOrEmpty(user.PasswordHash))
            return Conflict(new { error = "Password already set. Use reset (admin) if forgotten." });

        await users.UpdatePasswordHashAsync(user.UserId, UserRepository.HashPassword(request.NewPassword!), ct);
        var updated = await users.GetByUsernameAsync(user.Username, ct);
        SetSessionCookie(user.UserId);
        return Ok(ToDto(updated!));
    }

    /// <summary>POST /api/v1/auth/reset-password — admin vymaže heslo používateľa.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var admin = await ApiAuth.GetAuthenticatedUserAsync(Request, dataProtection, Options, users, ct);
        if (admin is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (admin.Role != "admin")
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator access required." });

        var user = await users.GetByUsernameAsync(request.Username ?? "", ct);
        if (user is null)
            return NotFound(new { error = "User not found." });

        await users.ResetPasswordAsync(user.UserId, ct);
        return Ok(new { ok = true, username = user.Username, message = "Password reset — user must set a new one on next login." });
    }

    /// <summary>POST /api/v1/auth/logout — zruší session cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(Options.SessionCookieName);
        return Ok(new { ok = true });
    }

    /// <summary>GET /api/v1/auth/me — aktuálny používateľ (session alebo API token).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await ApiAuth.GetAuthenticatedUserAsync(Request, dataProtection, Options, users, ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        return Ok(ToDto(user));
    }

    /// <summary>
    /// PUT /api/v1/auth/me — úprava profilu (S6-5 Settings → Profile):
    /// avatar (1..10 glyfov) + locale (sk|en). Iba prihlásený používateľ — svoj profil.
    /// </summary>
    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await ApiAuth.GetAuthenticatedUserAsync(Request, dataProtection, Options, users, ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        await users.UpdateProfileAsync(user.UserId, request.AvatarId, request.Locale ?? "sk", ct);
        var updated = await users.GetByUsernameAsync(user.Username, ct);
        return Ok(ToDto(updated!));
    }

    /// <summary>
    /// POST /api/v1/auth/change-password — zmena hesla prihláseného používateľa (S6-5 Settings → Security).
    /// Vyžaduje aktuálne heslo (overí PBKDF2) + nové (min 8 znakov). Session ostáva platná.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await ApiAuth.GetAuthenticatedUserAsync(Request, dataProtection, Options, users, ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });

        if (string.IsNullOrEmpty(user.PasswordHash))
            return BadRequest(new { error = "Password is not set yet — use set-password on first sign-in." });
        if (!UserRepository.VerifyPassword(request.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest(new { error = "Current password is incorrect." });

        await users.UpdatePasswordHashAsync(user.UserId, UserRepository.HashPassword(request.NewPassword!), ct);
        return Ok(new { ok = true, message = "Password updated." });
    }

    private void SetSessionCookie(int userId)
    {
        var value = ApiAuth.CreateSessionCookie(dataProtection, Options, userId);
        Response.Cookies.Append(Options.SessionCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false, // lokálna LAN HTTP; cez reverse proxy v produkcii nastaviť true
            Path = "/",
        });
    }

    private static UserDto ToDto(User user) => new(user.UserId, user.Username, user.Role, user.AvatarId, user.Locale);
}
