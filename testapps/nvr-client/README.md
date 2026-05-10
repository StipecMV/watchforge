# WatchForge NVR Client — DVRIP File Downloader

A .NET 10 console app for connecting to **Xiongmai/Sofia-based NVR devices** (tested on Movols brand) using the native **DVRIP** protocol. Logs in, queries recorded files across multiple channels, downloads them, and converts to MP4 or MKV via ffmpeg.

Two modes are supported: **oneshot** (download a specific time window once) and **infinite** (run continuously in a container, downloading new recordings as they appear).

## What Is DVRIP and Why Not ONVIF?

**DVRIP** (DVR Internet Protocol, also called NetSDK or Sofia protocol) is the proprietary binary TCP protocol used by Xiongmai-based NVR/DVR firmware (brand name: Sofia). It runs on port 34567 by default.

Although the NVR advertises partial ONVIF support, in practice the Movols/Xiongmai ONVIF implementation does not expose the Recording Search service, making it impossible to enumerate or download recordings through ONVIF alone. DVRIP provides full access to the file system, live streams, and recorded files.

## Project Structure

```
testapps/nvr-client/
├── Dockerfile
└── src/
    ├── WatchForge.DVRIP.Library/                # Reusable DVRIP protocol library
    │   ├── DvripClient.cs                       # Login, file query, download, ffmpeg
    │   ├── DvripPacket.cs                       # Packet build/parse
    │   └── Models/
    │       ├── LoginResult.cs
    │       └── NvrFile.cs
    ├── WatchForge.NVR.Client.TestApp/           # Console app (references Library)
    │   ├── Program.cs                           # Entry point, oneshot + infinite modes
    │   ├── DownloadStateService.cs              # Tracks downloaded files (downloaded.json)
    │   └── appsettings.json
    └── WatchForge.NVR.Client.TestApp.Tests/     # Unit tests (TUnit, references Library)
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
    "DownloadDir": "your_download_folder",

    "Mode": "infinite",
    "OutputFormat": "mp4",
    "PollIntervalSeconds": 60,
    "Channels": [ 0, 1, 2, 3, 4, 5 ],

    "StartTime": "2026-04-05T15:30:00",
    "DurationMinutes": 15
  }
}
```

| Key | Values | Description |
|-----|--------|-------------|
| `Mode` | `oneshot` / `infinite` | Run mode (default: `oneshot`) |
| `OutputFormat` | `mp4` / `mkv` | ffmpeg output format (default: `mp4`) |
| `PollIntervalSeconds` | integer | Seconds between polls in infinite mode (default: `60`) |
| `Channels` | int array | Zero-indexed channel list |
| `StartTime` | ISO datetime | **Oneshot only** — start of the query window |
| `DurationMinutes` | integer | **Oneshot only** — length of the query window (default: `60`) |

## Modes

### Oneshot

Queries the NVR for recordings in a specific time window (`StartTime` + `DurationMinutes`), downloads them, and exits.

```bash
dotnet run --project src/WatchForge.NVR.Client.TestApp
```

### Infinite

Runs continuously. On each poll it queries the entire available NVR history, skips files already downloaded (tracked in `downloaded.json` in the download dir), and downloads anything new. Ideal for running in a container.

```bash
# appsettings.json: "Mode": "infinite"
dotnet run --project src/WatchForge.NVR.Client.TestApp
```

Press **Ctrl+C** to stop — current downloads finish cleanly before the process exits.

State is persisted in `{DownloadDir}/downloaded.json`. If the container restarts, already-downloaded files are skipped automatically.

## How to Run Tests

```bash
# All tests in this sub-project
dotnet run --project src/WatchForge.NVR.Client.TestApp.Tests

# All tests in the solution
dotnet test WatchForge.slnx
```

## Running in a Container (Podman / Docker)

### Build the image

```bash
cd testapps/nvr-client
podman build -t watchforge-nvr-client .
```

