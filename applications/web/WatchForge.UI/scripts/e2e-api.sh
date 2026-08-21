#!/usr/bin/env bash
# WatchForge UI E2E — spustí API s čerstvou seeded DB (login heslo1234).
# Volá sa z playwright.config.ts (webServer). Vyžaduje: dotnet build riešenia.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../../../.." && pwd)"
DB="${TMPDIR:-/tmp}/watchforge-e2e-api.db"

# Seed DB cez e2e-seed nástroj (default users + NVR + kamery)
dotnet run --project "$REPO/tools/e2e-seed/e2e-seed.csproj" --configuration Release -- "$DB" 2>/dev/null

# Spusti API s DB (WatchForge__Api__DbPath) na porte 5000 (UI API_BASE_URL).
# --no-launch-profile: launchSettings.json (5158) by prebil ASPNETCORE_URLS.
ASPNETCORE_URLS="http://localhost:5000" \
WATCHFORGE__API__DBPATH="$DB" \
WATCHFORGE__API__APITOKEN="e2e-token" \
  dotnet run --project "$REPO/applications/server/WatchForge.Api" --no-build --no-launch-profile --configuration Release 2>/dev/null &
API_PID=$!

cleanup() { kill "$API_PID" 2>/dev/null || true; }
trap cleanup EXIT

for i in $(seq 1 90); do
  # /system/info je admin endpoint (401 bez tokenu) — readiness cez verejný /system/health
  if curl -sf http://localhost:5000/api/v1/system/health >/dev/null 2>&1; then
    echo "API ready (DB: $DB)"
    wait "$API_PID"  # drží proces nažive, kým Playwright neskončí
  fi
  sleep 1
done
echo "API sa nenaštartoval" >&2
exit 1
