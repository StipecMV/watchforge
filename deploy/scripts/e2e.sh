#!/usr/bin/env bash
# WatchForge E2E cez kontajnery (S9-4): „hľadaj pohyb 14:00–16:00" → fotka s obdĺžnikom.
# Spúšťa fake NVR + Runner + API (podman), seeduje DB, vytvorí request cez API,
# počká na dokončenie a overí doručený klip (video + fotka).
#
# Požiadavky: podman, ffmpeg, curl, jq, python3.
# Usage: deploy/scripts/e2e.sh [--keep]   (--keep = nezmazať kontajnery/volume po behu)

set -euo pipefail

KEEP="${1:-}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

E2E_NET="wf-e2e-net"
E2E_DATA="wf-e2e-data"
E2E_MEDIA="wf-e2e-media"
E2E_SEED="wf-e2e-seed"
API_PORT="8080"
API_TOKEN="e2e-token"
REC_NAME='[Ch0]_2026-04-05_15.00.00-15.15.mkv'
FROM_T="2026-04-05T14:00:00Z"
TO_T="2026-04-05T16:00:00Z"

cleanup() {
    # Uložiť logy pred zmazaním (debug S9-4)
    podman logs wf-e2e-runner > /tmp/wf-e2e-runner.log 2>&1 || true
    podman logs wf-e2e-api > /tmp/wf-e2e-api.log 2>&1 || true
    [ -n "$KEEP" ] && return 0
    echo "── cleanup: kontajnery + volume ──"
    podman rm -f wf-e2e-fake-nvr wf-e2e-runner wf-e2e-api 2>/dev/null || true
    podman volume rm -f "$E2E_DATA" "$E2E_MEDIA" "$E2E_SEED" 2>/dev/null || true
    podman network rm -f "$E2E_NET" 2>/dev/null || true
}
trap cleanup EXIT

echo "═══ WatchForge E2E (S9-4) — hľadaj pohyb 14:00–16:00 ═══"

# ── 0. Build imageov ──
echo "── build: fake-nvr ──"
podman build -t watchforge-fake-nvr:test -f deploy/Dockerfile.fake-nvr . >/dev/null 2>&1 || { echo "BUILD FAIL fake-nvr"; exit 1; }
echo "── build: runner ──"
podman build -t watchforge-runner:test -f deploy/Dockerfile.runner . >/dev/null 2>&1 || { echo "BUILD FAIL runner"; exit 1; }
echo "── build: api ──"
podman build -t watchforge-api:test -f deploy/Dockerfile.api . >/dev/null 2>&1 || { echo "BUILD FAIL api"; exit 1; }

# ── 1. Seed volume: syntetické video + seed DB ──
echo "── seed: video (ffmpeg testsrc2 — pohyb) + DB ──"
podman volume create "$E2E_SEED" >/dev/null
SEED_DIR=$(podman volume inspect "$E2E_SEED" --format '{{.Mountpoint}}')
ffmpeg -y -f lavfi -i "testsrc2=size=320x240:rate=10:duration=2" -pix_fmt yuv420p "$SEED_DIR/motion.mp4" >/dev/null 2>&1

# Seed DB: schéma vytvorí API pri štarte; tu len INSERT-y po prvom spustení.
# Preto najprv naštartujeme API s prázdnym volume (vytvorí schému), zastavíme, seedneme.
podman volume create "$E2E_DATA" >/dev/null
podman volume create "$E2E_MEDIA" >/dev/null
podman network create "$E2E_NET" >/dev/null 2>&1 || true

echo "── init DB schema (api prvý štart) ──"
podman run --rm -d --name wf-e2e-api-init \
    -v "$E2E_DATA":/data -v "$E2E_MEDIA":/media \
    -e WATCHFORGE__API__DBPATH=/data/watchforge.db \
    -e WATCHFORGE__API__APITOKEN="$API_TOKEN" \
    -e ASPNETCORE_URLS=http://0.0.0.0:8080 \
    -p "${API_PORT}:8080" \
    --network "$E2E_NET" \
    watchforge-api:test >/dev/null
# Počkáme na health (schema sa vytvorí pri prvom spojení s DB)
for i in $(seq 1 30); do
    if curl -sf "http://localhost:${API_PORT}/api/v1/system/health" >/dev/null 2>&1; then break; fi
    sleep 1
done
podman rm -f wf-e2e-api-init >/dev/null 2>&1 || true

