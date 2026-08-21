# AGENTS.md — WatchForge

> Návod pre AI agentov pracujúcich v tomto repozitári (vrátane autonómnych behov a cron agentov).
> Tento súbor sa automaticky vkladá do kontextu agenta (Project context) — čítaj ho PRED akoukoľvek prácou.

## 1. Čo je tento projekt

**WatchForge** je self-hosted video processing pre NVR (Network Video Recorder) systém — vylepšuje pixel-precision detekciu pohybu nad Movols/Xiongmai NVR (DVRIP protokol na porte 34567). Deteguje pohyb, osoby a objekty, generuje klipy a fotky s obdĺžnikmi detekcie, doručuje cez REST API, MCP server (pre agentov, napr. Hermes/Richtár) a web UI.

- **Stack:** .NET 10 (ASP.NET Core, TUnit + Moq, OpenCvSharp4, Microsoft.Data.Sqlite WAL, ModelContextProtocol), Angular 19 + Bun (vitest), nginx, Podman 4.x rootless + Quadlet/systemd
- **Repo:** `github.com/StipecMV/watchforge` — **pracovná vetva `watchforge-redesign`**
- **Riešenie:** `WatchForge.slnx` + `Directory.Packages.props` (CPM — centralizované NuGet verzie)
- **Deployment:** lokálny host, NVR `<nvr-ip>:34567` (Xiongmai; NVR nemá statickú IP — DHCP, rieši S13 discovery)
- **Dokumentácia:** `README.md`, `docs/architecture.md` (vrátane vyriešených rozhodnutí §10), `docs/requirements.md` (FR-01..FR-16, NFR), `docs/kanban.md` (zdroj pravdy pre stav!), `docs/gpu-analysis.md`, `docs/cpu-benchmark.md`, `docs/real-benchmark.md`, `docs/parallel-download-benchmark.md`, `docs/s18-network-analysis.md`, `docs/hw-inventory.md`, `deploy/README.md`

## 2. GIT PRAVIDLÁ (KRITICKÉ — autonómny beh)

- **Agenti commitujú a pushujú VÝHRADNE na `watchforge-redesign`, NIKDY na `main`.**
- Merge do `main` robí stakeholder + Richtár po návrate/revízii.
- Každá úloha = malý funkčný celok; po každej úlohe commit (`git add && commit && push`).
- **TDD povinné:** test najprv (RED→GREEN→REFACTOR).
- **BLOCKED handling:** ak chýba závislosť/nástroj a nedá sa rozumne vyriešiť → úlohu označ `BLOCKED` s dôvodom v kanbane a preskoč. **NIE workaroundy, NIE pálenie tokenov.**
- Commit message štýl: `sprintXX: SXX-Y popis` (napr. `sprint14: S14-2 CI E2E job ...`).
- **`deploy/secrets/` je GITIGNORED** — NIKDY necommitovať NVR prihlasovacie údaje; v repo je len `nvr-auth.yaml.example`.

## 3. Build a testy

```bash
# Build — 0 chýb (warningy: TUnit0018 v Mcp integračných testoch sú známe, neblokujúce)
dotnet build WatchForge.slnx

# .NET testy — TUnit: POUŽI dotnet run --project, NIE dotnet test
for p in tests/libraries/WatchForge.Processing.Library.Tests \
         tests/services/WatchForge.Runner.Tests \
         tests/libraries/WatchForge.DVRIP.Library.Tests \
         tests/libraries/WatchForge.MotionSentinel.Library.Tests \
         tests/libraries/WatchForge.Interfaces.Library.Tests \
         tests/libraries/WatchForge.Contracts.Library.Tests \
         tests/applications/server/WatchForge.Api.Tests \
         tests/tools/WatchForge.Mcp.Tests; do
  dotnet run --project $p
done

# Web UI testy (Bun toolchain)
cd applications/web/WatchForge.UI && bun run test

# E2E s fake NVR (bez reálneho NVR) — POVINNÉ pred pushom infra zmien
bash deploy/scripts/e2e.sh
# → E2E PASS: request completed, video + fotka doručené

# E2E s reálnym NVR — GATED, nikdy automaticky
WATCHFORGE_REAL_NVR=1 ...
```

**Aktuálny stav (2026-08-15):** 325 .NET testov + 189 UI testov + 15 E2E (Playwright), Release build 0/0. Kanban: S1–S21 + S22a–S22j kompletné (pozri `docs/kanban.md` — je zdroj pravdy). Produkcia LIVE na lokálnom hoste (systemd user služby, NVR, 8 kamier).

## 4. Architektúra (4 komponenty; produkcia = systemd user služby, kontajnery = build variant)

