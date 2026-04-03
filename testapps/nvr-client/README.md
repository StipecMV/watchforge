# WatchForge NVR Client — DVRIP File Downloader

A .NET 10 console test app for connecting to **Xiongmai/Sofia-based NVR devices** (tested on Movols brand) using the native **DVRIP** protocol. Lists and downloads recorded H.264 files over raw TCP.

## What Is DVRIP and Why Not ONVIF?

**DVRIP** (DVR Internet Protocol, also called NetSDK or Sofia protocol) is the proprietary binary TCP protocol used by Xiongmai-based NVR/DVR firmware (brand name: Sofia). It runs on port 34567 by default.

Although the NVR advertises partial ONVIF support, in practice the Movols/Xiongmai ONVIF implementation does not expose the Recording Search service, making it impossible to enumerate or download recordings through ONVIF alone. DVRIP provides full access to the file system, live streams, and recorded files.

## NVR Details

| Field        | Value              |
|--------------|--------------------|
| Brand        | Movols (OEM Xiongmai, Sofia firmware) |
| Host         | 192.168.68.58      |
| DVRIP port   | 34567              |
| Device type  | HVR                |
| Channels     | 9 (6 active)       |

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
    └── WatchForge.NVR.Client.TestApp.Tests/     # Unit tests (TUnit + Moq)
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
    "Host": "192.168.68.58",
    "Port": 34567,
    "Username": "your_username",
    "Password": "your_password"
  }
}
```

## How to Run

```bash
cd testapps/nvr-client/src/WatchForge.NVR.Client.TestApp
dotnet run
```

Or from the repo root:

```bash
dotnet run --project testapps/nvr-client/src/WatchForge.NVR.Client.TestApp
```

## How to Run Tests

```bash
# All tests in this sub-project
dotnet test testapps/nvr-client/src/WatchForge.NVR.Client.TestApp.Tests

# All tests in the solution
dotnet test --solution WatchForge.slnx
```

## Expected Output

```
🔧 WatchForge DVRIP File Downloader
====================================

🔌 Connecting to 192.168.68.58:34567 ...
✅ Login OK
   Device type  : HVR
   Channels     : 9
   Session ID   : 0x0000001B
   Keep-alive   : 21s

🔍 Querying files  2026-03-27 → 2026-04-03 14:22:10

📂 Found 12 file(s):
   📹 /idea0/2026-04-02/002/10.08.02-10.30.00[R][@8600f][1].h264
       2026-04-02 10:08:02 → 10:30:00  [1.0 MB]
   ...

⬇️  Downloading: 10.08.02-10.30.00[R][@8600f][1].h264
   Destination : /home/user/10.08.02-10.30.00[R][@8600f][1].h264
   Expected    : 1.0 MB
   Progress    : 1.0 MB / 1.0 MB (100%)
✅ Download complete → /home/user/10.08.02-10.30.00[R][@8600f][1].h264

====================================
✅ Done!
```

## Known NVR Quirk — Malformed Datetime in FileQuery Response

The Movols/Sofia firmware emits datetime strings **without the space** between date and time in FileQuery responses:

```
Standard: "2026-04-02 10:08:02"
NVR emits: "2026-04-0210:08:02"   ← no space
```

`NvrFile.ParseNvrDateTime()` handles both formats. The DVRIP request uses the standard format with a space; only the response is malformed.

## DVRIP Packet Format

| Offset | Length | Field          |
|--------|--------|----------------|
| 0      | 1      | Magic = 0xFF   |
| 1      | 1      | Version = 0x00 |
| 2–3    | 2      | Reserved       |
| 4–7    | 4      | Session ID (uint32 LE) |
| 8–11   | 4      | Sequence number (uint32 LE) |
| 12     | 1      | Total packets  |
| 13     | 1      | Current packet |
| 14–15  | 2      | Message ID (uint16 LE) |
| 16–19  | 4      | Payload length (uint32 LE) |
| 20+    | N      | JSON payload (UTF-8, null-terminated) |

Key message IDs:

| ID   | Direction | Purpose         |
|------|-----------|-----------------|
| 1000 | Request   | Login           |
| 1001 | Response  | Login           |
| 1442 | Request   | OPFileQuery     |
| 1443 | Response  | OPFileQuery     |
| 1466 | Request   | OPPlayBack (download — see TODO in DvripClient.cs) |

## Download Status

File listing is fully implemented. File download is best-effort: the OPPlayBack message ID and post-handshake stream framing vary by firmware version. See the `TODO` block in [DvripClient.cs](src/WatchForge.NVR.Client.TestApp/DvripClient.cs) for details.
