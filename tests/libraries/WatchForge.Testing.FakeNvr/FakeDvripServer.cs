using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WatchForge.DVRIP.Library;

namespace WatchForge.Testing.FakeNvr;

/// <summary>
/// S3-3: fake NVR test server emulujúci DVRIP handshake
/// (login 1000/1001, file query 1440/1441, playback claim 1424/1425,
/// download start 1420 + data 1426), aby sa DvripClient dal testovať
/// bez reálneho NVR hardvéru. Reusable testovacia knižnica pre
/// unit/integračné/E2E testy (S3-4, S4-8, S9-4).
/// </summary>
public sealed class FakeDvripServer : IDisposable
{
    public sealed record RecordingEntry(
        string FileName,
        DateTime BeginTime,
        DateTime EndTime,
        int LengthBlocks,
        byte[]? Payload = null);

    private readonly TcpListener _listener;
    private readonly string _validUsername;
    private readonly string _validPassword;
    private readonly IReadOnlyList<RecordingEntry> _recordings;
    private readonly byte _fillByte;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private uint _nextSessionId = 0x1B;
    private int _requestCount;

    public FakeDvripServer(
        string validUsername = "admin",
        string validPassword = "secret",
        IReadOnlyList<RecordingEntry>? recordings = null,
        byte fillByte = 0xAA,
        IPAddress? listenAddress = null,
        int? port = null)
    {
        _validUsername = validUsername;
        _validPassword = validPassword;
        _recordings = recordings ?? [];
        _fillByte = fillByte;
        // Loopback + náhodný port default (testy); Any + fixný port pre fake NVR kontajner (S9-4 E2E)
        _listener = new TcpListener(listenAddress ?? IPAddress.Loopback, port ?? 0);
    }

    public int Port { get; private set; }

    /// <summary>Počet prijatých DVRIP requestov (pre asercie v testoch).</summary>
    public int RequestCount => Interlocked.CompareExchange(ref _requestCount, 0, 0);

    /// <summary>
    /// Vytvorí nakonfigurovaného DvripClienta (options cesta) proti tomuto serveru.
    /// </summary>
    public DvripClient CreateClient(string username, string password, int? readTimeoutSeconds = null) => new(
        new DvripClientOptions
        {
            Host               = "127.0.0.1",
            Port               = Port,
            Username           = username,
            Password           = password,
            ReadTimeoutSeconds = readTimeoutSeconds ?? DvripClientOptions.DefaultReadTimeoutSeconds
        });

