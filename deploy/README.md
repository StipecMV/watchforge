# WatchForge — Deployment Guide & Runbook (S9, aktualizované 2026-08-15)

> **DÔLEŽITÉ (2026-08-12+): produkcia na lokálny host beží ako systemd user služby
> (nie kontajnery).** Dockerfile/compose/Quadlet zostávajú ako build variant a pre
> E2E s fake NVR. Nižšie je najprv reálny produkčný stav, potom compose/Quadlet
> variant pre čisté nasadenie.

## 0. Produkčný stav (lokálny host, LIVE od 2026-08-12)

| Služba | Image/Binárka | Port | Úloha |
|---|---|---|---|
| `watchforge-api.service` | `WatchForge.Api` (dotnet) | **5000** | REST `/api/v1/*` (verejné read + admin write, session cookie + `X-Api-Token`) |
| `watchforge-runner.service` | `WatchForge.Runner` (dotnet) | **8081** | worker loop: sync, download, analyze, clip_extract, purge |
| `watchforge-web.service` | python static server (build Angular) | **4200** | Angular SPA + proxy `/api` → api:5000 |
| `watchforge-mcp.service` | `WatchForge.Mcp` (dotnet) | **8770** (127.0.0.1) | MCP streamable HTTP pre agentov (Hermes `mcp_servers.watchforge`) |

- Zdieľané volume/cesty: SQLite DB (WAL) + media cache (`~/watchforge-media/`), zone-bg fotky (`~/watchforge-media/zone-bg/`).
- Secrets: `WATCHFORGE_NVR_AUTH_FILE` (env v unitoch) alebo Podman secret `nvr-auth`.
- Prístup: **Tailscale serve** `https://<host>.<tailnet>.ts.net:<port>` (UI) — žiadne verejné porty.
- Backup: `watchforge-db-backup.timer` (denne 04:00, VACUUM INTO + WAL checkpoint TRUNCATE, rotácia 14 dní).

```bash
# Stav služieb
systemctl --user status watchforge-api watchforge-runner watchforge-web watchforge-mcp
journalctl --user -u watchforge-runner -n 50
```

---

## 1. Build imageov

```bash
cd <repo>
podman build -t watchforge-runner:latest -f deploy/Dockerfile.runner .
podman build -t watchforge-api:latest    -f deploy/Dockerfile.api .
podman build -t watchforge-ui:latest     -f deploy/Dockerfile.ui .
podman build -t watchforge-mcp:latest    -f deploy/Dockerfile.mcp .
```

> **Poznámka CPU-only OpenCV**: `Dockerfile.runner` publikuje s `-p:WatchForgeCpuOnly=true` —
> CUDA build `libOpenCvSharpExtern.so` vyžaduje `libcuda.so.1` (CUDA runtime libs), ktoré v kontajneri
> nie sú, a Quadro M2200 (Maxwell SM 5.0) aj tak nepodporuje `sm_100` cubiny NuGet runtime 1.0.8.
> CPU fallback (Farneback + HOG) je plnohodnotný (~27 ms/frame @1080p). GPU by fungoval len na stroji
> s Turing+ GPU (SM 7.5+) — vtedy odstráň `WatchForgeCpuOnly=true` z Dockerfile.runner.

## 2. Secrets

```bash
# Možnosť A — Podman secret (odporúčané)
cp deploy/secrets/nvr-auth.yaml.example deploy/secrets/nvr-auth.yaml   # vyplň username/password
podman secret create nvr-auth deploy/secrets/nvr-auth.yaml

# Možnosť B — host mount súboru (Quadlet)
#   Environment=WATCHFORGE_NVR_AUTH_FILE=/etc/watchforge/nvr-auth.yaml
#   Mount=type=bind,source=/etc/watchforge/nvr-auth.yaml,destination=/etc/watchforge/nvr-auth.yaml,ro
```

