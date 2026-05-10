# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so dotnet restore is cached separately from source
COPY src/WatchForge.DVRIP.Library/WatchForge.DVRIP.Library.csproj \
     src/WatchForge.DVRIP.Library/
COPY src/WatchForge.NVR.Client.TestApp/WatchForge.NVR.Client.TestApp.csproj \
     src/WatchForge.NVR.Client.TestApp/

RUN dotnet restore src/WatchForge.NVR.Client.TestApp/WatchForge.NVR.Client.TestApp.csproj \
    --runtime linux-x64

COPY src/ src/

RUN dotnet publish src/WatchForge.NVR.Client.TestApp/WatchForge.NVR.Client.TestApp.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output /app/publish \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM debian:bookworm-slim AS runtime

# Install ffmpeg (required for MP4/MKV conversion after download)
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish/WatchForge.NVR.Client.TestApp .
COPY --from=build /app/publish/appsettings.json .

# Downloads and state file land here; mount a host volume to persist them.
# Example: -v /your/host/downloads:/downloads
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

ENTRYPOINT ["./WatchForge.NVR.Client.TestApp"]