```text
Runner (komponent 1) → DVRIP klient + OpenCV pipeline (Farneback motion, HOG person, YuNet+SFace face)
                        + job orchestrácia, /internal/* HTTP, CPU-only (M2200 nepodporuje GPU cubiny)
Api (komponent 2)    → ASP.NET Core MVC, /api/v1/*, verejné read + admin write (S22c),
                        session cookie + X-Api-Token, RFC 9457 ProblemDetails, CORS pre web UI
Web UI (komponent 3) → Angular SPA (SK/EN), PWA, python static server + proxy /api → api
MCP (komponent 4)    → streamable HTTP server mapujúci REST API (search_motion, get_clip,
                        trigger_processing, get_request_status, search_recordings)
SQLite (WAL)         → shared volume watchforge-data; JOBS tabuľka = job front (NIE message broker)
NVR <nvr-ip>:34567 ← Runner (DVRIP TCP)
```

**Produkcia (2026-08-12+):** systemd user služby `watchforge-{api,runner,web,mcp}.service`
(api :5000, runner :8081, web :4200, mcp :8770), Tailscale serve :19200. Kontajnery
(Dockerfile/compose/Quadlet) sú build variant + E2E s fake NVR. Detail: `deploy/README.md` §0.

- **Job orchestration cez SQLite tabuľku** (KISS), nie message broker. Priorita: whatsapp=100, telegram=90, webui=80, system=50, background=10. Semafory `MaxParallelAnalyses` + `MaxParallelDownloads` (default 4+4, rozsah 1–4). Preempcia: prioritný job preruší najnižšie-prioritný bežiaci; Sync/Purge sa neprerušujú (S22e).
- **Detekčný pipeline:** motion (Farneback dense optical flow, 1080p; S22g: 720p+1fps) → person (HOG+SVM, len v regiónoch pohybu) → face (YuNet+SFace ONNX, 4K crop). Regióny normalizované 0..1 (platné pre 4K).
- **SQLite:** repos si otvárajú spojenie **per operáciu** (`OpenConnectionStringAsync`) — Microsoft.Data.Sqlite connection NIE je thread-safe (E2E bug S9-4); WAL/PRAGMA sa nastavujú len pri vytvorení DB (PRAGMA journal_mode=WAL pri každom otvorení = „database is locked").
- **Runner claimuje Analyze ALEBO ClipExtract joby** (E2E bug S9-4 — ClipExtract ostával navždy queued).
- Štruktúra: `libraries/` (DVRIP, MotionSentinel, Processing, Interfaces, Contracts), `applications/` (services/WatchForge.Runner, server/WatchForge.Api, web/WatchForge.UI, tools/WatchForge.Mcp + WatchForge.FakeNvr.Host), `tests/`, `deploy/`, `tools/` (nvr-bench, nvr-check, real-bench, cpu-benchmark, e2e-seed). Legacy `apps/services` (starý DVRIP service + MotionSentinel) boli nahradené redizajnom.

## 5. SCHVÁLENÉ rozhodnutia (potvrdené stakeholderom)

| Rozhodnutie | Stav |
|---|---|
| **MCP = samostatný kontajner (4)** — `/mcp` endpoint v API sa NEPOUŽÍVA | ✅ potvrdené stakeholderom |
| **Auth:** prihlasovanie LEN pre web UI (S22c: verejné UI, admin-only write); username fixný v DB (admin + rodinní používatelia); prázdny `password_hash` = výzva na nastavenie hesla; reset hesla = vymazanie hashu v DB; web UI = session cookie; **agent (MCP→API) = API token v secrets, žiadny JWT pre agenta** | ✅ potvrdené |
| **Fake NVR test server** (WatchForge.Testing.FakeNvr) — testovať bez reálneho NVR; E2E s fake NVR vždy, s reálnym NVR gated `WATCHFORGE_REAL_NVR=1` | ✅ potvrdené |
| **Downscale: fixný 1080p** pre motion + person (4× redukcia z 4K); regióny sa mapujú na 4K | ✅ potvrdené |
| **Face recognition = 4K crop z originálneho framu** (nie 1080p downscale) — embeddings potrebujú detail | ✅ potvrdené |
| **Face vrstva = klasická CV (LBP cascade + LBPH), NIE ML model** (konzistentné s FR-04 „programatically without AI model"); `IFaceRecognizer` navrhnutý tak, že ML model (DeepFace/ArcFace) sa dá neskôr podsunúť bez zmeny volajúceho | ✅ S8-3 |
| **GPU:** `OpenCvSharp4.Cuda.runtime.linux-x64` (Combined) obsahuje LEN sm_100 (Blackwell) cubiny → **Quadro M2200 (Maxwell SM 5.0) NIE je podporovaný**; produkčná cesta = **CPU-only build** (`-p:WatchForgeCpuOnly=true` v Dockerfile.runner); GPU cesta gated na Turing+ alebo vlastný OpenCV build (vyžaduje nvcc, ktorý nie je nainštalovaný) | ✅ S8-1/S9-1 |
| **YAGNI:** multi-site sa NEimplementuje — len `nvr_id`/`site_id` stĺpce pripravené | ✅ |
| **NVR discovery (S13):** NVR nemá statickú IP (DHCP) → auto-sken LAN na port 34567 + login overenie + auto-update host v DB | ✅ S13-1 |
| **Backup:** denný job `VACUUM INTO` + WAL checkpoint TRUNCATE (04:00, Persistent=true), rotácia 14 dní (`deploy/scripts/backup-db.py`) | ✅ S9-3 |
| **Retention:** purge job — lokálne video hneď, DB záznam po mesiaci, persist výnimka, clips expiry (2–3 h) | ✅ S4-5 |
| **Testovacia stratégia:** TDD; mocks len na hraniciach (network, filesystem); pipeline logika na reálnych dátach; gated testy NIKDY v default suite | ✅ |
| **Kanban = zdroj pravdy** pre stav implementácie; každá úloha sa odškrtáva a report rozhodnutí sa píše na konci behu | ✅ |

## 6. ZAMIETNUTÉ rozhodnutia (NEVRAĆAJ bez diskusie so stakeholderom)

- **`/mcp` endpoint v API kontajneri** — ZAMIETNUTÉ (stakeholder chce samostatný kontajner; KISS návrh „najprv /mcp v API" bol prehlasovaný).
- **JWT pre agenta** — ZAMIETNUTÉ (agent = API token v secrets; JWT len komplikuje).
- **Multi-site funkcionalita** — ZAMIETNUTÉ (YAGNI; len stĺpce do budúcna).
- **ML model pre face recognition v prvej etape** — ZAMIETNUTÉ (LBPH klasická CV stačí; ML až keď na to príde kanban úloha).
- **Message broker pre job orchestration** — ZAMIETNUTÉ (SQLite tabuľka, KISS).
- **720p downscale** — ZAMIETNUTÉ (9× redukcia plochy, nedostatočná spoľahlivosť analýzy).
- **GPU ako produkčná cesta na M2200** — ZAMIETNUTÉ (sm_100 mismatch + chýbajúci CUDA runtime libs na hoste; CPU-only je produkcia).
- **Legacy monolit** (staré `apps/services` DVRIP + MotionSentinel + staré `/api/videos`) — nahradené 4-kontajnerovou architektúrou (S5-1: legacy `/api/videos` endpoint nahradený).
- **Workaroundy na BLOCKED úlohách** — zakázané pravidlami autonómneho behu (znač BLOCKED a preskoč).
- **Deploy do produkcie bez overenia** — E2E/fake NVR je minimálna brána; reálne nasadenie (Quadlet + secrets + prvý beh proti NVR) vyžaduje schválenie stakeholdera.

## 7. Známé muchy a pitfalls

- `dotnet test` nefunguje spoľahlivo pre TUnit projekty — vždy `dotnet run --project`.
- **SQLite:** nikdy nezdieľaj jeden `SqliteConnection` medzi vláknami; WAL pragma len pri create DB.
- **Runner:** analysis slot musí claimovať Analyze **aj** ClipExtract (inak sa klipy negenerujú).
- **OpenCV CUDA:** `IsCudaAvailable()` musí chytať aj `EntryPointNotFoundException`/`BadImageFormatException` (bez CUDA runtime) → CPU fallback.
- **Kontajnerový build:** CPU-only OpenCV; GTK3/atk/atomic libs v runtime image (OpenCV highgui linkovacia závislosť aj bez GUI); NU1510 — redundantné PackageReference odstrániť (Hosting/Configuration.Binder prichádza cez FrameworkReference).
- **Fake NVR v kontajneri:** `IPAddress.Any` + fixný port + `--network-alias fake-nvr`.
- **CPU benchmark:** baseline ukázal bottleneck = HOG ~280–550 ms/frame (720p/1080p/1440p); „27 ms" z S8-1 bolo len 320×240 — pozri `docs/cpu-benchmark.md` a `docs/gpu-analysis.md` pred tvrdeniami o výkone.
- **NVR IP:** aktuálne overené `<nvr-ip>` (login OK), NIE iná IP (No route to host) — overené reálnym DVRIP loginom 2026-08-08.
- **Secrets:** NIKDY nespomínať/necommitovať skutočné NVR heslo; len `nvr-auth.yaml.example` alebo env var `WATCHFORGE_NVR_AUTH_FILE`/Podman secret `nvr-auth`.

## 8. Pravidlá práce

1. **Pred zmenou:** prečítaj `docs/kanban.md` (stav), `docs/architecture.md` (dizajn) a `docs/requirements.md` (FR-01..FR-16, NFR). FR → komponent → test mapovanie je v architecture §9.
2. **Autonómny beh:** pracuj sprint po sprinte (S10-..., S11-..., ...), odškrtávaj kanban, na konci report rozhodnutí pre stakeholdera.
3. **Po zmene:** build 0 chýb + dotknuté test projekty + (`bun run test` pri UI) + E2E pri infra zmenách.
4. **Gated testy** (reálny NVR, GPU benchmark) nikdy nespúšťaj v default suite.
5. **Zmeny rozhodnutí** (nové schválené/zamietnuté) zapisuj do tohto súboru (sekcie 5/6) aj do `docs/kanban.md` reportu.
