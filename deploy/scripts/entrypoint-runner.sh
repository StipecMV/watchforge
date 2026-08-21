#!/bin/sh
# WatchForge Runner entrypoint (S9-1/S9-2): namapuje NVR auth (Podman secret
# alebo WATCHFORGE_NVR_AUTH_FILE súbor) na env var, ktorú Runner očakáva
# cez password_secret_env v DB.
set -e

# Priorita: 1) WATCHFORGE_NVR_AUTH_FILE (Quadlet/host mount), 2) Podman secret
SECRET_FILE=""
if [ -n "$WATCHFORGE_NVR_AUTH_FILE" ] && [ -f "$WATCHFORGE_NVR_AUTH_FILE" ]; then
    SECRET_FILE="$WATCHFORGE_NVR_AUTH_FILE"
    echo "NVR auth z WATCHFORGE_NVR_AUTH_FILE: $WATCHFORGE_NVR_AUTH_FILE"
elif [ -f "/run/secrets/nvr-auth" ]; then
    SECRET_FILE="/run/secrets/nvr-auth"
    echo "NVR auth z Podman secret /run/secrets/nvr-auth"
fi

if [ -n "$SECRET_FILE" ]; then
    # YAML: username: X / password: Y — načítať hodnoty (bez pyyaml v runtime image)
    NVR_USER=$(grep -E '^username:' "$SECRET_FILE" | head -1 | sed 's/^username:[[:space:]]*//; s/^["'\'']//; s/["'\'']$//')
    NVR_PASS=$(grep -E '^password:' "$SECRET_FILE" | head -1 | sed 's/^password:[[:space:]]*//; s/^["'\'']//; s/["'\'']$//')
    [ -n "$NVR_USER" ] && export WATCHFORGE_NVR_USERNAME="$NVR_USER"
    [ -n "$NVR_PASS" ] && export WATCHFORGE_NVR_PASSWORD="$NVR_PASS"
    echo "nvr-auth načítaný (username=${NVR_USER:-<prázdne>})"
else
    echo "UPOZORNENIE: NVR auth zdroj nenájdený (WATCHFORGE_NVR_AUTH_FILE alebo /run/secrets/nvr-auth) — nastav env WATCHFORGE_NVR_* priamo"
fi

exec dotnet WatchForge.Runner.dll "$@"
