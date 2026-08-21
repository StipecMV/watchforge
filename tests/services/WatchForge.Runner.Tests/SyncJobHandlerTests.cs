using Moq;
using WatchForge.Interfaces.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>S4-2: SyncJobHandler — payload parsing a vykonanie sync jobu.</summary>
public class SyncJobHandlerTests
{
    private static SyncJobHandler CreateHandler()
        // ParseRange nevolá synchronizer — null! je bezpečný pre tieto testy
        => new(null!, Mock.Of<IClock>(), new RunnerOptions());

    [Test]
    public async Task ParseRange_PayloadProvided_UsesIt()
    {
        // Given payload s rozsahom
        const string payload = """{"from":"2026-04-05T14:00:00Z","to":"2026-04-05T16:00:00Z"}""";
        var now = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc);

        // When parse
        var (from, to) = CreateHandler().ParseRange(payload, now);

        // Then použitý payload, nie default
        await Assert.That(from).IsEqualTo(new DateTime(2026, 4, 5, 14, 0, 0, DateTimeKind.Utc));
        await Assert.That(to).IsEqualTo(new DateTime(2026, 4, 5, 16, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task ParseRange_EmptyPayload_UsesRetentionWindows()
    {
        // Given prázdny payload — S22l: len retenčné okná (4×15 + 15 rezerva = 75 min)
        var now = new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc);

        // When parse
        var (from, to) = CreateHandler().ParseRange(null, now);

        // Then default = retenčné okná, NIE 24h
        await Assert.That(from).IsEqualTo(now - TimeSpan.FromMinutes(75));
        await Assert.That(to).IsEqualTo(now);
    }

    [Test]
    public async Task ParseRange_InvalidJson_FallsBackToDefault()
    {
        // Given nevalidný payload
        var now = new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc);

        // When parse
        var (from, to) = CreateHandler().ParseRange("not-json", now);

        // Then default = retenčné okná
        await Assert.That(from).IsEqualTo(now - TimeSpan.FromMinutes(75));
    }

    [Test]
    public async Task Execute_SyncJob_CompletesWithSummary()
    {
        // Given fake NVR s jednou nahrávkou + DB s NVR/kamerou
        var entry = new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 15, 15, 0), 100);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();
        var passwordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(passwordEnvVar, "secret");

        var dbPath = Path.Combine(Path.GetTempPath(), "watchforge-synchandler-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var connection = await WatchForge.Processing.Library.WatchForgeDatabase.OpenAsync(dbPath);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
                    VALUES ('site-a', '127.0.0.1', 34567, 'admin', '{passwordEnvVar}');
                    INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
                    VALUES (1, 0, 'Dvor', 'camera', 1);
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var synchronizer = new NvrSynchronizer(
                new WatchForge.Processing.Library.NvrRepository(connection),
                new WatchForge.Processing.Library.CameraRepository(connection),
                new WatchForge.Processing.Library.RecordingRepository(connection),
                new SystemClock(),
                opts => server.CreateClient(opts.Username, opts.Password));
            var handler = new SyncJobHandler(synchronizer, new SystemClock(), new RunnerOptions());

            var job = new Job
            {
                JobId = 1, Type = JobType.Sync, Priority = JobPriority.System, Source = JobSource.System,
                Payload = """{"from":"2026-04-05T14:00:00Z","to":"2026-04-05T16:00:00Z"}""",
            };

            // When vykonáme sync job
            await handler.ExecuteAsync(job, CancellationToken.None);

            // Then job má progress 100 a sumár v Error
            await Assert.That(job.Progress).IsEqualTo(100);
            await Assert.That(job.Error).Contains("inserted=1");
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordEnvVar, null);
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}
