#!/usr/bin/env bash
# Produkčný deploy WatchForge na hp-camera-hub (S21) — systemd user services.
# Spúšťa API + Runner + UI serve natívne (Release binárky), prežije logout.
# Použitie: deploy/scripts/prod-up.sh
set -euo pipefail

WF_DIR="$HOME/workspace/watchforge"
SERVICES_DIR="$HOME/.config/systemd/user"
DATA_DIR="$HOME/watchforge-data"
MEDIA_DIR="$HOME/watchforge-media"
NVR_PASSWORD=$(grep -oE 'password: *"?[A-Za-z0-9]+' "$WF_DIR/deploy/secrets/nvr-auth.yaml" | head -1 | grep -oE '[A-Za-z0-9]+$')

mkdir -p "$SERVICES_DIR" "$DATA_DIR" "$MEDIA_DIR/tmp" "$MEDIA_DIR/clips"

# ── 1. Seed produkčnej DB (ak ešte neexistuje) ──
if [ ! -f "$DATA_DIR/watchforge.db" ]; then
  echo "── seed: produkčná DB ──"
  # Prázdna DB (schéma cez API prvý štart)
  touch "$DATA_DIR/watchforge.db"
fi

cat > "$SERVICES_DIR/watchforge-api.service" <<EOF
[Unit]
Description=WatchForge API (produkčný)
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=$WF_DIR/applications/server/WatchForge.Api/bin/Release/net10.0/WatchForge.Api
WorkingDirectory=$WF_DIR/applications/server/WatchForge.Api/bin/Release/net10.0
Environment=WATCHFORGE__API__DBPATH=$DATA_DIR/watchforge.db
Environment=WATCHFORGE__API__CORS__ALLOWEDORIGINS__0=http://localhost
Environment=WATCHFORGE__API__CORS__ALLOWEDORIGINS__1=http://localhost:4200
Environment=WATCHFORGE__API__CORS__ALLOWEDORIGINS__2=http://localhost:8080
Environment=WATCHFORGE_NVR_PASSWORD=$NVR_PASSWORD
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
EOF

cat > "$SERVICES_DIR/watchforge-runner.service" <<EOF
[Unit]
Description=WatchForge Runner (produkčný)
After=network-online.target watchforge-api.service
Wants=network-online.target

[Service]
ExecStart=$WF_DIR/applications/services/WatchForge.Runner/bin/Release/net10.0/WatchForge.Runner
WorkingDirectory=$WF_DIR/applications/services/WatchForge.Runner/bin/Release/net10.0
Environment=WATCHFORGE__RUNNER__DBPATH=$DATA_DIR/watchforge.db
Environment=WATCHFORGE__RUNNER__DOWNLOAD__TEMPDIR=$MEDIA_DIR/tmp
Environment=WATCHFORGE__RUNNER__CLIPS__CLIPSDIR=$MEDIA_DIR/clips
Environment=WATCHFORGE__RUNNER__HTTPPORT=8081
Environment=WATCHFORGE_NVR_PASSWORD=$NVR_PASSWORD
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
EOF

cat > "$SERVICES_DIR/watchforge-web.service" <<EOF
[Unit]
Description=WatchForge Web UI (produkčný, bun serve)
After=network-online.target watchforge-api.service
Wants=network-online.target

[Service]
ExecStart=$(command -v bun) serve $WF_DIR/applications/web/WatchForge.UI/dist/watchforgeclient --port 4200
WorkingDirectory=$WF_DIR/applications/web/WatchForge.UI
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
EOF

systemctl --user daemon-reload
systemctl --user enable --now watchforge-api.service watchforge-runner.service watchforge-web.service

echo "── stav ──"
sleep 3
systemctl --user is-active watchforge-api watchforge-runner watchforge-web
echo "── health ──"
curl -s http://localhost:5000/api/v1/system/health || echo "API health zlyhal"
echo