    public async Task StartAsync()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => HandleClientAsync(tcp));
        }
    }

    private async Task HandleClientAsync(TcpClient tcp)
    {
        using (tcp)
        using (var stream = tcp.GetStream())
        {
            while (!_cts.IsCancellationRequested)
            {
                var header = new byte[DvripPacket.HeaderSize];
                if (!await ReadExactAsync(stream, header, _cts.Token))
                    break;

                DvripPacket pkt;
                try
                {
                    pkt = await DvripPacket.ReadFromStreamAsync(stream, header, _cts.Token);
                }
                catch (EndOfStreamException) { break; }
                catch (InvalidDataException) { break; }

                Interlocked.Increment(ref _requestCount);

                switch (pkt.MessageId)
                {
                    case 1000: // login request → 1001 response
                        var loginJson = HandleLogin(pkt);
                        await WritePacketAsync(stream, pkt.SessionId, 1001, loginJson);
                        break;

                    case 1440: // file query → 1441 response
                        var queryJson = HandleFileQuery(pkt);
                        await WritePacketAsync(stream, pkt.SessionId, 1441, queryJson);
                        break;

                    case 1424: // playback claim → 1425 response
                        var claimJson = HandlePlaybackClaim();
                        await WritePacketAsync(stream, pkt.SessionId, 1425, claimJson);
                        break;

                    case 1420: // download start → stream 1426 data packets
                        await HandleDownloadStartAsync(stream, pkt.SessionId, pkt.Payload);
                        break;

                    default:
                        // Unknown message — ignore
                        break;
                }
            }
        }
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private string HandleLogin(DvripPacket request)
    {
        string? username = null, password = null;
        try
        {
            using var doc = JsonDocument.Parse(TrimNull(request.Payload));
            var root = doc.RootElement;
            if (root.TryGetProperty("UserName", out var u)) username = u.GetString();
            if (root.TryGetProperty("PassWord", out var p)) password = p.GetString();
        }
        catch (JsonException) { /* malformed request → reject */ }

        if (username != _validUsername || password != _validPassword)
            return """{"Name":"OPMonitor","Ret":203,"SessionID":"0x00000000"}""";

        var sessionId = _nextSessionId++;
        return $$"""
            {"Name":"OPMonitor","Ret":100,"SessionID":"0x{{sessionId:X8}}","DeviceType":"HVR","ChannelNum":6,"AliveInterval":10}
            """;
    }

    private string HandleFileQuery(DvripPacket request)
    {
        // Reálny NVR filtruje podľa kanála z OPFileQuery.Channel
        var channel = ExtractRequestedChannel(request.Payload);
        var matching = channel < 0
            ? _recordings
            : _recordings.Where(r => ExtractChannelFromName(r.FileName) == channel).ToList();

        var sb = new StringBuilder("""{"Name":"OPFileQuery","Ret":100,"OPFileQuery":[""");
        for (int i = 0; i < matching.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var r = matching[i];
            // Ak má záznam reálny payload, FileLength (v blokoch po 1024) musí pokryť jeho veľkosť
            var lengthBlocks = r.Payload is not null
                ? (r.Payload.Length + 1023) / 1024
                : r.LengthBlocks;
            sb.Append(
                $$"""
                {"FileName":"{{r.FileName}}","BeginTime":"{{r.BeginTime:yyyy-MM-dd HH:mm:ss}}","EndTime":"{{r.EndTime:yyyy-MM-dd HH:mm:ss}}","FileLength":"{{lengthBlocks}}","DiskNo":0,"SerialNo":{{i + 1}}}
                """);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private string HandlePlaybackClaim() =>
        """{"Name":"OPPlayBack","Ret":100,"SessionID":"0x00000000"}""";

    private async Task HandleDownloadStartAsync(NetworkStream stream, uint sessionId, byte[] payload)
    {
        // Find which recording the client asked for (FileName in OPPlayBack.Parameter)
        var fileName = ExtractFileName(payload);
        var recording = _recordings.FirstOrDefault(r => r.FileName == fileName);
        if (recording is null)
            return;

        // Reálny payload (napr. validné video) alebo fill byte pattern
        var data = recording.Payload ?? CreateFillPayload(recording.LengthBlocks);
        var chunk = new byte[8192];

        long sent = 0;
        while (sent < data.Length && !_cts.IsCancellationRequested)
        {
            int n = (int)Math.Min(chunk.Length, data.Length - sent);
            var packet = DvripPacket.Build(sessionId, 0, 1426, data[(int)sent..(int)(sent + n)]);
            await stream.WriteAsync(packet, _cts.Token);
            sent += n;
        }

        // Ukončenie downloadu: prázdny 1426 paket (klient ho berie ako koniec — Ret 103 je normálne)
        if (!_cts.IsCancellationRequested)
        {
            var endPacket = DvripPacket.Build(sessionId, 0, 1426, Array.Empty<byte>());
            await stream.WriteAsync(endPacket, _cts.Token);
        }
    }

    private byte[] CreateFillPayload(int lengthBlocks)
    {
        long totalBytes = lengthBlocks * 1024L;
        var data = new byte[totalBytes];
        Array.Fill(data, _fillByte);
        return data;
    }

    private static string? ExtractFileName(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(TrimNull(payload));
            var root = doc.RootElement;
            if (root.TryGetProperty("OPPlayBack", out var pb)
                && pb.TryGetProperty("Parameter", out var parameter)
                && parameter.TryGetProperty("FileName", out var fname))
                return fname.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Vytiahne OPFileQuery.Channel z requestu (-1 ak chýba = všetky).</summary>
    private static int ExtractRequestedChannel(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(TrimNull(payload));
            var root = doc.RootElement;
            if (root.TryGetProperty("OPFileQuery", out var query)
                && query.TryGetProperty("Channel", out var channel))
                return channel.GetInt32();
        }
        catch (JsonException) { }
        return -1;
    }

    /// <summary>Parsuje číslo kanála z názvu nahrávky ("[Ch3]_..." → 3).</summary>
    private static int ExtractChannelFromName(string fileName)
    {
        var start = fileName.IndexOf("[Ch", StringComparison.Ordinal);
        if (start < 0) return -1;
        var end = fileName.IndexOf(']', start);
        if (end < 0) return -1;
        return int.TryParse(fileName.AsSpan(start + 3, end - start - 3), out var ch) ? ch : -1;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task WritePacketAsync(NetworkStream stream, uint sessionId, ushort msgId, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json + "\0");
        var packet = DvripPacket.Build(sessionId, 0, msgId, payload);
        await stream.WriteAsync(packet, _cts.Token);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            }
            catch (IOException) { return false; }
            catch (OperationCanceledException) { return false; }

            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private static string TrimNull(byte[] payload)
    {
        var end = Array.IndexOf(payload, (byte)0);
        var len = end >= 0 ? end : payload.Length;
        return Encoding.UTF8.GetString(payload, 0, len);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
