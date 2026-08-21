namespace WatchForge.Api;

/// <summary>
/// Konfigurácia API (S5-1). Sekcia "WatchForge:Api" — prepísateľné cez env vars
/// (WatchForge__Api__Cors__AllowedOrigins__0=...). Options pattern (architektúra §1):
/// registrácia cez AddOptions+Bind, vyhodnotenie lazy cez IOptions&lt;ApiOptions&gt;.
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "WatchForge:Api";

    /// <summary>Názov CORS policy pre web UI (kontajner 3) — interný detail, fixný.</summary>
    public const string DefaultCorsPolicyName = "watchforge-web";

    /// <summary>Povolené originy pre CORS (web UI kontajner / dev servery).</summary>
    public string[] CorsAllowedOrigins { get; set; } =
        ["http://localhost:4200", "http://localhost:5173"];

    /// <summary>
    /// Povoliť credentials (session cookie pre web UI auth, S5-2).
    /// true vylučuje AllowAnyOrigin — originy musia byť explicitné.
    /// </summary>
    public bool CorsAllowCredentials { get; set; } = true;

    /// <summary>Cesta k SQLite databáze (zdieľaná s Runnerom).</summary>
    public string DbPath { get; set; } = "watchforge.db";

    /// <summary>
    /// API token pre agenta (MCP, S5-2) — statický token z env
    /// (WatchForge__Api__ApiToken). Agent posiela header "X-Api-Token".
    /// Prázdny = token auth je vypnutý (len session cookie).
    /// </summary>
    public string ApiToken { get; set; } = "";

    /// <summary>Názov session cookie (S5-2).</summary>
    public string SessionCookieName { get; set; } = "wf_session";

    /// <summary>Platnosť session v hodinách (default 168 = 7 dní).</summary>
    public int SessionExpiryHours { get; set; } = 168;

    /// <summary>
    /// S22l: cesta k dočasným stiahnutým videám (zdieľaná s Runnerom) — pre DELETE
    /// nahrávky (lokálny .mp4). Env: WatchForge__Api__MediaTempDir.
    /// </summary>
    public string MediaTempDir { get; set; } = "watchforge-media/tmp";

    /// <summary>
    /// S22j: analýzy zapnuté/vypnuté (WatchForge__Api__Features__AnalysesEnabled).
    /// false = UI skryje analýzy (dashboard, sidebar button), runner nedostáva
    /// nové analyze joby. Použité na dočasné vypnutie analýz bez zmeny kódu.
    /// </summary>
    public bool AnalysesEnabled { get; set; } = true;

    /// <summary>
    /// S22o: Live view adaptívny stream — rozlíšenie/FPS per režim (view 1 / view 8).
    /// Env: WatchForge__Api__Live__View1Width, View1Fps, View8Width, View8Fps.
    /// </summary>
    public LiveViewOptions Live { get; set; } = new();

    public void Validate()
    {
        if (CorsAllowedOrigins.Length == 0)
            throw new ArgumentException("At least one CORS origin is required.", nameof(CorsAllowedOrigins));
        if (CorsAllowCredentials && CorsAllowedOrigins.Any(o => o == "*"))
            throw new ArgumentException("AllowCredentials cannot be combined with wildcard origin.", nameof(CorsAllowedOrigins));
        if (string.IsNullOrWhiteSpace(DbPath))
            throw new ArgumentException("DbPath is required.", nameof(DbPath));
        if (SessionExpiryHours < 1)
            throw new ArgumentOutOfRangeException(nameof(SessionExpiryHours), "Must be >= 1.");
    }
}

/// <summary>Live view stream parametre (S22o).</summary>
public sealed class LiveViewOptions
{
    /// <summary>View 1 (jedna kamera): šírka v px (1080p).</summary>
    public int View1Width { get; set; } = 1920;
    /// <summary>View 1 FPS.</summary>
    public int View1Fps { get; set; } = 10;
    /// <summary>View 8 (mriežka): šírka v px (360p).</summary>
    public int View8Width { get; set; } = 640;
    /// <summary>View 8 FPS (per kamera).</summary>
    public int View8Fps { get; set; } = 1;
}
