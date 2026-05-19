<p align="left">
  <img src="logo.jpeg" alt="WatchForge Logo" width="150"/>
</p>

# WatchForge

WatchForge is a self-hosted video processing for NVR (Network Video Recorder) system to enhance pixel precision motion detection.

##  Project Structure

```
watchforge/
├── WatchForge.slnx              # Main .NET solution file
├── apps/
│   ├── web/                     # Angular frontend
│   ├── api/                     # REST API server (.NET)
│   └── services/
│       ├── motion-sentinel/     # Linux Worker Service — OpenCV motion detection
│       │   ├── WatchForge.MotionSentinel.Server.Service/
│       │   └── WatchForge.MotionSentinel.Server.Service.Tests/
│       └── WatchForge.DVRIP.Service/  # DVRIP file downloader for Xiongmai/Sofia NVR
├── libs/
│   ├── WatchForge.DVRIP.Library/           # DVRIP protocol library (NuGet)
│   ├── WatchForge.DVRIP.Library.Tests/     # Unit tests for DVRIP library
│   ├── WatchForge.MotionSentinel.Library/  # Motion detection library (NuGet)
│   └── WatchForge.MotionSentinel.Library.Tests/  # Unit tests for MotionSentinel library
└── db/
    └── queries/                 # Database schema and queries
```

## ✨ Features

### MotionSentinel (.NET · Linux)
- 🎯 Farneback Dense Optical Flow — per-frame motion region detection using OpenCV
- 📁 Local filesystem — watches NVR recordings folder, writes JSON detection results
- ⚡ FileSystemWatcher + backfill — picks up files written while the service was down
- 🔌 Headless Worker Service — runs as a systemd unit, no UI required
- 🧪 test coverage via TUnit + Moq

See [apps/services/motion-sentinel/README.md](apps/services/motion-sentinel/README.md) for full docs.

### NVR Client (.NET · Linux)
- 📡 DVRIP protocol — native Xiongmai/Sofia TCP protocol (port 34567)
- 🔐 Sofia MD5 login, file listing, best-effort H.264 file download
- 🖥️ .NET 10, Linux (x64, arm64)
- 🧪 test coverage via TUnit + Moq
See [apps/services/WatchForge.DVRIP.Service/README.md](apps/services/WatchForge.DVRIP.Service/README.md) for full docs.

## 🛠️ Tech Stack

### .NET Components
- **Framework**: .NET 10
- **NVR protocol**: DVRIP (raw TCP, Xiongmai/Sofia firmware)
- **Motion detection**: OpenCV (Farneback optical flow) via OpenCvSharp4
- **Platform**: Linux (x64);
- **Testing**: TUnit + Moq with code coverage

## 👨‍💻 Getting Started

### Prerequisites

#### For .NET Components
- .NET 10 SDK
- Any .NET 10 runtime platform (Linux x64)

### Clone the Repository

```bash
git clone https://github.com/StipecMV/watchforge
cd watchforge
```

## �📝 License

WatchForge is an open-source project. See the [LICENSE](LICENSE) file for details.
