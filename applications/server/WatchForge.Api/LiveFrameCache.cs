using System.Collections.Concurrent;
using System.Diagnostics;
using WatchForge.DVRIP.Library;

namespace WatchForge.Api;

/// <summary>
/// S19: Live frame cache — každá kamera × rozlíšenie × fps má JEDEN persistentný
/// OPMonitor stream (OPMonitor → ffmpeg → MJPEG), ktorý beží na pozadí a ukladá
/// posledný JPEG frame. GET /api/v1/live/{id}/frame potom vráti posledný frame
/// OKAMŽITE (&lt;50 ms), namiesto 13 s (ffmpeg by musel čakať na keyframe).
/// S22o: adaptívny stream — view 1 (1080p@10fps) a view 8 (360p@1fps) sú RÔZNE
/// streamy (iný kľúč), takže každý zobrazený režim beží v svojom rozlíšení/fps a
/// pri prepnutí sa nepotrebný uvoľní (šetrí NVR upload).
/// Stream sa po ~20 s nečinnosti (nikto nepozerá) sám ukončí.
/// </summary>
public sealed class LiveFrameCache : IDisposable
{
    private readonly ConcurrentDictionary<(int CameraId, int Width, int Fps), LiveSession> _sessions = new();
    private readonly Timer _cleanupTimer;

