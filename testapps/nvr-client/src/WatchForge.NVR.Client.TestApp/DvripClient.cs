namespace WatchForge.NVR.Client.TestApp;

/// <summary>
/// Minimal DVRIP client for Xiongmai/Sofia-based NVR devices (e.g. Movols brand).
/// Handles login, file listing, and best-effort file download over raw TCP.
/// </summary>
public sealed class DvripClient : IDisposable
{
    // ── Message IDs ───────────────────────────────────────────────────────────
    private const ushort MsgLoginRequest     = 1000;
    private const ushort MsgFileQueryRequest = 1442;

    // TODO: The OPPlayBack message ID for Sofia/Xiongmai firmware is not definitively
    // documented in public specifications. The candidates are:
    //   1466 (0x5BA) — most commonly cited in open-source DVRIP implementations
    //   1426 (0x592) — used in some firmware variants for monitor/playback
    //   1546 (0x60A) — cited in Xiongmai SDK documentation for some device types
    // This implementation uses 1466 as the primary candidate. If download fails,
    // try the other values for your specific firmware version.
    private const ushort MsgPlayBackRequest  = 1466;

    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private uint _sessionId;
    private uint _seqNum;

    public DvripClient(string host, int port, string username, string password)
    {
        _host     = host;
        _port     = port;
        _username = username;
        _password = password;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a TCP connection to the NVR and authenticates with the Sofia MD5 password hash.
    /// </summary>
    public async Task<LoginResult> LoginAsync(CancellationToken ct = default)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
        _seqNum = 0;
        _sessionId = 0;

        // This NVR (Movols/Xiongmai HVR, Sofia firmware) rejects the Sofia MD5 hash
        // (EncryptType "MD5") with Ret 203. Plain text credentials with EncryptType "None"
        // return Ret 100. Confirmed via raw TCP test in Python.
        var loginJson = JsonSerializer.Serialize(new
        {
            EncryptType = "None",
            LoginType   = "DVRIP",
            PassWord    = _password,
            UserName    = _username
        });

        var response = await SendAndReceiveAsync(MsgLoginRequest, loginJson, ct);
        var json = Encoding.UTF8.GetString(response.Payload).TrimEnd('\0');

        using var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ret = GetNvrProp(root, "Ret").GetInt32();
        if (ret != 100)
            throw new InvalidOperationException($"Login failed — NVR returned code {ret}");

        var sessionHex = GetNvrProp(root, "SessionID").GetString() ?? "0x0";
        _sessionId = Convert.ToUInt32(sessionHex, 16);

        return new LoginResult
        {
            DeviceType    = GetNvrProp(root, "DeviceType").GetString() ?? "Unknown",
            ChannelNum    = GetNvrProp(root, "ChannelNum").GetInt32(),
            SessionId     = _sessionId,
            AliveInterval = GetNvrProp(root, "AliveInterval").GetInt32()
        };
    }

    /// <summary>
    /// Queries recorded files in the given time range and channel.
    /// Channel is zero-indexed (0 = first channel).
    /// Returns an empty list if the NVR has no files for the period or returns a non-100 code.
    /// </summary>
    public async Task<List<NvrFile>> QueryFilesAsync(
        DateTime from, DateTime to, int channel = 0, CancellationToken ct = default)
    {
        var queryJson = JsonSerializer.Serialize(new
        {
            Name        = "OPFileQuery",
            OPFileQuery = new
            {
                BeginTime = from.ToString("yyyy-MM-dd HH:mm:ss"),
                EndTime   = to.ToString("yyyy-MM-dd HH:mm:ss"),
                Channel   = channel,
                Types     = new[] { "h264" }
            },
            SessionID = $"0x{_sessionId:X8}",
            Magic     = "0x1234"
        });

        var response = await SendAndReceiveAsync(MsgFileQueryRequest, queryJson, ct);
        var json = Encoding.UTF8.GetString(response.Payload).TrimEnd('\0');
        return ParseFileQueryResponse(json);
    }

