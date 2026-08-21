using Microsoft.Data.Sqlite;
using WatchForge.DVRIP.Library;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Testing.FakeNvr;

namespace WatchForge.Runner.Tests;

/// <summary>
/// S4-2: NvrSynchronizer — backlog, availability, missed_during_outage.
/// Integračné testy: fake NVR server + reálna SQLite (temp súbor).
/// </summary>
public class NvrSynchronizerTests : IAsyncDisposable
{
    // Unikátny názov env var per test inštancia — TUnit beží triedy paralelne
    // a zdieľaná env var by sa navzájom prepisovala.
    private readonly string PasswordEnvVar = "WF_TEST_NVR_PASSWORD_" + Guid.NewGuid().ToString("N");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "watchforge-sync-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteConnection? _connection;

    public NvrSynchronizerTests()
    {
        Environment.SetEnvironmentVariable(PasswordEnvVar, "secret");
    }

    private async Task<SqliteConnection> DbAsync()
    {
        _connection = await WatchForgeDatabase.OpenAsync(_dbPath);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
            VALUES ('site-a', '127.0.0.1', 34567, 'admin', '{PasswordEnvVar}');
            INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
            VALUES (1, 0, 'Dvor', 'camera', 1), (1, 2, 'Garaz', 'camera', 1);
            """;
        await cmd.ExecuteNonQueryAsync();
        return _connection;
    }

    private static NvrSynchronizer CreateSynchronizer(
        SqliteConnection connection, FakeDvripServer server, IClock clock)
        => new(
            new NvrRepository(connection),
            new CameraRepository(connection),
            new RecordingRepository(connection),
            clock,
            opts => server.CreateClient(opts.Username, opts.Password));

    [Test]
    public async Task SyncBacklog_AddsNewRecordings_Idempotent()
    {
        // Given fake NVR s dvoma nahrávkami (Ch0 segment + Ch2 event klip)
        var recordings = new[]
        {
            new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 15, 15, 0), 100),
            new FakeDvripServer.RecordingEntry("[Ch2]_2026-04-05_15.30.00-15.30.30.mkv",
                new DateTime(2026, 4, 5, 15, 30, 0), new DateTime(2026, 4, 5, 15, 30, 30), 8),
        };
        using var server = new FakeDvripServer(recordings: recordings);
        await server.StartAsync();

        var connection = await DbAsync();
        var sync = CreateSynchronizer(connection, server, new SystemClock());

        // When synchronizujeme backlog 14:00–16:00
        var result = await sync.SyncBacklogAsync(
            new DateTime(2026, 4, 5, 14, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));

        // Then obe nahrávky sú v DB (segment + event klip)
        await Assert.That(result.Inserted).IsEqualTo(2);
        var repo = new RecordingRepository(connection);
        var all = await repo.QueryAsync(null, null, null, null);
        await Assert.That(all).Count().IsEqualTo(2);

        var segment = all.First(r => r.NvrFilename.Contains("15.00.00"));
        await Assert.That(segment.CameraId).IsEqualTo(1);          // Ch0 → camera 1
        await Assert.That(segment.SourceType).IsEqualTo("segment");
        await Assert.That(segment.AnalysisState).IsEqualTo("queued");
        await Assert.That(segment.Availability).IsEqualTo("available");

        var clip = all.First(r => r.NvrFilename.Contains("15.30.30"));
        await Assert.That(clip.CameraId).IsEqualTo(2);             // Ch2 → camera 2
        await Assert.That(clip.SourceType).IsEqualTo("event_clip");
        await Assert.That(clip.DurationSec).IsEqualTo(30);

        // When spustíme znova (idempotencia)
        var second = await sync.SyncBacklogAsync(
            new DateTime(2026, 4, 5, 14, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));

        // Then žiadne duplicity
        await Assert.That(second.Inserted).IsEqualTo(0);
        var again = await repo.QueryAsync(null, null, null, null);
        await Assert.That(again).Count().IsEqualTo(2);
    }

    [Test]
    public async Task CheckAvailability_MarksMissingAsUnavailable()
    {
        // Given záznam v DB, ktorý fake NVR nemá
        var connection = await DbAsync();
        var repo = new RecordingRepository(connection);
        await repo.InsertAsync(new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment",
            NvrFilename = "[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            BeginTime = new DateTime(2026, 4, 5, 15, 0, 0),
            EndTime = new DateTime(2026, 4, 5, 15, 15, 0),
            DurationSec = 900, SizeBytes = 1024, Availability = "available",
        });

        // Given fake NVR bez nahrávok
        using var server = new FakeDvripServer();
        await server.StartAsync();
        var clock = new FakeClock(new DateTime(2026, 4, 6, 12, 0, 0));
        var sync = CreateSynchronizer(connection, server, clock);

        // When kontrola dostupnosti
        var result = await sync.CheckAvailabilityAsync(clock.UtcNow);

        // Then záznam je unavailable s časom a purge_at (+30 dní)
        await Assert.That(result.Unavailable).IsEqualTo(1);
        var stored = await repo.GetByNvrFilenameAsync("[Ch0]_2026-04-05_15.00.00-15.15.mkv");
        await Assert.That(stored!.Availability).IsEqualTo("unavailable");
        await Assert.That(stored.UnavailableSince).IsEqualTo(clock.UtcNow);
        await Assert.That(stored.PurgeAt).IsEqualTo(clock.UtcNow.AddDays(30));
    }

    [Test]
    public async Task CheckAvailability_KeepsAvailableRecording()
    {
        // Given záznam v DB aj na fake NVR
        var entry = new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
            new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 15, 15, 0), 100);
        using var server = new FakeDvripServer(recordings: [entry]);
        await server.StartAsync();

        var connection = await DbAsync();
        var repo = new RecordingRepository(connection);
        await repo.InsertAsync(new Recording
        {
            NvrId = 1, CameraId = 1, SourceType = "segment",
            NvrFilename = entry.FileName,
            BeginTime = entry.BeginTime, EndTime = entry.EndTime,
            DurationSec = 900, SizeBytes = 1024, Availability = "available",
        });

        var sync = CreateSynchronizer(connection, server, new SystemClock());

        // When kontrola
        var result = await sync.CheckAvailabilityAsync(DateTime.UtcNow);

        // Then záznam zostáva available
        await Assert.That(result.Unavailable).IsEqualTo(0);
        var stored = await repo.GetByNvrFilenameAsync(entry.FileName);
        await Assert.That(stored!.Availability).IsEqualTo("available");
    }

    [Test]
    public async Task DetectMissedDuringOutage_FindsGap()
    {
        // Given NVR má segmenty 15:00–15:15 a 15:30–15:45 (medzera 15:15–15:30)
        var recordings = new[]
        {
            new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.00.00-15.15.mkv",
                new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 15, 15, 0), 100),
            new FakeDvripServer.RecordingEntry("[Ch0]_2026-04-05_15.30.00-15.45.mkv",
                new DateTime(2026, 4, 5, 15, 30, 0), new DateTime(2026, 4, 5, 15, 45, 0), 100),
        };
        using var server = new FakeDvripServer(recordings: recordings);
        await server.StartAsync();

        var connection = await DbAsync();
        var sync = CreateSynchronizer(connection, server, new SystemClock());

        // When detekcia výpadkov 15:00–16:00
        var result = await sync.DetectMissedDuringOutageAsync(
            new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));

        // Then chýbajúci slot 15:15–15:30 je označený (a slot 15:45–16:00 NIE — NVR tam nemusel nahrávať)
        await Assert.That(result.MissedDuringOutage).IsEqualTo(1);
        var repo = new RecordingRepository(connection);
        var missed = await repo.QueryAsync(1, null, null, "segment");
        var missedOnly = missed.Where(r => r.Availability == "missed_during_outage").ToList();
        await Assert.That(missedOnly).Count().IsEqualTo(1);
        await Assert.That(missedOnly[0].BeginTime).IsEqualTo(new DateTime(2026, 4, 5, 15, 15, 0));
        await Assert.That(missedOnly[0].EndTime).IsEqualTo(new DateTime(2026, 4, 5, 15, 30, 0));
        await Assert.That(missedOnly[0].AnalysisState).IsEqualTo("skipped");
    }

    [Test]
    public async Task DetectMissedDuringOutage_NvrSilent_NoMissed()
    {
        // Given fake NVR bez záznamov (NVR nenahrával)
        using var server = new FakeDvripServer();
        await server.StartAsync();

        var connection = await DbAsync();
        var sync = CreateSynchronizer(connection, server, new SystemClock());

        // When detekcia
        var result = await sync.DetectMissedDuringOutageAsync(
            new DateTime(2026, 4, 5, 15, 0, 0), new DateTime(2026, 4, 5, 16, 0, 0));

        // Then žiadne missed (nie je to výpadok systému, ale NVR)
        await Assert.That(result.MissedDuringOutage).IsEqualTo(0);
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(PasswordEnvVar, null);
        if (_connection is not null) await _connection.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
