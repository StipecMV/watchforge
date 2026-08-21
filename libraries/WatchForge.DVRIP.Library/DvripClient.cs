namespace WatchForge.DVRIP.Library;

/// <summary>
/// Minimal DVRIP client for Xiongmai/Sofia-based NVR devices (e.g. Movols brand).
/// Handles login, file listing, and best-effort file download over raw TCP.
/// </summary>
public sealed class DvripClient : IDvripClient
{
    // ── Message IDs ───────────────────────────────────────────────────────────
    private const ushort MsgLoginRequest     = 1000;
    private const ushort MsgLogoutRequest    = 1001;  // OPLogout (uvoľnenie session — inak NVR drží sessiony, kým nevyprší timeout)
    private const ushort MsgFileQueryRequest = 1440;  // FILESEARCH_REQ (confirmed)
    private const ushort MsgPlayBackRequest  = 1420;  // PLAY_REQ / DownloadStart
    private const ushort MsgPlayClaimRequest = 1424;  // PLAY_CLAIM (Perl: PLAY_CLAIM=1424 for Claim step)
    private const ushort MsgDownloadData     = 1426;  // DOWNLOAD_DATA (confirmed)
    private const ushort MsgMonitorClaim     = 1413;  // OPMonitor Claim (S19 live view)
    private const ushort MsgMonitor          = 1410;  // OPMonitor Start/Stop
    private const ushort MsgMonitorData      = 1412;  // OPMonitor video dáta (HEVC)

    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly int _readTimeoutMs;
    private readonly int _readTimeoutSeconds;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private uint _sessionId;
    private uint _seqNum;

    /// <summary>
    /// Konfigurácia cez <see cref="DvripClientOptions"/> (S3-2) — odporúčaná cesta
    /// (binding z appsettings / env vars, validácia pri vytvorení).
    /// </summary>
    public DvripClient(DvripClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _host     = options.Host;
        _port     = options.Port;
        _username = options.Username;
        _password = options.Password;
        _readTimeoutMs = options.ReadTimeoutSeconds * 1000;
        _readTimeoutSeconds = options.ReadTimeoutSeconds;
    }

    /// <summary>
    /// Pozičný konštruktor — kompatibilný shim pre existujúci kód (service, testy).
    /// Pre nový kód používaj <see cref="DvripClient(DvripClientOptions)"/>.
    /// </summary>
    public DvripClient(string host, int port, string username, string password)
        : this(new DvripClientOptions
        {
            Host     = host,
            Port     = port,
            Username = username,
            Password = password
        })
    {
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
        _stream.ReadTimeout = _readTimeoutMs;
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
        // Type "*" returns all recordings regardless of codec (NVR records H.265, not H.264).
        // Event "*" and DriverTypeMask "0x0000FFFF" are required by this firmware.
        var queryJson = JsonSerializer.Serialize(new
        {
            Name        = "OPFileQuery",
            OPFileQuery = new
            {
                BeginTime      = from.ToString("yyyy-MM-dd HH:mm:ss"),
                EndTime        = to.ToString("yyyy-MM-dd HH:mm:ss"),
                Channel        = channel,
                DriverTypeMask = "0x0000FFFF",
                Event          = "*",
                Type           = "*"
            },
            SessionID = $"0x{_sessionId:X8}",
            Magic     = "0x1234"
        });

        var response = await SendAndReceiveAsync(MsgFileQueryRequest, queryJson, ct);
        var json = Encoding.UTF8.GetString(response.Payload).TrimEnd('\0');
        return ParseFileQueryResponse(json);
    }