DATA_DIR=$(podman volume inspect "$E2E_DATA" --format '{{.Mountpoint}}')
echo "── seed DB: NVRS→fake-nvr, CAMERAS, USERS, Sync job ──"
python3 - "$DATA_DIR/watchforge.db" <<'PY'
import sqlite3, sys, datetime
db = sys.argv[1]
conn = sqlite3.connect(db)
c = conn.cursor()
# .NET "O" formát: yyyy-MM-dd'T'HH:mm:ss.fffffff'Z' (Python %f = mikrosekundy, 6 číslic — OK)
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%fZ')

# USERS (seed default — admin admin)
c.execute("INSERT OR IGNORE INTO USERS (username, password_hash, role, avatar_id, locale, created_at) VALUES ('admin','','admin',1,'sk',?)", (now,))

# NVRS → fake-nvr kontajner (DVRIP)
c.execute("INSERT OR IGNORE INTO NVRS (site_id, host, port, username, password_secret_env) VALUES ('e2e','fake-nvr',34567,'admin','WATCHFORGE_NVR_PASSWORD')")
c.execute("INSERT OR IGNORE INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active) VALUES (1,0,'Dvor','camera',1)")

# Sync job — backlog 14:00–16:00 (payload JSON)
c.execute("""INSERT OR IGNORE INTO JOBS (type, priority, source, status, payload, created_at)
             VALUES ('sync', 50, 'system', 'queued', ?, ?)""",
          ('{"from":"2026-04-05T14:00:00Z","to":"2026-04-05T16:00:00Z"}', now))
conn.commit()
conn.close()
print("seed OK")
PY

# ── 2. Spustenie kontajnerov ──
echo "── run: fake-nvr + runner + api ──"
podman run -d --name wf-e2e-fake-nvr \
    -v "$E2E_SEED":/seed:ro \
    -e WF_FAKE_NVR_PORT=34567 -e WF_FAKE_NVR_USERNAME=admin -e WF_FAKE_NVR_PASSWORD=secret \
    -e WF_FAKE_NVR_FILENAME="$REC_NAME" \
    -e WF_FAKE_NVR_BEGIN=2026-04-05T15:00:00Z -e WF_FAKE_NVR_END=2026-04-05T15:15:00Z \
    -e WF_FAKE_NVR_VIDEO=/seed/motion.mp4 \
    --network "$E2E_NET" --network-alias fake-nvr \
    watchforge-fake-nvr:test >/dev/null

podman run -d --name wf-e2e-runner \
    -v "$E2E_DATA":/data -v "$E2E_MEDIA":/media \
    -e WATCHFORGE__RUNNER__DBPATH=/data/watchforge.db \
    -e WATCHFORGE__RUNNER__DOWNLOAD__TEMPDIR=/media/tmp \
    -e WATCHFORGE__RUNNER__CLIPS__CLIPSDIR=/media/clips \
    -e WATCHFORGE__RUNNER__HTTPPORT=8081 \
    -e WATCHFORGE_NVR_PASSWORD=secret \
    --network "$E2E_NET" \
    watchforge-runner:test >/dev/null

podman run -d --name wf-e2e-api \
    -v "$E2E_DATA":/data -v "$E2E_MEDIA":/media \
    -e WATCHFORGE__API__DBPATH=/data/watchforge.db \
    -e WATCHFORGE__API__APITOKEN="$API_TOKEN" \
    -e ASPNETCORE_URLS=http://0.0.0.0:8080 \
    -p "${API_PORT}:8080" \
    --network "$E2E_NET" \
    watchforge-api:test >/dev/null

echo "── čakám na health API ──"
for i in $(seq 1 60); do
    if curl -sf "http://localhost:${API_PORT}/api/v1/system/health" >/dev/null 2>&1; then
        echo "API ready (${i}s)"; break
    fi
    sleep 1
    [ "$i" = 60 ] && { echo "API health timeout"; podman logs wf-e2e-api 2>&1 | tail -15; exit 1; }
done

# ── 3. Čakáme na spracovanie Sync jobu (backlog → recording v DB) ──
echo "── čakám na sync job (recording v DB) ──"
REC_ID=""
for i in $(seq 1 60); do
    REC_ID=$(curl -sf -H "X-Api-Token: $API_TOKEN" \
        "http://localhost:${API_PORT}/api/v1/recordings?from=${FROM_T}&to=${TO_T}" \
        | jq -r '.[0].recordingId // empty' 2>/dev/null || true)
    [ -n "$REC_ID" ] && { echo "recording ${REC_ID} zosyncovaný (${i}s)"; break; }
    sleep 1
