# WatchForge NVR Client — DVRIP File Downloader

A .NET 10 console test app for connecting to **Xiongmai/Sofia-based NVR devices** (tested on Movols brand) using the native **DVRIP** protocol. Logs in, lists recorded files across multiple channels, downloads them, and converts to MKV via ffmpeg.

## What Is DVRIP and Why Not ONVIF?

**DVRIP** (DVR Internet Protocol, also called NetSDK or Sofia protocol) is the proprietary binary TCP protocol used by Xiongmai-based NVR/DVR firmware (brand name: Sofia). It runs on port 34567 by default.

Although the NVR advertises partial ONVIF support, in practice the Movols/Xiongmai ONVIF implementation does not expose the Recording Search service, making it impossible to enumerate or download recordings through ONVIF alone. DVRIP provides full access to the file system, live streams, and recorded files.

## Project Structure

```
testapps/nvr-client/
└── src/
    ├── WatchForge.NVR.Client.TestApp/           # DVRIP console app
    │   ├── Program.cs                           # Entry point
    │   ├── DvripClient.cs                       # Login, file query, download
    │   ├── DvripPacket.cs                       # Packet build/parse
    │   ├── Models/
    │   │   ├── LoginResult.cs
    │   │   └── NvrFile.cs
    │   └── appsettings.json
    └── WatchForge.NVR.Client.TestApp.Tests/     # Unit tests (TUnit)
        ├── NvrFileTests.cs
        ├── SofiaPasswordTests.cs
        ├── DvripPacketTests.cs
        └── FileQueryTests.cs
```

## Configuration

Edit `appsettings.json` in `WatchForge.NVR.Client.TestApp/`:

```json
{
  "Dvrip": {
    "Host": "your_nvr_ip_address",
    "Port": 34567,
    "Username": "your_username",
    "Password": "your_password",
    "DownloadDir": "your_download_folder"
  }
}
```

The app also has a hardcoded query window (`from`/`to`) and channel list in `Program.cs` — edit those to target the desired time range and cameras.

## How to Run

```bash
cd testapps/nvr-client/src/WatchForge.NVR.Client.TestApp
dotnet run
```

Or from the repo root:

```bash
dotnet run --project testapps/nvr-client/src/WatchForge.NVR.Client.TestApp
```

ffmpeg must be on `PATH` for automatic MKV conversion after download. If not found, the raw stream file is kept instead.

## How to Run Tests

```bash
# All tests in this sub-project
dotnet run --project testapps/nvr-client/src/WatchForge.NVR.Client.TestApp.Tests

# All tests in the solution
dotnet test --solution WatchForge.slnx
```

## Expected Output

```
🔧 WatchForge DVRIP File Downloader
====================================

🔌 Connecting to your_nvr_ip_address:34567 ...
✅ Login OK
   Device type  : HVR
   Channels     : 9
   Session ID   : 0x0000001B
   Keep-alive   : 21s

🔍 Querying channels 0–5  2026-04-05 15:30:00 → 15:45:00

   Channel 0: 1 file(s)
   Channel 1: 1 file(s)
   ...

📂 Found 6 file(s) across all channels:
   📹 /idea0/2026-04-05/002/15.30.00-15.45.00[R][@da7ed][1].h264
       2026-04-05 15:30:00 → 15:45:00  [502.3 MB]
   ...

⬇️  Downloading 6 file(s) → your_download_folder

   [1/6] 15.30.00-15.45.00[R][@da7ed][1].h264  (502.3 MB)
         Downloading 502.3 MB of 502.3 MB (100%)
         ✅ your_download_folder/15.30.00-15.45.00[R][@da7ed][1].mkv
         Preview: ffplay "your_download_folder/15.30.00-15.45.00[R][@da7ed][1].mkv"

====================================
✅ Done!  6 downloaded, 0 failed.
```

## DVRIP Protocol Notes

### Confirmed message IDs (Movols HVR, Sofia firmware)

| ID   | Direction | Purpose                        |
|------|-----------|--------------------------------|
| 1000 | Request   | Login                          |
| 1001 | Response  | Login                          |
| 1440 | Request   | OPFileQuery (file listing)     |
| 1441 | Response  | OPFileQuery                    |
| 1424 | Request   | OPPlayBack Claim               |
| 1425 | Response  | OPPlayBack Claim               |
| 1420 | Request   | OPPlayBack DownloadStart       |
| 1426 | Data      | Download data packets          |

### Confirmed download flow

1. **Fresh TCP connection + re-login** — required for each file download
2. **OPPlayBack Claim** (msgID 1424) — await JSON response, check `Ret == 100`
3. **OPPlayBack DownloadStart** (msgID 1420) — fire-and-forget; the NVR immediately starts streaming binary data packets without a JSON handshake response
4. **Read msgID 1426 data packets** until `FileLengthBytes` received or stream closes
5. **ffmpeg conversion** — raw HEVC stream → MKV (`-f hevc -i raw -c:v copy -c:a aac output.mkv`)

### Authentication

This firmware rejects the standard Sofia MD5 hash (`EncryptType "MD5"`) with code 203. Plain-text credentials (`EncryptType "None"`) work correctly (returns code 100).

### DVRIP Packet Format

| Offset | Length | Field                        |
|--------|--------|------------------------------|
| 0      | 1      | Magic = 0xFF                 |
| 1      | 1      | Version = 0x00               |
| 2–3    | 2      | Reserved                     |
| 4–7    | 4      | Session ID (uint32 LE)       |
| 8–11   | 4      | Sequence number (uint32 LE)  |
| 12     | 1      | Total packets                |
| 13     | 1      | Current packet               |
| 14–15  | 2      | Message ID (uint16 LE)       |
| 16–19  | 4      | Payload length (uint32 LE)   |
| 20+    | N      | JSON payload (UTF-8, null-terminated) |

### Known NVR Quirk — Malformed Datetime in FileQuery Response

The Movols/Sofia firmware emits datetime strings **without the space** between date and time in FileQuery responses:

```
Standard: "2026-04-02 10:08:02"
NVR emits: "2026-04-0210:08:02"   ← no space
```

`NvrFile.ParseNvrDateTime()` handles both formats. DVRIP requests use the standard format; only the NVR response is malformed.

### FileLength Units

The `FileLength` field in OPFileQuery responses is in **1024-byte blocks**, not bytes. `NvrFile.ParseFileLength()` multiplies the parsed value by 1024.
