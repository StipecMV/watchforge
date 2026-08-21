using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// Autentifikácia API (S5-2):
/// - Session cookie: podpísaná cez ASP.NET DataProtection (TimeLimited) — obsahuje userId.
/// - API token pre agenta (MCP): statický token z ApiOptions.ApiToken, header "X-Api-Token",
///   constant-time porovnanie.
/// </summary>
public static class ApiAuth
{
    public const string ApiTokenHeader = "X-Api-Token";
    private const string SessionPurpose = "wf-session";

    /// <summary>Vytvorí podpísanú session cookie hodnotu pre daného používateľa.</summary>
    public static string CreateSessionCookie(IDataProtectionProvider dataProtection, ApiOptions options, int userId)
        => dataProtection.CreateProtector(SessionPurpose)
            .ToTimeLimitedDataProtector()
            .Protect(userId.ToString(), TimeSpan.FromHours(options.SessionExpiryHours));

    /// <summary>Prečíta userId zo session cookie; null pri neplatnej/expirovanej hodnote.</summary>
    public static int? ReadSessionCookie(IDataProtectionProvider dataProtection, string? cookieValue)
    {
        if (string.IsNullOrEmpty(cookieValue)) return null;
        try
        {
            var payload = dataProtection.CreateProtector(SessionPurpose)
                .ToTimeLimitedDataProtector()
                .Unprotect(cookieValue);
            return int.TryParse(payload, out var userId) ? userId : null;
        }
        catch (CryptographicException)
        {
            return null; // neplatná alebo expirovaná cookie
        }
    }

    /// <summary>Overí API token (agent) constant-time; false ak token auth nie je zapnutý.</summary>
    public static bool IsValidApiToken(ApiOptions options, string? token)
    {
        if (string.IsNullOrEmpty(options.ApiToken) || string.IsNullOrEmpty(token)) return false;
        var expected = Encoding.UTF8.GetBytes(options.ApiToken);
        var actual = Encoding.UTF8.GetBytes(token);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Získa userId z requestu (API token alebo session cookie); null = neoverený.</summary>
    public static int? GetAuthenticatedUserId(HttpRequest request, IDataProtectionProvider dataProtection, ApiOptions options)
    {
        var token = request.Headers[ApiTokenHeader].FirstOrDefault();
        if (IsValidApiToken(options, token))
            return 0; // 0 = agent/API token identita (nie konkrétny user)

        var cookie = request.Cookies[options.SessionCookieName];
        return ReadSessionCookie(dataProtection, cookie);
    }

    /// <summary>Načíta autentifikovaného používateľa; null pri neoverenom requeste.</summary>
    public static async Task<User?> GetAuthenticatedUserAsync(
        HttpRequest request, IDataProtectionProvider dataProtection, ApiOptions options, IUserRepository users, CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId(request, dataProtection, options);
        if (userId is null) return null;

        if (userId == 0)
        {
            // API token (agent) → vystupuje ako prvý admin používateľ (plný prístup,
            // reálny UserId pre FK v USERS — persist/annotations/flag).
            var all = await users.GetAllAsync(ct);
            return all.FirstOrDefault(u => u.Role == "admin") ?? all.FirstOrDefault();
        }

        foreach (var user in await users.GetAllAsync(ct))
        {
            if (user.UserId == userId) return user;
        }
        return null;
    }
}