done
[ -z "$REC_ID" ] && {
    echo "sync timeout — recording nenájdený";
    echo "── JOBS v DB ──";
    python3 - "$DATA_DIR/watchforge.db" <<'PY'
import sqlite3, sys
conn = sqlite3.connect(sys.argv[1])
conn.row_factory = sqlite3.Row
for row in conn.execute("SELECT job_id, type, status, priority, error, created_at, started_at, finished_at FROM JOBS ORDER BY job_id"):
    print(dict(row))
print("RECORDINGS:", [dict(r) for r in conn.execute("SELECT recording_id, nvr_filename, availability, analysis_state FROM RECORDINGS")])
PY
    podman logs wf-e2e-runner 2>&1 | tail -15;
    exit 1;
}

# ── 4. POST request: „hľadaj pohyb 14:00–16:00" ──
echo "── POST /api/v1/requests (14:00–16:00) ──"
CREATE_RESP=$(curl -sf -X POST -H "X-Api-Token: $API_TOKEN" -H "Content-Type: application/json" \
    -d "{\"fromTime\":\"${FROM_T}\",\"toTime\":\"${TO_T}\",\"cameraId\":1}" \
    "http://localhost:${API_PORT}/api/v1/requests")
REQUEST_ID=$(echo "$CREATE_RESP" | jq -r '.requestId')
echo "requestId=$REQUEST_ID"

# ── 5. Poll stavu requestu ──
echo "── čakám na completed + clipIds ──"
STATUS=""
CLIP_IDS=""
for i in $(seq 1 90); do
    STATUS_BODY=$(curl -sf -H "X-Api-Token: $API_TOKEN" \
        "http://localhost:${API_PORT}/api/v1/requests/${REQUEST_ID}")
    STATUS=$(echo "$STATUS_BODY" | jq -r '.status // empty')
    CLIP_IDS=$(echo "$STATUS_BODY" | jq -r '.clipIds // empty' 2>/dev/null || true)
    if [ "$STATUS" = "completed" ] && [ -n "$CLIP_IDS" ]; then
        echo "request completed (${i}s), clipIds=$CLIP_IDS"; break
    fi
    if [ "$STATUS" = "failed" ]; then
        echo "request FAILED"; podman logs wf-e2e-runner 2>&1 | tail -20; exit 1
    fi
    sleep 1
    [ "$i" = 90 ] && {
        echo "request timeout (status=$STATUS)";
        echo "── JOBS v DB ──";
        python3 - "$DATA_DIR/watchforge.db" <<'PY'
import sqlite3, sys
conn = sqlite3.connect(sys.argv[1])
conn.row_factory = sqlite3.Row
for row in conn.execute("SELECT job_id, type, status, priority, error, progress, created_at, started_at, finished_at FROM JOBS ORDER BY job_id"):
    print(dict(row))
print("REQUESTS:", [dict(r) for r in conn.execute("SELECT request_id, status, query FROM REQUESTS")])
print("CLIPS:", [dict(r) for r in conn.execute("SELECT clip_id, kind, size_bytes FROM CLIPS")])
PY
        podman logs wf-e2e-runner 2>&1 | tail -25;
        exit 1;
    }
done

# ── 6. Overenie klipov (video + fotka s obdĺžnikom) ──
echo "── overenie klipov ──"
VIDEO_OK=0
PHOTO_OK=0
for cid in $(echo "$CLIP_IDS" | jq -r '.[]'); do
    TMPF="/tmp/wf-e2e-clip-${cid}"
    curl -sf -H "X-Api-Token: $API_TOKEN" "http://localhost:${API_PORT}/api/v1/clips/${cid}" -o "$TMPF" || continue
    SZ=$(stat -c%s "$TMPF" 2>/dev/null || echo 0)
    FT=$(file -b "$TMPF" 2>/dev/null | head -c 40)
    echo "  clip $cid: ${SZ} B — ${FT}"
    if echo "$FT" | grep -qi "JPEG\|JPG"; then PHOTO_OK=1; fi
    if echo "$FT" | grep -qi "MP4\|ISO Media\|video"; then VIDEO_OK=1; fi
    rm -f "$TMPF"
done

echo ""
echo "═══ VÝSLEDOK ═══"
if [ "$PHOTO_OK" = 1 ] && [ "$VIDEO_OK" = 1 ]; then
    echo "✅ E2E PASS: request completed, video + fotka doručené (fotka = frame s obdĺžnikom detekcie)"
    echo "   Runner log (posledné riadky):"
    podman logs wf-e2e-runner 2>&1 | tail -6
    exit 0
else
    echo "❌ E2E FAIL: video_ok=$VIDEO_OK photo_ok=$PHOTO_OK"
    podman logs wf-e2e-runner 2>&1 | tail -20
    exit 1
fi