Runner entrypoint (`deploy/scripts/entrypoint-runner.sh`) prečíta YAML (`username:`/`password:`) a
exportuje `WATCHFORGE_NVR_USERNAME`/`WATCHFORGE_NVR_PASSWORD`. DB stĺpec `NVRS.password_secret_env`
musí ukazovať na `WATCHFORGE_NVR_PASSWORD` (seed SQL).

## 3. Spustenie

### Možnosť A — produkcia (systemd user služby, aktuálny stav na lokálny host)

Služby `watchforge-api/runner/web/mcp` bežia z build artefaktov (`dotnet run`/`python http.server`).
Reštart stacku:

```bash
systemctl --user restart watchforge-api watchforge-runner watchforge-web watchforge-mcp
```

> Východiskové porty compose/Quadlet variantu sú web `:80`, api `:8080`, mcp `:18082` —
> pri produkčnom nasadení cez systemd služby sa používajú porty 5000/8081/4200/8770
> (pozri §0).

### Možnosť B — compose (jednoduché, build variant)

```bash
podman-compose -f deploy/compose.yaml up -d --build
podman-compose -f deploy/compose.yaml ps
```

### Možnosť C — Quadlet / systemd (kontajnerový variant, autoštart)

```bash
mkdir -p ~/.config/containers/systemd
cp deploy/quadlet/* ~/.config/containers/systemd/
systemctl --user daemon-reload
systemctl --user enable --now watchforge-runner watchforge-api watchforge-web watchforge-mcp
# Backup timer
systemctl --user enable --now watchforge-db-backup.timer
```

Uprav `deploy/quadlet/watchforge-runner.container` (mount NVR auth) a
`deploy/quadlet/watchforge-db-backup.service` (cesty k DB vo volume) pred inštaláciou.

## 4. SQLite backup (S9-3)

- Skript: `deploy/scripts/backup-db.py <db> <backup_dir> [retention_days]`
- Robí: WAL checkpoint (TRUNCATE) → VACUUM INTO (atomická kópia) → rotácia starých záloh.
- Timer: `watchforge-db-backup.timer` (denne 04:00, Persistent=true — dobehne po vypnutí).
- Manuálne: `python3 deploy/scripts/backup-db.py /data/watchforge.db /data/backups 14`

## 5. E2E overenie (bez reálneho NVR)

```bash
bash deploy/scripts/e2e.sh
```

Spustí fake NVR + Runner + API kontajnery, seedne DB (NVRS→fake-nvr, kamera, users, sync job),
vytvorí request „hľadaj pohyb 14:00–16:00", počká na dokončenie a overí doručené klipy
(video MP4 + fotka JPEG s obdĺžnikom detekcie). `--keep` zachová kontajnery/volume.

---

## 📖 Runbook

### Zdravie a stav

```bash
# API health + info (produkcia: port 5000; compose variant: 8080)
curl -s http://localhost:5000/api/v1/system/health
curl -s -H "X-Api-Token: $API_TOKEN" http://localhost:5000/api/v1/system/info

# Runner interné endpointy
curl -s http://localhost:8081/internal/health
curl -s http://localhost:8081/internal/jobs
curl -s http://localhost:8081/internal/jobs/<id>

# Logy (produkcia: systemd; compose: podman logs)
journalctl --user -u watchforge-runner -n 100
podman logs -f watchforge-runner
```

### Joby — čo znamená stav

`queued → running → completed | failed | interrupted`; `failed → queued` (retry), `interrupted → queued` (reštart).

- `sync` — backlog z NVR (nové nahrávky), missed_during_outage, availability (default 24 h, payload `{"from","to"}`).
- `download` — DVRIP stiahnutie do media cache (`.downloading` cleanup).
- `analyze` — motion + person detekcia → DETECTIONS; lokálne video sa po analýze maže (NVR = archivár).
- `clip_extract` — ffmpeg cut + fotka s obdĺžnikom → CLIPS (beží v analysis slotoch Runnera).
- `purge` — retention (lokálne video hneď, DB po mesiaci, persist výnimka).

### Typické operácie