    public LiveFrameCache()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>Vráti posledný JPEG frame kamery v danom rozlíšení/fps (spustí stream, ak ešte nebeží).</summary>
    public async Task<byte[]?> GetFrameAsync(int cameraId, DvripClientOptions opts, int channel,
        int width, int fps, CancellationToken ct)
    {
        var key = (cameraId, width, fps);
        var session = _sessions.GetOrAdd(key, _ => new LiveSession());
        session.Touch();
        // DÔLEŽITÉ: EnsureStartedAsync vracia Task streamu (beží nepretržite) — NEawaitovať!
        // await na ňom by čakal, kým stream neskončí (nikdy) a request by visel.
        _ = session.EnsureStartedAsync(opts, channel, width, fps, ct);
        var frame = await session.WaitFirstFrameAsync(ct);
        if (frame is null)
        {
            // Ladiace info: ako dlho stream beží, koľko framov už vyprodukoval, aký je stav.
            Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] GetFrameAsync: frame je NULL (stream beží {session.AgeSeconds}s, framov: {session.FrameCount})");
        }
        return frame;
    }

    /// <summary>Okamžite zastaví stream kamery v danom rozlíšení/fps (UI ju už nezobrazuje — uvoľnenie NVR/ffmpeg zdrojov).</summary>
    public void Release(int cameraId, int width, int fps)
    {
        if (_sessions.TryRemove((cameraId, width, fps), out var session))
        {
            Console.Error.WriteLine($"[live-cache ch{cameraId}@{width}x{fps}fps] release: stream ukončený (UI ho už nezobrazuje)");
            session.Cancel();
        }
    }

    /// <summary>S22o: uvoľní VŠETKY režimy kamery (pri prepnutí view 1↔8 sa ukončia nepotrebné streamy).</summary>
    public void ReleaseAll(int cameraId)
    {
        foreach (var (key, session) in _sessions)
        {
            if (key.CameraId == cameraId)
            {
                Console.Error.WriteLine($"[live-cache ch{cameraId}@{key.Width}x{key.Fps}fps] release-all: stream ukončený");
                session.Cancel();
                _sessions.TryRemove(key, out _);
            }
        }
    }

    private void Cleanup()
    {
        foreach (var (key, session) in _sessions)
        {
            if (session.IsIdle)
            {
                Console.Error.WriteLine($"[live-cache ch{key.CameraId}@{key.Width}x{key.Fps}fps] cleanup: stream neaktívny — ukončujem");
                session.Cancel();
                _sessions.TryRemove(key, out _);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var (_, session) in _sessions) session.Cancel();
    }

    /// <summary>Jeden persistentný stream pre jednu (kamera × rozlíšenie × fps).</summary>
    private sealed class LiveSession
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<byte[]?> _firstFrame =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? _latestFrame;
        private Task? _streamTask;
        private CancellationTokenSource? _cts;
        private DateTime _lastAccess = DateTime.UtcNow;
        private int _packetCount;
        private int _frameCount;
        private readonly DateTime _createdAt = DateTime.UtcNow;

        public void Touch() => _lastAccess = DateTime.UtcNow;

        public bool IsIdle => (DateTime.UtcNow - _lastAccess) > TimeSpan.FromSeconds(20);

        /// <summary>Ako dlho stream beží (pre ladenie frame=null).</summary>
        public int AgeSeconds => (int)(DateTime.UtcNow - _createdAt).TotalSeconds;

        /// <summary>Koľko JPEG framov už stream vyprodukoval (pre ladenie frame=null).</summary>
        public int FrameCount => _frameCount;

        public void Cancel() => _cts?.Cancel();

        /// <summary>Spustí stream (raz) — OPMonitor + ffmpeg MJPEG, číta framy do cache.</summary>
        public Task EnsureStartedAsync(DvripClientOptions opts, int channel, int width, int fps, CancellationToken ct)
        {
            lock (_gate)
            {
                if (_streamTask is not null) return _streamTask;
                // VLASTNÝ token (nie linked s request ct!): stream musí prežiť odpojenie
                // klienta — inak by každý timeout request zabil stream a ďalšie requesty
                // by dostali 504. Request ct sa používa len na čakanie (WaitFirstFrameAsync).
                _cts = new CancellationTokenSource();
                _streamTask = RunAsync(opts, channel, width, fps, _cts.Token);
                return _streamTask;
            }
        }

        /// <summary>Počká na prvý frame, potom vráti posledný (cache) — rýchle.</summary>
        public async Task<byte[]?> WaitFirstFrameAsync(CancellationToken ct)
        {
            var first = await _firstFrame.Task.WaitAsync(ct);
            if (first is not null) return _latestFrame;
            return null;
        }

        private async Task RunAsync(DvripClientOptions opts, int channel, int width, int fps, CancellationToken ct)
        {
            Process? ffmpeg = null;
            try
            {
                Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] stream start...");
                ffmpeg = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        // S22o: adaptívne rozlíšenie/fps podľa režimu (view 1: 1080p@10, view 8: 360p@1).
                        // image2pipe + nobuffer neprodukovali framy z HEVC pipe vstupu.
                        Arguments = $"-v error -f hevc -i pipe:0 -f mpjpeg -q:v 5 -s {width}x{HeightFor(width)} -r {fps} pipe:1",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    }
                };
                ffmpeg.Start();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // PRIEBEŽNÉ čítanie stderr (ReadToEndAsync by čakal na EOF = ffmpeg exit)
                        var line = await ffmpeg.StandardError.ReadLineAsync();
                        while (line is not null)
                        {
                            Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] ffmpeg: {line}");
                            line = await ffmpeg.StandardError.ReadLineAsync();
                        }
                    }
                    catch { /* process exit */ }
                }, CancellationToken.None);

                using var dvrip = new DvripClient(new DvripClientOptions
                {
                    Host = opts.Host, Port = opts.Port,
                    Username = opts.Username, Password = opts.Password,
                    ReadTimeoutSeconds = 10,
                });
                await dvrip.LoginAsync(ct);
                Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] login OK, monitor start...");

                var dvripTask = dvrip.MonitorStreamAsync(
                    channel, "Extra",
                    async (data, _) =>
                    {
                        var n = Interlocked.Increment(ref _packetCount);
                        if (n == 1 || n % 50 == 0)
                            Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] packet #{n} ({data.Length} B)");
                        // DÔLEŽITÉ: FlushAsync po každom zápise — inak dáta ostanú v pipe
                        // buffri a ffmpeg nedostane nič (testované: 0 B z ffmpeg bez flush).
                        await ffmpeg.StandardInput.BaseStream.WriteAsync(data, ct);
                        await ffmpeg.StandardInput.BaseStream.FlushAsync(ct);
                    },
                    ct);
                Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] monitor started, reading frames...");

                // Čítať MJPEG framy z ffmpeg stdout → cache (FFD8 … FFD9)
                await ReadFramesAsync(ffmpeg.StandardOutput.BaseStream, ct);

                try { await dvripTask; } catch { /* stream skončil */ }
            }
            catch (OperationCanceledException) { /* cleanup/shutdown */ }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] error: {ex}");
                _firstFrame.TrySetResult(null);
            }
            finally
            {
                Console.Error.WriteLine($"[live-cache ch{channel}@{width}x{fps}fps] stream končí (framov: {_frameCount})");
                _firstFrame.TrySetResult(_latestFrame); // ak stream skončil bez framu, odblokovať čakajúcich
                // S22e: tvrdo ukončiť ffmpeg — Cleanup/Release zruší len CTS, ale samotný
                // ffmpeg proces by inak ostal bežať (žerie CPU + NVR upload). Kill aj deti.
                try { ffmpeg?.Kill(entireProcessTree: true); } catch { /* už skončil */ }
                ffmpeg?.Dispose();
            }
        }

        private async Task ReadFramesAsync(Stream stream, CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            var frame = new MemoryStream();
            bool inFrame = false;
            long totalBytes = 0;

            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buf, ct);
                if (read == 0)
                {
                    Console.Error.WriteLine($"[live-cache] ffmpeg stdout EOF po {totalBytes} B");
                    break;
                }
                totalBytes += read;

                for (int i = 0; i < read; i++)
                {
                    var b = buf[i];
                    if (!inFrame)
                    {
                        // Hľadáme SOI (FFD8)
                        if (b == 0xFF && i + 1 < read && buf[i + 1] == 0xD8)
                        {
                            inFrame = true;
                            frame.SetLength(0);
                            frame.WriteByte(b);
                            frame.WriteByte(buf[i + 1]);
                            i++;
                            }
                        else if (b == 0xFF && i + 1 == read)
                        {
                            // FFD8 prekročil hranicu buffera — načítať ešte 1 bajt
                            var next = new byte[1];
                            if (await stream.ReadAsync(next, ct) == 1 && next[0] == 0xD8)
                            {
                                inFrame = true;
                                frame.SetLength(0);
                                frame.WriteByte(b);
                                frame.WriteByte(next[0]);
                            }
                        }
                        continue;
                    }

                    // V streame — kopírujeme až po EOI (FFD9)
                    frame.WriteByte(b);
                    if (b == 0xD9 && i > 0 && buf[i - 1] == 0xFF)
                    {
                        _latestFrame = frame.ToArray();
                        _frameCount++;
                        _firstFrame.TrySetResult(_latestFrame);
                        inFrame = false;
                        frame.SetLength(0);
                        if (_latestFrame!.Length > 8 * 1024 * 1024) _latestFrame = null; // poistka
                    }
                }
            }
        }

        /// <summary>Výška pre zachovanie 16:9 pomeru pri danom width.</summary>
        private static int HeightFor(int width) => (int)Math.Round(width * 9.0 / 16.0 / 2) * 2;
    }
}