    /// <summary>
    /// Attempts to download a recorded file to <paramref name="destinationPath"/>.
    ///
    /// NOTE: The DVRIP file download protocol for Sofia/Xiongmai firmware is not
    /// definitively documented. This implementation sends OPPlayBack Start
    /// (message ID 1466) and reads the resulting data stream as DVRIP packets,
    /// writing their payloads to disk.
    ///
    /// Known unknowns:
    ///   • The correct OPPlayBack message ID may be 1426, 1466, or 1546 depending on firmware.
    ///   • After the Start handshake the NVR may wrap AV data in DVRIP packets (message ID ~508)
    ///     or emit a proprietary frame format with its own inner header.
    ///   • If the downloaded file does not play back, the inner frame header may need to be
    ///     stripped. Inspect with a hex editor: valid H.264 streams begin with 0x00 0x00 0x00 0x01.
    /// </summary>
    public async Task DownloadFileAsync(
        NvrFile file, string destinationPath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var startJson = JsonSerializer.Serialize(new
        {
            Name        = "OPPlayBack",
            OPPlayBack  = new
            {
                Action    = "Start",
                Parameter = new
                {
                    PlayMode  = "ByName",
                    FileName  = file.FileName,
                    TransMode = "TCP",
                    Value     = 0
                }
            },
            SessionID = $"0x{_sessionId:X8}"
        });

        var response = await SendAndReceiveAsync(MsgPlayBackRequest, startJson, ct);
        var json = Encoding.UTF8.GetString(response.Payload).TrimEnd('\0');

        using var doc = JsonDocument.Parse(json);
        var ret = GetNvrProp(doc.RootElement, "Ret").GetInt32();
        if (ret != 100)
            throw new InvalidOperationException(
                $"OPPlayBack Start failed — NVR returned code {ret}. " +
                $"Tried message ID {MsgPlayBackRequest}. See TODO in DvripClient.cs.");

        await using var fileStream = File.OpenWrite(destinationPath);
        long bytesWritten = 0;

        while (bytesWritten < file.FileLengthBytes && !ct.IsCancellationRequested)
        {
            DvripPacket pkt;
            try { pkt = await ReceivePacketAsync(ct); }
            catch (EndOfStreamException) { break; }

            if (pkt.Payload.Length > 0)
            {
                await fileStream.WriteAsync(pkt.Payload, ct);
                bytesWritten += pkt.Payload.Length;
                progress?.Report(bytesWritten);
            }
        }

        // Best-effort Stop — ignore errors since the file data is already received
        try
        {
            var stopJson = JsonSerializer.Serialize(new
            {
                Name       = "OPPlayBack",
                OPPlayBack = new { Action = "Stop" },
                SessionID  = $"0x{_sessionId:X8}"
            });
            await SendAndReceiveAsync(MsgPlayBackRequest, stopJson, ct);
        }
        catch { /* intentionally swallowed */ }
    }

    // ── Public static helpers (also used by unit tests) ───────────────────────

    /// <summary>
    /// Computes the Sofia firmware password hash used in DVRIP login.
    /// Algorithm: MD5(password) → take bytes at even positions (0,2,4,...,14)
    /// → each byte: (byte % 61 + 64) as ASCII char → 8-char string.
    /// </summary>
    public static string ComputeSofiaHash(string password)
    {
        var md5   = MD5.HashData(Encoding.UTF8.GetBytes(password));
        var chars = new char[8];
        for (int i = 0; i < 8; i++)
            chars[i] = (char)(md5[i * 2] % 61 + 64);
        return new string(chars);
    }

    /// <summary>
    /// Parses the JSON body of an OPFileQuery response into a list of <see cref="NvrFile"/>.
    /// Handles the malformed datetime format returned by some Sofia firmware versions.
    /// Returns an empty list for non-100 return codes or missing/empty file arrays.
    /// </summary>
    public static List<NvrFile> ParseFileQueryResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (GetNvrProp(root, "Ret").GetInt32() != 100)
            return [];

        if (!root.TryGetProperty("OPFileQuery", out var fileArray)
            || fileArray.ValueKind != JsonValueKind.Array)
            return [];

        var files = new List<NvrFile>();
        foreach (var item in fileArray.EnumerateArray())
        {
            files.Add(new NvrFile
            {
                FileName        = GetNvrProp(item, "FileName").GetString() ?? "",
                BeginTime       = NvrFile.ParseNvrDateTime(GetNvrProp(item, "BeginTime").GetString()),
                EndTime         = NvrFile.ParseNvrDateTime(GetNvrProp(item, "EndTime").GetString()),
                FileLengthBytes = NvrFile.ParseFileLength(GetNvrProp(item, "FileLength").GetString()),
                DiskNo          = GetNvrProp(item, "DiskNo").GetInt32(),
                SerialNo        = GetNvrProp(item, "SerialNo").GetInt32()
            });
        }
        return files;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads a property from a NVR JSON element defensively.
    /// Sofia firmware sometimes emits property names with trailing/leading whitespace
    /// (e.g. "DeviceType " instead of "DeviceType"). Tries the exact name first,
    /// then falls back to a case-sensitive trimmed scan of all properties.
    /// Throws <see cref="KeyNotFoundException"/> if the property is not found at all.
    /// </summary>
    private static JsonElement GetNvrProp(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var exact))
            return exact;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Trim() == name)
                return prop.Value;
        }

        throw new KeyNotFoundException($"NVR response missing property '{name}'");
    }

    private async Task<DvripPacket> SendAndReceiveAsync(ushort msgId, string json, CancellationToken ct)
    {
        // JSON payload is null-terminated per the DVRIP spec; length field includes the null byte.
        var payload = Encoding.UTF8.GetBytes(json + "\0");
        var packet  = DvripPacket.Build(_sessionId, _seqNum++, msgId, payload);
        await _stream!.WriteAsync(packet, ct);
        return await ReceivePacketAsync(ct);
    }

    private async Task<DvripPacket> ReceivePacketAsync(CancellationToken ct)
    {
        var header = new byte[DvripPacket.HeaderSize];
        await ReadExactAsync(_stream!, header, ct);
        return await DvripPacket.ReadFromStreamAsync(_stream!, header, ct);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException("Connection closed while reading DVRIP header");
            offset += read;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
    }
}