    /// <summary>
    /// Downloads a recorded file, converts it via ffmpeg, and returns the output path.
    /// Supported output formats: "mp4" (default, H.264+AAC, faststart) or "mkv" (H.264+copy audio).
    ///
    /// Confirmed download flow (tested against Movols/Xiongmai NVR):
    ///   1. Fresh TCP connection + re-login (required per file)
    ///   2. OPPlayBack Claim  (msgID 1424 → response 1425)
    ///   3. OPPlayBack DownloadStart (msgID 1420) — NVR responds immediately with 1426 data
    ///   4. Read DOWNLOAD_DATA packets (msgID 1426) until FileLengthBytes received
    ///   5. ffmpeg conversion: raw HEVC stream → MP4 or MKV
    /// </summary>
    public async Task<string> DownloadFileAsync(
        NvrFile file, string destinationPath, string outputFormat = "mp4",
        IProgress<long>? progress = null, TimeSpan? trimFromSegmentStart = null,
        CancellationToken ct = default)
    {
        // Fresh connection + re-login required for each file download
        await ReconnectAsync(ct);

        // Step 1: Claim
        var claimJson = JsonSerializer.Serialize(new
        {
            Name       = "OPPlayBack",
            OPPlayBack = new
            {
                Action    = "Claim",
                StartTime = file.BeginTime.ToString("yyyy-MM-dd HH:mm:ss"),
                EndTime   = file.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Parameter = new
                {
                    FileName  = file.FileName,
                    PlayMode  = "ByName",
                    TransMode = "TCP",
                    Value     = 0
                }
            },
            SessionID = $"0x{_sessionId:X8}"
        });

        var claimResp = await SendAndReceiveAsync(MsgPlayClaimRequest, claimJson, ct);
        var claimRespJson = Encoding.UTF8.GetString(claimResp.Payload).TrimEnd('\0');
        using (var claimDoc = JsonDocument.Parse(claimRespJson))
        {
            var ret = GetNvrProp(claimDoc.RootElement, "Ret").GetInt32();
            if (ret != 100)
                throw new InvalidOperationException($"OPPlayBack Claim failed — NVR returned code {ret}");
        }

        // Step 2: DownloadStart
        var startJson = JsonSerializer.Serialize(new
        {
            Name       = "OPPlayBack",
            OPPlayBack = new
            {
                Action    = "DownloadStart",
                StartTime = file.BeginTime.ToString("yyyy-MM-dd HH:mm:ss"),
                EndTime   = file.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Parameter = new
                {
                    FileName  = file.FileName,
                    PlayMode  = "ByName",
                    TransMode = "TCP",
                    Value     = 0
                }
            },
            SessionID = $"0x{_sessionId:X8}"
        });

        // Step 3: Send DownloadStart — the NVR responds immediately with 1426 data packets.
        // Do NOT await a JSON response; fire the command and go straight into the read loop.
        var startPayload = Encoding.UTF8.GetBytes(startJson + "\0");
        var startPacket  = DvripPacket.Build(_sessionId, _seqNum++, MsgPlayBackRequest, startPayload);
        await _stream!.WriteAsync(startPacket, ct);

        // Step 4: Read DOWNLOAD_DATA packets (msgID 1426) until FileLengthBytes received.
        // NVR naturally closes stream or sends non-1426 when done (Ret 103 is normal).
        await using (var fileStream = File.OpenWrite(destinationPath))
        {
            long bytesWritten = 0;
            while (bytesWritten < file.FileLengthBytes && !ct.IsCancellationRequested)
            {
                DvripPacket pkt;
                try { pkt = await ReceivePacketWithTimeoutAsync(ct); }
                catch (EndOfStreamException) { break; }

                if (pkt.MessageId != MsgDownloadData || pkt.Payload.Length == 0)
                    break;

                await fileStream.WriteAsync(pkt.Payload, ct);
                bytesWritten += pkt.Payload.Length;
                progress?.Report(bytesWritten);
            }
        }

        // Step 5: Convert raw DVRIP/HEVC stream to target format
        // S22n: trimFromSegmentStart — oreže na 15-min okno (offset od začiatku segmentu)
        return await ConvertVideoAsync(destinationPath, outputFormat, trimFromSegmentStart, ct);
    }

    // ── Private connection helpers ────────────────────────────────────────────