```bash
# Stav požiadavky („hľadaj pohyb 14:00–16:00")
curl -s -H "X-Api-Token: $API_TOKEN" http://localhost:5000/api/v1/requests/<id> | jq

# Stiahnuť klip (video/fotka)
curl -s -H "X-Api-Token: $API_TOKEN" http://localhost:5000/api/v1/clips/<id> -o clip.mp4

# Live snapshot kamery (verejné, bez tokenu)
curl -s http://localhost:5000/api/v1/live/1/frame -o frame.jpg

# Reštart celého stacku (produkcia = systemd user služby)
systemctl --user restart watchforge-runner watchforge-api watchforge-web watchforge-mcp

# Recovery po reštarte je automatický (running → interrupted → queued)
```

### Riešenie problémov

| Symptóm | Príčina / riešenie |
|---|---|
| `Name or service not known` v sync jobe | Runner nevidí NVR — skontroluj `NVRS.host` v DB (compose: `fake-nvr` alias; produkcia: IP NVR) |
| Analyze job `Failed` | Skontroluj `job.error` v DB; bežné: chýbajúce heslo env (`password_secret_env`), ffmpeg chyba na neštandardnom videu |
| `SqliteTransaction has completed` | Už opravené (S9-4): repos otvárajú spojenie per operáciu; neupravuj späť na zdieľaný connection |
| ClipExtract navždy `queued` | Už opravené (S9-4): RunnerService claimuje Analyze aj ClipExtract v analysis slotoch |
| OpenCV `DllNotFoundException` v kontajneri | CPU-only build; GTK deps (libgtk-3-0, libatk, libatomic) sú v Dockerfile.runner |
| Fotka bez obdĺžnika | Detekcia nenašla región (statické video) — skontroluj detections v DB |
| DB zamknutá (backup) | VACUUM INTO funguje za behu; `busy_timeout=5000` je nastavené per spojenie |

### Upgrade / rollback

```bash
podman build -t watchforge-runner:new -f deploy/Dockerfile.runner .   # nový image
podman tag watchforge-runner:new watchforge-runner:latest            # alebo uprav Quadlet Image=
systemctl --user restart watchforge-runner                            # (Quadlet)
# Rollback: podman tag <starý> watchforge-runner:latest && restart
```

DB je verziovaná (`PRAGMA user_version`, schema idempotentná) — downgrade DB sa nerobí automaticky.

---

## Env vars (prehľad)

| Premenná | Kontajner | Default | Popis |
|---|---|---|---|
| `WATCHFORGE__RUNNER__DBPATH` | runner | `watchforge.db` | SQLite cesta |
| `WATCHFORGE__RUNNER__DOWNLOAD__TEMPDIR` | runner | `/tmp/watchforge-media` | media cache (produkcia: `~/watchforge-media/`) |
| `WATCHFORGE__RUNNER__CLIPS__CLIPSDIR` | runner | `/tmp/watchforge-clips` | clips |
| `WATCHFORGE__RUNNER__HTTPPORT` | runner | `8081` | interný HTTP |
| `WATCHFORGE__RUNNER__POLLINTERVALMS` | runner | `1000` | poll interval |
| `WATCHFORGE__API__DBPATH` | api | `watchforge.db` | SQLite cesta (zdieľaná) |
| `WATCHFORGE__API__APITOKEN` | api | prázdny | `X-Api-Token` pre agenta |
| `WATCHFORGE__API__CORS__ALLOWEDORIGINS__*` | api | localhost | CORS originy web UI |
| `WATCHFORGE_API_URL` | mcp | `http://localhost:5000` | base URL API (produkcia: `http://localhost:5000`) |
| `WATCHFORGE_API_TOKEN` | mcp | prázdny | token pre MCP nástroje |
| `WATCHFORGE_NVR_AUTH_FILE` | runner | — | YAML súbor NVR auth (Quadlet/systemd) |
| `WF_FAKE_NVR_*` | fake-nvr (E2E) | — | fake NVR konfigurácia |
