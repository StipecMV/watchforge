using System.Net;
using WatchForge.Testing.FakeNvr;

// WatchForge fake NVR host (S9-4): spustí FakeDvripServer ako samostatný
// kontajner/process pre E2E cez compose — emuluje Movols NVR (DVRIP)
// s jednou nahrávkou (MP4 payload) bez reálneho hardvéru.
//
// Konfigurácia cez env vars:
//   WF_FAKE_NVR_PORT      — port (default 34567, DVRIP DefaultPort)
//   WF_FAKE_NVR_USERNAME  — login (default "admin")
//   WF_FAKE_NVR_PASSWORD  — heslo (default "secret")
//   WF_FAKE_NVR_FILENAME  — názov nahrávky (default "[Ch0]_2026-04-05_15.00.00-15.15.mkv")
//   WF_FAKE_NVR_BEGIN     — začiatok nahrávky ISO 8601 (default 2026-04-05T15:00:00Z)
//   WF_FAKE_NVR_END       — koniec nahrávky ISO 8601 (default 2026-04-05T15:15:00Z)
//   WF_FAKE_NVR_VIDEO     — cesta k MP4 súboru s videom (payload; ak chýba, fill byte)

var port = int.TryParse(Environment.GetEnvironmentVariable("WF_FAKE_NVR_PORT"), out var p) ? p : 34567;
var username = Environment.GetEnvironmentVariable("WF_FAKE_NVR_USERNAME") ?? "admin";
var password = Environment.GetEnvironmentVariable("WF_FAKE_NVR_PASSWORD") ?? "secret";
var fileName = Environment.GetEnvironmentVariable("WF_FAKE_NVR_FILENAME") ?? "[Ch0]_2026-04-05_15.00.00-15.15.mkv";
var videoPath = Environment.GetEnvironmentVariable("WF_FAKE_NVR_VIDEO");

var begin = DateTime.TryParse(Environment.GetEnvironmentVariable("WF_FAKE_NVR_BEGIN"), out var b)
    ? b.ToUniversalTime() : new DateTime(2026, 4, 5, 15, 0, 0, DateTimeKind.Utc);
var end = DateTime.TryParse(Environment.GetEnvironmentVariable("WF_FAKE_NVR_END"), out var e)
    ? e.ToUniversalTime() : new DateTime(2026, 4, 5, 15, 15, 0, DateTimeKind.Utc);

byte[]? payload = null;
if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
    payload = await File.ReadAllBytesAsync(videoPath);

var server = new FakeDvripServer(
    validUsername: username,
    validPassword: password,
    recordings:
    [
        new FakeDvripServer.RecordingEntry(fileName, begin, end, LengthBlocks: 0, Payload: payload)
    ],
    listenAddress: IPAddress.Any,
    port: port);

await server.StartAsync();
Console.WriteLine($"Fake NVR listening on 0.0.0.0:{server.Port} (requested {port}) — {fileName} [{begin:O} → {end:O}] payload={payload?.Length ?? 0} B");
Console.WriteLine("Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopping fake NVR.");
}
finally
{
    server.Dispose();
}
