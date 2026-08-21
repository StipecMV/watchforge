using WatchForge.Processing.Library;

// WatchForge UI E2E seed (S11): vytvorí čerstvú DB s default users
// (admin/user1, heslo heslo1234) + NVR + kamery pre Playwright testy.
// Použitie: dotnet run --project tools/e2e-seed -- <dbPath>

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: e2e-seed <dbPath>");
    return 1;
}

var dbPath = args[0];
foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
    if (File.Exists(f)) File.Delete(f);

await using var connection = await WatchForgeDatabase.OpenAsync(dbPath);

var users = new UserRepository(connection);
await users.SeedDefaultUsersAsync();
foreach (var name in new[] { "admin", "user1" })
{
    var user = await users.GetByUsernameAsync(name);
    await users.UpdatePasswordHashAsync(user!.UserId, UserRepository.HashPassword("heslo1234"));
}

await using var cmd = connection.CreateCommand();
cmd.CommandText = """
    INSERT INTO NVRS (site_id, host, port, username, password_secret_env)
    VALUES ('site-a', '192.168.68.10', 34567, 'nvr-user', 'WF_TEST_NVR_PASSWORD');
    INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
    VALUES (1, 0, 'Dvor', 'camera', 1);
    INSERT INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active)
    VALUES (1, 1, 'Brana', 'camera', 1);
    """;
await cmd.ExecuteNonQueryAsync();

// S11-3: seed dnešnej detekcie (dashboard event-first → flag screen)
var now = DateTime.UtcNow;
var begin = now.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss");
var end = now.ToString("yyyy-MM-dd HH:mm:ss");
var nvrFile = $"[Ch0]_{now:yyyy-MM-dd}_{now:HH.mm.ss}-{now:HH.mm}.mkv";
await using var det = connection.CreateCommand();
det.CommandText = """
    INSERT INTO RECORDINGS (nvr_id, camera_id, source_type, nvr_filename, begin_time, end_time, duration_sec, size_bytes, codec, width, height)
    VALUES (1, 1, 'segment', $file, $begin, $end, 1800, 100, 'hevc', 3840, 2160);
    INSERT INTO DETECTIONS (recording_id, camera_id, detection_type, timestamp_ms, duration_ms, confidence, algorithm_version, region_x, region_y, region_w, region_h, object_class)
    VALUES (last_insert_rowid(), 1, 'motion', 1000, 500, 0.9, 'optical-flow-1', 0.1, 0.2, 0.3, 0.4, '');
    """;
det.Parameters.AddWithValue("$file", nvrFile);
det.Parameters.AddWithValue("$begin", begin);
det.Parameters.AddWithValue("$end", end);
await det.ExecuteNonQueryAsync();

Console.WriteLine($"E2E DB seeded: {dbPath}");
return 0;
