<p align="left">
  <img src="logo.jpeg" alt="WatchForge Logo" width="150"/>
</p>

# WatchForge

WatchForge is a self-hosted video processing for NVR (Network Video Recorder) system to enhance pixel precision motion detection.

##  Project Structure

```
watchforge/
├── WatchForge.slnx              # Main .NET solution file
├── testapps/                    # Test applications
│   └── nvr-client/              # .NET NVR Client for ONVIF devices
│       └── src/
│           ├── WatchForge.NVR.Client.Core/
│           └── WatchForge.NVR.Client.TestApp/
├── client/                      # Frontend client application
├── db/
│   └── queries/                 # Database schema and queries
└── server/                      # Dotnet-based server components
```

## ✨ Features

### NVR Client (.NET)
- 📹 ONVIF Client - Full ONVIF protocol support
- 🔧 SharpOnvif - Uses actively maintained SharpOnvif library
- 🏗️ SOLID Architecture - Clean code with dependency injection
- 🖥️ Cross-platform - .NET 10 supported on Windows, macOS, Linux (x64/arm64 and other supported runtimes)
- 🧪 Test project not included in this repository version (no `tests/` folder or `WatchForge.NVR.Client.TestApp.Tests` present)
- ✅ Codelab coverage recommendation: add CI test suite if required for your fork

## 🛠️ Tech Stack

### .NET Components
- **Framework**: .NET 10
- **ONVIF**: SharpOnvif library
- **Platform**: Windows, macOS, Linux (x64, ARM64)
- **Testing**: NUnit + TUnit with code coverage

## 👨‍💻 Getting Started

### Prerequisites

#### For .NET Components
- .NET 10 SDK
- Any .NET 10 runtime platform (Windows, macOS, Linux x64/arm64)

### Clone the Repository

```bash
git clone https://github.com/StipecMV/watchforge
cd watchforge
```

### NVR Client Quick Start

See [testapps/nvr-client/README.md](testapps/nvr-client/README.md) for:
- Installation and configuration
- Build instructions (all platforms)
- Run and test commands
- Publishing options

## � NVR Client details

For full architecture, run instructions, tests, and environment variable docs see:

- [testapps/nvr-client/README.md](testapps/nvr-client/README.md)

## � Architecture

See [testapps/nvr-client/README.md](testapps/nvr-client/README.md#-architecture) for the detailed architecture diagram.

## �📝 License

WatchForge is an open-source project. See the [LICENSE](LICENSE) file for details.