### Run oneshot

```bash
podman run --rm \
  -v /your/host/downloads:/downloads \
  -e Dvrip__Host=192.168.1.100 \
  -e Dvrip__Username=admin \
  -e Dvrip__Password=yourpassword \
  -e Dvrip__DownloadDir=/downloads \
  -e Dvrip__Mode=oneshot \
  -e Dvrip__StartTime="2026-04-05T15:30:00" \
  -e Dvrip__DurationMinutes=60 \
  watchforge-nvr-client
```

### Run infinite mode (auto-restart on crash)

```bash
podman run -d \
  --name nvr-downloader \
  --restart=unless-stopped \
  -v /your/host/downloads:/downloads \
  -e Dvrip__Host=192.168.1.100 \
  -e Dvrip__Username=admin \
  -e Dvrip__Password=yourpassword \
  -e Dvrip__DownloadDir=/downloads \
  -e Dvrip__Mode=infinite \
  -e Dvrip__OutputFormat=mp4 \
  -e Dvrip__PollIntervalSeconds=60 \
  -e Dvrip__Channels__0=0 \
  -e Dvrip__Channels__1=1 \
  -e Dvrip__Channels__2=2 \
  watchforge-nvr-client
```

Environment variables override `appsettings.json` using the standard .NET double-underscore separator (e.g. `Dvrip__Host` = `"Dvrip:Host"`).

To view logs:

```bash
podman logs -f nvr-downloader
```

## Expected Output — Oneshot

```
🔧 WatchForge DVRIP File Downloader
====================================

   Mode         : ONESHOT
   Output format: MP4
   Download dir : /downloads

🔌 Connecting to 192.168.1.100:34567 ...
✅ Login OK
   Device type  : HVR
   Session ID   : 0x0000001B

🔍 Querying 6 channel(s)  2026-04-05 15:30 → 15:45
   Channel 0: 1 file(s)
   Channel 1: 1 file(s)
   ...

📂 Found 6 file(s) across 6 channel(s):
   📹 [Ch0] 15.30.00-15.45.00[R][@da7ed][1].h264  15:30:00-15:45:00  [502.1 MB]
   ...

📥 Downloading 6 file(s) to /downloads  [MP4]

   ⬇️  [Ch0 1/1] [Ch0]_2026-04-05_15.30.00-15.45.00.mp4  (502.1 MB)
      [Ch0] 100 MB / 502 MB (19%)
      [Ch0] 200 MB / 502 MB (39%)
      ...
      ✅ [Ch0] → [Ch0]_2026-04-05_15.30.00-15.45.00.mp4

====================================
✅ Done!  6 downloaded.
```

## Expected Output — Infinite Mode

```
🔧 WatchForge DVRIP File Downloader
====================================

   Mode         : INFINITE
   Output format: MP4
   Download dir : /downloads

♾️  Infinite mode — polling every 60s
   State file   : /downloads/downloaded.json
   Already known: 42 file(s)

[14:00:01] 🔄 Poll #1
🔌 Connecting to 192.168.1.100:34567 ...
✅ Login OK
   Found 48 total, 42 already downloaded, 6 new

   ⬇️  [Ch0 1/1] [Ch0]_2026-04-13_14.00.00-15.00.00.mp4  (1024.0 MB)
      ...
      ✅ [Ch0] → [Ch0]_2026-04-13_14.00.00-15.00.00.mp4
   ✅ 6 downloaded

   ⏳ Next poll in 60s  (Ctrl+C to stop)

[14:01:01] 🔄 Poll #2
   Found 48 total, 48 already downloaded, 0 new
   ⏳ Next poll in 60s  (Ctrl+C to stop)
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
5. **ffmpeg conversion** — raw HEVC stream → MP4 (`-c:v libx264 -preset fast -crf 23 -c:a aac -movflags +faststart`) or MKV (`-c:v libx264 -crf 23 -c:a copy`)

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
