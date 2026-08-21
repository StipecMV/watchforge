using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WatchForge.Interfaces.Library;

namespace WatchForge.Api;

/// <summary>
/// Systémové endpointy (S5-1 scaffold): health check pre deployment (compose healthcheck, S9)
/// a operatívne sledovanie.
/// </summary>
[ApiController]
[Route("api/v1/system")]
public sealed class SystemController(
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    IOptions<ApiOptions> options,
    INvrRepository nvrs,
    IJobRepository jobs,
    IRecordingRepository recordings) : ApiControllerBase(users, dataProtection, options)
{
    /// <summary>GET /api/v1/system/health — liveness/readiness (200 + status "ok").</summary>
    [HttpGet("health")]
    public ActionResult<HealthResponse> Health()
    {
        var version = typeof(SystemController).Assembly
            .GetName().Version?.ToString() ?? "0.0.0";
        return Ok(new HealthResponse("ok", version, DateTimeOffset.UtcNow));
    }

    public sealed record NvrInfoDto(int NvrId, string SiteId, string Host, int Port, string Username, string PasswordSecretEnv);

    public sealed record WorkerInfoDto(int Queued, int Running, int Completed, int Failed, int Interrupted, int Cancelled, int Total);

    /// <summary>S22g: jedna služba/kontajner WatchForge (api/runner/web) + verzia implementácie.</summary>
    public sealed record ServiceInfoDto(string Name, string Status, string Version, int Port);

    public sealed record SystemInfoDto(
        NvrInfoDto Nvr, WorkerInfoDto Worker, string ApiVersion, DateTimeOffset ServerTimeUtc,
        IReadOnlyList<ServiceInfoDto> Services);

    /// <summary>S12-1: stav synchronizácie/backlogu pre system/status (FR-11).</summary>
    public sealed record SyncStatusDto(
        int Backlog, DateTimeOffset? LastSyncUtc, int TotalRecordings, int CompletedAnalyses);

    /// <summary>S12-1: kompletný status systému (admin).</summary>
    public sealed record SystemStatusDto(
        WorkerInfoDto Worker, SyncStatusDto Sync, string ApiVersion, DateTimeOffset ServerTimeUtc,
        bool AnalysesEnabled);

    /// <summary>
    /// GET /api/v1/system/status — VEREJNÝ status (štatistiky analyz pre UI):
    /// worker summary (koľko analyz beží / čaká v poradovníku) + sync/backlog.
    /// Bez NVR detailov (tie sú v /info, admin-only). Neprihlásený používateľ
    /// vidí koľko analyz prebieha a koľko je v poradovníku.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusDto>> Status(CancellationToken ct)
    {
        var summary = await jobs.GetStatusSummaryAsync(ct);
        var backlog = await recordings.CountBacklogAsync(ct);
        var lastSync = await recordings.GetLastSyncAsync(ct);
        var (totalRecordings, completedAnalyses) = await recordings.CountAllAndCompletedAsync(ct);
        var version = typeof(SystemController).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        return Ok(new SystemStatusDto(
            new WorkerInfoDto(
                summary.Queued, summary.Running, summary.Completed, summary.Failed,
                summary.Interrupted, summary.Cancelled, summary.Total),
            new SyncStatusDto(
                backlog,
                lastSync is null ? null : new DateTimeOffset(DateTime.SpecifyKind(lastSync.Value, DateTimeKind.Utc)),
                totalRecordings,
                completedAnalyses),
            version,
            DateTimeOffset.UtcNow,
            Options.AnalysesEnabled));
    }

    /// <summary>
    /// GET /api/v1/system/info — System sekcia Settings (S6-5, admin):
    /// NVR pripojenie (bez hesla — secret je v env, NFR-05) + worker job summary
    /// + verzia API + detaily všetkých služieb (api/runner/web — stav + verzia).
    /// </summary>
    [HttpGet("info")]
    public async Task<ActionResult<SystemInfoDto>> Info(CancellationToken ct)
    {
        var user = await AuthenticatedUserAsync(ct);
        if (user is null)
            return Unauthorized(new { error = "Authentication required (session or X-Api-Token)." });
        if (user.Role != "admin")
            return Forbidden();

        var nvr = (await nvrs.GetAllAsync(ct)).FirstOrDefault();
        if (nvr is null)
            return NotFound(new { error = "No NVR configured." });

        var summary = await jobs.GetStatusSummaryAsync(ct);
        var version = typeof(SystemController).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var services = CollectServices();

        return Ok(new SystemInfoDto(
            new NvrInfoDto(nvr.NvrId, nvr.SiteId, nvr.Host, nvr.Port, nvr.Username, nvr.PasswordSecretEnv),
            new WorkerInfoDto(
                summary.Queued, summary.Running, summary.Completed, summary.Failed,
                summary.Interrupted, summary.Cancelled, summary.Total),
            version,
            DateTimeOffset.UtcNow,
            services));
    }

    /// <summary>
    /// S22g: stav + verzia implementácie všetkých služieb WatchForge
    /// (systemd user služby: watchforge-api, watchforge-runner, watchforge-web).
    /// Verzia = assembly verzia danej služby; stav cez `systemctl --user is-active`.
    /// </summary>
    private static IReadOnlyList<ServiceInfoDto> CollectServices()
    {
        var services = new[]
        {
            ("api", "WatchForge.Api", 5000),
            ("runner", "WatchForge.Runner", 8081),
            ("web", "WatchForge.UI", 4200),
        };

        var result = new List<ServiceInfoDto>(services.Length);
        foreach (var (name, assemblyName, port) in services)
        {
            var status = "unknown";
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = $"--user is-active watchforge-{name}.service",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                if (proc is not null)
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    status = string.IsNullOrEmpty(output) ? "unknown" : output;
                }
            }
            catch { /* systemctl nedostupný — status zostane unknown */ }

            var version = "?";
            try
            {
                var asm = System.Reflection.Assembly.Load($"WatchForge.{assemblyName}");
                version = asm.GetName().Version?.ToString() ?? "?";
            }
            catch { /* assembly nie je načítaná v API procese — verzia zostane ? */ }

            result.Add(new ServiceInfoDto(name, status, version, port));
        }
        return result;
    }
}

/// <summary>Odpoveď health endpointu (camelCase serializácia).</summary>
public sealed record HealthResponse(string Status, string Version, DateTimeOffset ServerTimeUtc);
