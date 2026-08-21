using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// GET /api/v1/users — správa používateľov (admin) (S5-7). Zoznam fixných userov;
/// reset hesla je v auth/reset-password.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options) : ApiControllerBase(users, dataProtection, options)
{
    public sealed record UserDto(int UserId, string Username, string Role, int AvatarId, string Locale, bool HasPassword);

    /// <summary>GET /api/v1/users — zoznam používateľov (len admin/agent token).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var result = await Users.GetAllAsync(ct);
        return Ok(result.Select(u => new UserDto(
            u.UserId, u.Username, u.Role, u.AvatarId, u.Locale, !string.IsNullOrEmpty(u.PasswordHash))).ToList());
    }
}