    private async Task ReconnectAsync(CancellationToken ct)
    {
        // S22i: najprv odhlásiť starú session (msg 1001) — inak NVR drží session
        // aj po TCP close a každý analyze download zaplní NVR (Ret 205).
        if (_stream is not null && _tcp is { Connected: true })
        {
            try
            {
                var logoutJson = JsonSerializer.Serialize(new
                {
                    Name = "OPLogout",
                    SessionID = $"0x{_sessionId:X8}"
                });
                var payload = Encoding.UTF8.GetBytes(logoutJson + "\0");
                var packet = DvripPacket.Build(_sessionId, _seqNum++, MsgLogoutRequest, payload);
                await _stream.WriteAsync(packet, ct);
                await _stream.FlushAsync(ct);
            }
            catch { /* best effort */ }
        }
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
        await LoginAsync(ct);
    }

    // ── Private ffmpeg helpers ────────────────────────────────────────────────

    private static async Task<string> ConvertVideoAsync(string rawPath, string outputFormat,
        TimeSpan? trimFromSegmentStart, CancellationToken ct)
    {
        var format    = outputFormat.ToLowerInvariant() is "mkv" ? "mkv" : "mp4";
        var outputPath = Path.ChangeExtension(rawPath, "." + format);

        // BUG FIX (S22j): ak rawPath už má cieľovú príponu (napr. volanie
        // DownloadFileAsync(..., "x.mkv", "mkv")), outputPath == rawPath —
        // ffmpeg by konvertoval súbor do seba (zlyhá) a cleanup by VYMAZAL
        // jediný stiahnutý súbor. Vtedy nie je čo konvertovať — vrátime raw.
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(rawPath), StringComparison.Ordinal))
            return rawPath;

        var ffmpegExe = FindExecutable("ffmpeg");
        if (ffmpegExe is null)
        {
            Console.WriteLine("         ⚠️  ffmpeg not found — keeping raw file");
            return rawPath;
        }

        // MP4: re-encode to H.264 + AAC with faststart (browser-compatible)
        // MKV: copy video stream, re-encode audio to AAC
        // S22m: scale na 1080p (1920:1080) — 4K H.264 by bol ~1.5 GB/15 min (14.5 Mbit/s),
        // 1080p ~400 MB (4× menej); analýza aj tak beží na 1080p, 4K ostáva na NVR disku.
        // S22n: trim na 15-min okno — NVR segmenty sú variabilné (5–60+ min); lokálna
        // nahrávka má byť VŽDY presne WindowMinutes (výsek z dlhšieho segmentu). -ss pred
        // vstupom = rýchly seek (keyframe), -t dĺžka okna (15 min). Offset od začiatku segmentu.
        const double WindowMinutesForTrim = 15 * 60; // S22n: 15-min okno (sekundy)
        var trim = trimFromSegmentStart is not null
            ? $"-ss {trimFromSegmentStart.Value.TotalSeconds:0.##} -t {WindowMinutesForTrim:0.##} "
            : "";
        // DÔLEŽITÉ: raw stream z NVR je HEVC s dĺžkovými prefixami (hlavička 0000 01fc),
        // NIE Annex B — ffmpeg auto-detekcia ho nepozná. Skúšame najprv auto-detekciu
        // (funguje pre MP4/MKV vstupy v testoch), fallback na -f hevc (produkčné raw).
        var scale = "-vf scale=1920:1080";
        var ffmpegArgs = format == "mp4"
            ? $"-v error -y {trim}-i \"{rawPath}\" {scale} -c:v libx264 -preset fast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\""
            : $"-v error -y {trim}-i \"{rawPath}\" {scale} -c:v libx264 -crf 23 -c:a copy \"{outputPath}\"";
        var hevcArgs = format == "mp4"
            ? $"-v error -y {trim}-f hevc -i \"{rawPath}\" {scale} -c:v libx264 -preset fast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\""
            : $"-v error -y {trim}-f hevc -i \"{rawPath}\" {scale} -c:v libx264 -crf 23 -c:a copy \"{outputPath}\"";

        if (!await RunFfmpegAsync(ffmpegExe, ffmpegArgs, ct) || !File.Exists(outputPath))
        {
            // Fallback: produkčný raw HEVC z NVR (dĺžkové prefixy) — auto-detekcia zlyhá.
            // Čiastočný/0-bajtový output z prvého pokusu zmazať (ffmpeg pri zlyhaní ho nechá).
            if (File.Exists(outputPath)) File.Delete(outputPath);
            if (await RunFfmpegAsync(ffmpegExe, hevcArgs, ct) && File.Exists(outputPath))
            {
                File.Delete(rawPath);
                return outputPath;
            }
        }
        else
        {
            File.Delete(rawPath);
            return outputPath;
        }

        // Oba pokusy zlyhali — odstrániť čiastočný output, nechať raw
        if (File.Exists(outputPath)) File.Delete(outputPath);
        Console.WriteLine($"         ⚠️  ffmpeg conversion failed — keeping raw file");
        return rawPath;
    }

    /// <summary>Spustí ffmpeg a počká; stderr sa číta priebežne (pipe deadlock fix).</summary>
    private static async Task<bool> RunFfmpegAsync(string ffmpegExe, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpegExe)
        {
            Arguments             = arguments,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");
        // stderr sa MUSÍ čítať priebežne — inak sa pipe (64 KB) zaplní a ffmpeg zablokuje
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        await stderrTask;
        return proc.ExitCode == 0;
    }

    private static string? FindExecutable(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
            if (File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        return null;
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

    /// <summary>
    /// Generický DVRIP command (OPMachine, OPStorage, OPChannelSetting, OPNetWork…).
    /// Pošle JSON s Name + SessionID + Magic a vráti RAW odpoveď JSON (string).
    /// Užitočné pre diagnostiku NVR a pre S18 (live streaming príkazy).
    /// </summary>
    public async Task<string> SendCommandAsync(string name, string payloadJson = "{}", CancellationToken ct = default)
    {
        // DVRIP príkazy: OPMachine=1020, OPStorage=1030, OPChannelSetting=1040,
        // OPNetWork=1050, OPTime=1060, OPCamera=1070 (Xiongmai/Sofia firmware).
        var msgId = name switch
        {
            "OPMachine"         => (ushort)1020,
            "OPStorage"         => (ushort)1030,
            "OPChannelSetting"  => (ushort)1040,
            "OPNetWork"         => (ushort)1050,
            "OPTime"            => (ushort)1060,
            "OPCamera"          => (ushort)1070,
            _                   => (ushort)1020,
        };

        // Ak payload neobsahuje Name, obalíme ho správne (Name + SessionID + Magic)
        string json;
        if (payloadJson.Contains("\"Name\""))
        {
            json = payloadJson;
        }
        else
        {
            json = $"{{\"Name\":\"{name}\",\"{name}\":{payloadJson},\"SessionID\":\"0x{_sessionId:X8}\",\"Magic\":\"0x1234\"}}";
        }

        var response = await SendAndReceiveAsync(msgId, json, ct);
        return Encoding.UTF8.GetString(response.Payload).TrimEnd('\0');
    }

    /// <summary>
    /// S19: Live monitor stream z NVR cez OPMonitor (Xiongmai). Claim + Start,
    /// potom volá <paramref name="onData"/> pre každý paket s videom (HEVC).
    /// Po skončení (cancellation/výnimka) automaticky pošle Stop a uvoľní claim.
    /// StreamType: "Main" (4K) alebo "Extra" (800×448 — odporúčané pre live view).
    /// </summary>
    public async Task MonitorStreamAsync(
        int channel, string streamType, Func<ReadOnlyMemory<byte>, CancellationToken, Task> onData,
        CancellationToken ct = default)
    {
        // Claim (msg 1413)
        var claimJson = JsonSerializer.Serialize(new
        {
            Name = "OPMonitor",
            SessionID = $"0x{_sessionId:X8}",
            OPMonitor = new
            {
                Action = "Claim",
                Parameter = new { Channel = channel, StreamType = streamType, TransMode = "TCP" }
            }
        });
        var claimResp = await SendAndReceiveAsync(MsgMonitorClaim, claimJson, ct);
        var claimText = Encoding.UTF8.GetString(claimResp.Payload).TrimEnd('\0');
        if (!claimText.Contains("\"Ret\" : 100") && !claimText.Contains("\"Ret\":100"))
            throw new InvalidOperationException($"OPMonitor Claim failed (channel {channel}): {claimText}");

        try
        {
            // Start (msg 1410) — NVR odpovedá PRIAMO dátami (1412), nie JSON.
            // Preto LEN pošleme; prvý dátový paket (SPS/PPS + keyframe) nesmieme
            // zhltnúť v SendAndReceiveAsync — ffmpeg by bez neho čakal na parametre.
            var startJson = JsonSerializer.Serialize(new
            {
                Name = "OPMonitor",
                SessionID = $"0x{_sessionId:X8}",
                OPMonitor = new
                {
                    Action = "Start",
                    Parameter = new { Channel = channel, StreamType = streamType, TransMode = "TCP" }
                }
            });
            var startPayload = Encoding.UTF8.GetBytes(startJson + "\0");
            var startPacket = DvripPacket.Build(_sessionId, _seqNum++, MsgMonitor, startPayload);
            await _stream!.WriteAsync(startPacket, ct);

            // Čítame dáta (msg 1412) — NVR posiela HEVC pakety
            while (!ct.IsCancellationRequested)
            {
                var packet = await ReceivePacketWithTimeoutAsync(ct);
                if (packet.MessageId == MsgMonitorData)
                    await onData(packet.Payload, ct);
            }
        }
        finally
        {
            // Stop (msg 1410) — uvoľnenie monitoru
            try
            {
                var stopJson = JsonSerializer.Serialize(new
                {
                    Name = "OPMonitor",
                    SessionID = $"0x{_sessionId:X8}",
                    OPMonitor = new
                    {
                        Action = "Stop",
                        Parameter = new { Channel = channel, StreamType = streamType, TransMode = "TCP" }
                    }
                });
                await SendAndReceiveAsync(MsgMonitor, stopJson, CancellationToken.None);
            }
            catch { /* best effort — NVR uvoľní claim aj sám */ }
        }
    }

    private async Task<DvripPacket> SendAndReceiveAsync(ushort msgId, string json, CancellationToken ct)
    {        // JSON payload is null-terminated per the DVRIP spec; length field includes the null byte.
        var payload = Encoding.UTF8.GetBytes(json + "\0");
        var packet  = DvripPacket.Build(_sessionId, _seqNum++, msgId, payload);
        await _stream!.WriteAsync(packet, ct);
        return await ReceivePacketWithTimeoutAsync(ct);
    }

    /// <summary>
    /// Číta jeden paket s read timeoutom. NetworkStream.ReadTimeout nefunguje pre
    /// async ReadAsync, preto používame Task.WaitAsync(timeout) — spoľahlivá ochrana
    /// pred zamrznutým NVR (S3-4). Pri timeout vyhodí <see cref="IOException"/>.
    /// </summary>
    private async Task<DvripPacket> ReceivePacketWithTimeoutAsync(CancellationToken ct)
    {
        var readTask = ReceivePacketAsync(ct);
        try
        {
            return await readTask.WaitAsync(TimeSpan.FromSeconds(_readTimeoutSeconds), ct);
        }
        catch (TimeoutException)
        {
            throw new IOException(
                $"DVRIP read timed out after {_readTimeoutSeconds}s — NVR nereaguje (msgId {readTask.Status}).");
        }
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
        // S22h: odhlásiť sa z NVR (msg 1001) — inak NVR drží session, kým nevyprší
        // timeout (analyze joby robili stovky downloadov → NVR sessiony sa zaplnili
        // → nové pripojenia vracali Ret 205). Best-effort, nikdy nehádzať.
        if (_stream is not null && _tcp is { Connected: true })
        {
            try
            {
                var logoutJson = JsonSerializer.Serialize(new
                {
                    Name = "OPLogout",
                    SessionID = $"0x{_sessionId:X8}"
                });
                var payload = Encoding.UTF8.GetBytes(logoutJson + "\0");
                var packet = DvripPacket.Build(_sessionId, _seqNum++, MsgLogoutRequest, payload);
                _stream.Write(packet);
                _stream.Flush();
            }
            catch { /* best effort — TCP close uvoľní aj bez logout */ }
        }
        _stream?.Dispose();
        _tcp?.Dispose();
    }
}
