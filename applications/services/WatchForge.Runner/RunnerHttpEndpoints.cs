using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WatchForge.Interfaces.Library;

namespace WatchForge.Runner;

/// <summary>
/// DTO pre /internal endpointy (S4-6).
/// </summary>
public static class RunnerHttpEndpoints
{
    public sealed record HealthResponse(string Status, string Version, DateTime StartedAtUtc, TimeSpan Uptime, int HttpPort);

    public sealed record JobInfo(int JobId, string Type, string Status, string Source, int Priority, int Progress, string? Error);

    public sealed record JobsResponse(JobStatusSummary Summary, IReadOnlyList<JobInfo> Running);

    /// <summary>Mapuje internal endpointy (liveness + job stav).</summary>
    public static void Map(WebApplication app, RunnerOptions options)
    {
        var startedAt = DateTime.UtcNow;

        app.MapGet("/internal/health", () => Results.Ok(new HealthResponse(
            Status: "ok",
            Version: "0.1.0",
            StartedAtUtc: startedAt,
            Uptime: DateTime.UtcNow - startedAt,
            HttpPort: options.HttpPort)));

        app.MapGet("/internal/jobs", async (IJobRepository repository, CancellationToken ct) =>
        {
            var summary = await repository.GetStatusSummaryAsync(ct);
            var running = (await repository.GetRunningAsync(ct))
                .Select(j => new JobInfo(j.JobId, j.Type.ToString(), j.Status.ToString(), j.Source.ToString(),
                    j.Priority, j.Progress, j.Error))
                .ToList();
            return Results.Ok(new JobsResponse(summary, running));
        });

        app.MapGet("/internal/jobs/{id:int}", async (int id, IJobRepository repository, CancellationToken ct) =>
        {
            var job = await repository.GetByIdAsync(id, ct);
            if (job is null)
                return Results.NotFound(new { error = $"Job {id} not found." });
            return Results.Ok(new JobInfo(job.JobId, job.Type.ToString(), job.Status.ToString(), job.Source.ToString(),
                job.Priority, job.Progress, job.Error));
        });
    }
}
