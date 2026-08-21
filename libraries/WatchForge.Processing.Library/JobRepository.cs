using Microsoft.Data.Sqlite;
using WatchForge.Interfaces.Library;

namespace WatchForge.Processing.Library;

/// <summary>
/// Repository nad tabuľkou JOBS (FR-08 job orchestration).
/// Worker loop polluje ClaimNextAsync; reštart cez MarkInterruptedAsync.
/// </summary>
public sealed class JobRepository(SqliteConnection connection) : IJobRepository
{
    // Thread-safe prístup: každá operácia si otvorí vlastné spojenie (zdieľaný
    // SqliteConnection nie je thread-safe — Runner claimuje/updatuje paralelne).
    private readonly string _connectionString = connection.ConnectionString;
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        => await WatchForgeDatabase.OpenConnectionStringAsync(_connectionString, ct);
    public async Task<Job> EnqueueAsync(Job job, CancellationToken ct = default)
    {
        job.CreatedAt = DateTime.UtcNow;
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO JOBS (recording_id, request_id, type, priority, source, status, payload, progress, attempts, error, created_at)
            VALUES ($recordingId, $requestId, $type, $priority, $source, $status, $payload, $progress, $attempts, $error, $createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$recordingId", (object?)job.RecordingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$requestId", (object?)job.RequestId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", job.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$priority", job.Priority);
        cmd.Parameters.AddWithValue("$source", job.Source.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$status", job.Status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$payload", (object?)job.Payload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$progress", job.Progress);
        cmd.Parameters.AddWithValue("$attempts", job.Attempts);
        cmd.Parameters.AddWithValue("$error", (object?)job.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O"));

        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        job.JobId = (int)id;
        return job;
    }

    public async Task<Job?> ClaimNextAsync(int maxPriority = int.MaxValue, CancellationToken ct = default)
        => await ClaimNextCoreAsync(typeFilter: null, maxPriority, ct);

    public async Task<Job?> ClaimNextByTypeAsync(JobType type, int maxPriority = int.MaxValue, CancellationToken ct = default)
        => await ClaimNextCoreAsync(typeFilter: type.ToString().ToLowerInvariant(), maxPriority, ct);

    private async Task<Job?> ClaimNextCoreAsync(string? typeFilter, int maxPriority, CancellationToken ct)
    {
        // Vyzdvihne najvyššiu prioritu (status=queued, prípadne daný typ), označí running.
        // Jedna transakcia — dva workeri nemôžu claimnúť ten istý job.
        await using var conn = await OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            long id;
            await using (var select = conn.CreateCommand())
            {
                select.Transaction = (SqliteTransaction)tx;
                select.CommandText = typeFilter is null
                    ? """
                    SELECT job_id FROM JOBS
                    WHERE status = 'queued' AND priority <= $maxPriority
                    ORDER BY priority DESC, created_at ASC
                    LIMIT 1;
                    """
                    : """
                    SELECT job_id FROM JOBS
                    WHERE status = 'queued' AND type = $type AND priority <= $maxPriority
                    ORDER BY priority DESC, created_at ASC
                    LIMIT 1;
                    """;
                select.Parameters.AddWithValue("$maxPriority", maxPriority);
                if (typeFilter is not null)
                    select.Parameters.AddWithValue("$type", typeFilter);
                var scalar = await select.ExecuteScalarAsync(ct);
                if (scalar is null)
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
                id = Convert.ToInt64(scalar);
            }

            await using (var update = conn.CreateCommand())
            {
                update.Transaction = (SqliteTransaction)tx;
                update.CommandText = """
                    UPDATE JOBS
                    SET status = 'running', started_at = $startedAt
                    WHERE job_id = $id AND status = 'queued';
                    """;
                update.Parameters.AddWithValue("$startedAt", DateTime.UtcNow.ToString("O"));
                update.Parameters.AddWithValue("$id", id);
                var affected = await update.ExecuteNonQueryAsync(ct);
                if (affected == 0)
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
            }

            await tx.CommitAsync(ct);
            return await GetByIdAsync((int)id, ct);
        }
        catch
        {
            // Commit už prebehol (GetByIdAsync po commite) → transakciu nerollbackovať
            if (conn.State == System.Data.ConnectionState.Open)
                try { await tx.RollbackAsync(ct); } catch (InvalidOperationException) { /* už dokončená */ }
            throw;
        }
    }

    public async Task<Job?> GetByIdAsync(int jobId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JOBS WHERE job_id = $id;";
        cmd.Parameters.AddWithValue("$id", jobId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapJob(reader) : null;
    }

    public async Task<Job?> PeekNextByTypeAsync(JobType type, CancellationToken ct = default)
    {
        // S20 (FR-08): pozrie najvyššiu prioritu queued jobu DANÉHO typu BEZ claimu
        // (žiadna zmena stavu) — RunnerService ju používa na rozhodnutie o preempcii.
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM JOBS
            WHERE status = 'queued' AND type = $type
            ORDER BY priority DESC, created_at ASC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$type", type.ToString().ToLowerInvariant());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapJob(reader) : null;
    }

    public async Task RequeueInterruptedJobAsync(int jobId, CancellationToken ct = default)
    {
        // S20 (FR-08): preempcia — jeden prerušený job sa vráti do frontu
        // (running → interrupted → queued; stavový model povoľuje interrupted → queued).
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE JOBS SET status = 'interrupted' WHERE job_id = $id AND status = 'running';
            UPDATE JOBS SET status = 'queued' WHERE job_id = $id AND status = 'interrupted';
            """;
        cmd.Parameters.AddWithValue("$id", jobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE JOBS
            SET status = $status, progress = $progress, attempts = $attempts,
                error = $error, started_at = $startedAt, finished_at = $finishedAt
            WHERE job_id = $id;
            """;
        cmd.Parameters.AddWithValue("$status", job.Status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$progress", job.Progress);
        cmd.Parameters.AddWithValue("$attempts", job.Attempts);
        cmd.Parameters.AddWithValue("$error", (object?)job.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$startedAt", (object?)job.StartedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finishedAt", (object?)job.FinishedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", job.JobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Job>> GetRunningAsync(CancellationToken ct = default)
    {
        var result = new List<Job>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JOBS WHERE status = 'running' ORDER BY job_id;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(MapJob(reader));
        return result;
    }

    /// <summary>Či už existuje aktívny job daného typu (queued/running) — dedup pre auto-plánovač.</summary>
    public async Task<bool> HasActiveJobAsync(JobType type, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM JOBS WHERE type = $type AND status IN ('queued', 'running');";
        cmd.Parameters.AddWithValue("$type", type.ToString().ToLowerInvariant());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>S22l: vymaže joby patriace záznamu (pre purge — FK mazanie pred recordings).</summary>
    public async Task DeleteForRecordingAsync(int recordingId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM JOBS WHERE recording_id = $id;";
        cmd.Parameters.AddWithValue("$id", recordingId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Čas dokončenia posledného jobu daného typu (completed); null ak žiadny nie je.</summary>
    public async Task<DateTime?> GetLastCompletedAtAsync(JobType type, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(finished_at) FROM JOBS WHERE type = $type AND status = 'completed';";
        cmd.Parameters.AddWithValue("$type", type.ToString().ToLowerInvariant());
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DBNull or null ? null : DateTime.Parse((string)value, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public async Task<JobStatusSummary> GetStatusSummaryAsync(CancellationToken ct = default)
    {
        var counts = new Dictionary<string, int>
        {
            ["queued"] = 0, ["running"] = 0, ["completed"] = 0,
            ["failed"] = 0, ["interrupted"] = 0, ["cancelled"] = 0,
        };

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // S22m: stats-bar ukazuje LEN Analyze joby (purge/sync/klip by kazili čísla —
        // purge+sync bežia každých 15 min a nafukujú „hotové").
        cmd.CommandText = "SELECT status, COUNT(*) FROM JOBS WHERE type = 'analyze' GROUP BY status;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetString(0)] = reader.GetInt32(1);

        return new JobStatusSummary(
            Queued: counts["queued"],
            Running: counts["running"],
            Completed: counts["completed"],
            Failed: counts["failed"],
            Interrupted: counts["interrupted"],
            Cancelled: counts["cancelled"]);
    }

    public async Task MarkInterruptedAsync(CancellationToken ct = default)
    {
        // Reštart: všetky running joby → interrupted (traceability)
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE JOBS SET status = 'interrupted' WHERE status = 'running';";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RequeueInterruptedAsync(CancellationToken ct = default)
    {
        // Preplánovanie po reštarte: interrupted → queued (stavový model: interrupted → queued)
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE JOBS SET status = 'queued' WHERE status = 'interrupted';";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Job MapJob(SqliteDataReader reader) => new()
    {
        JobId = reader.GetInt32(reader.GetOrdinal("job_id")),
        RecordingId = reader.IsDBNull(reader.GetOrdinal("recording_id"))
            ? null : reader.GetInt32(reader.GetOrdinal("recording_id")),
        RequestId = reader.IsDBNull(reader.GetOrdinal("request_id"))
            ? null : reader.GetInt32(reader.GetOrdinal("request_id")),
        Type = Enum.Parse<JobType>(reader.GetString(reader.GetOrdinal("type")), ignoreCase: true),
        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
        Source = Enum.Parse<JobSource>(reader.GetString(reader.GetOrdinal("source")), ignoreCase: true),
        Status = Enum.Parse<JobStatus>(reader.GetString(reader.GetOrdinal("status")), ignoreCase: true),
        Payload = reader.IsDBNull(reader.GetOrdinal("payload"))
            ? null : reader.GetString(reader.GetOrdinal("payload")),
        Progress = reader.GetInt32(reader.GetOrdinal("progress")),
        Attempts = reader.GetInt32(reader.GetOrdinal("attempts")),
        Error = reader.IsDBNull(reader.GetOrdinal("error"))
            ? null : reader.GetString(reader.GetOrdinal("error")),
        CreatedAt = reader.IsDBNull(reader.GetOrdinal("created_at"))
            ? DateTime.UtcNow : DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at"))
            ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
        FinishedAt = reader.IsDBNull(reader.GetOrdinal("finished_at"))
            ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("finished_at"))),
    };
}
