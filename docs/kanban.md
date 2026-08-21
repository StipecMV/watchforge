# WatchForge — Kanban board (autonómny beh)

> Zdroj pravdy pre stav implementácie počas autonómneho behu.
> Vetva: `watchforge-redesign` (agenti commitujú a pushujú SEM, nie na main; merge do main spraví stakeholder + Richtár po návrate).
> Pravidlá autonómneho behu:
> - Agenti commitujú a pushujú na `watchforge-redesign` (žiadny push na main).
> - **Blocked handling**: ak chýba závislosť/nástroj a nedá sa rozumne vyriešiť, úloha sa označí `BLOCKED` s dôvodom a preskočí sa — NIE workaroundy, NIE pálenie tokenov.
> - Každá úloha = malý funkčný celok (sprint štýl); po každej úlohe commit.
> - TDD: test najprv (RED→GREEN→REFACTOR).
> - Na konci behu: report rozhodnutí pre stakeholdera.

## Kolóny
`NOT STARTED` → `IN PROGRESS` → `DONE` · `BLOCKED` · `WAITING`

---

## 📋 PREHĽAD PORADIA ŠPRINTOV (2026-08-10, čisté číslovanie)

| # | Sprint | Obsah | Stav |
|---|---|---|---|
| 1 | S1 | Foundation (spike, fake NVR, štruktúra) | ✅ |
| 2 | S2 | Processing.Library (SQLite + jobs) | ✅ |
| 3 | S3 | DVRIP refactor (protokol + testy) | ✅ |
| 4 | S4 | Runner (worker, download, analyze, purge, E2E) | ✅ |
| 5 | S5 | REST API (auth, kamery, requests, clips) | ✅ |
| 6 | S6 | Web UI (login, dashboard, settings, témy) | ✅ |
| 7 | S7 | MCP server + skill | ✅ |
| 8 | S8 | GPU + person/face detection (klasická CV) | ✅ |
| 9 | S9 | Deployment (compose, Quadlet, backup, E2E) | ✅ |
| 10 | S10 | FR-05 face perzistencia + Identity API/UI | ✅ |
| 11 | S11 | Web UI E2E (Playwright) | ✅ |
| 12 | S12 | system/status + GPU decision doc | ✅ |
| 13 | S13 | Benchmark na reálnych dátach (reálny NVR) | ✅ |
| 14 | S14 | CI pipeline (GitHub Actions, 0/0) | ✅ |
| 15 | S15 | Meranie paralelného downloadu | ✅ |
| 16 | S16 | Paralelné downloady+analýzy produkčne (4/4) | ✅ |
| 17 | S17 | Face ML (YuNet+SFace) | ✅ |
| 18 | S18 | Preempcia FR-08 + cache test | ✅ |
| 19 | S19 | DVRIP live view v UI (1/4/8 + Analýzy) | ✅ 2026-08-12 |
| 20 | S20 | Export selekcie cez UI (dôkazový klip) | ✅ 2026-08-12 |
| 21 | S21 | **Produkčné nasadenie + test nasadenia** | ✅ 2026-08-12 |
| 21b | S21b | Mobilná responzivita UI (StoryboardMobile) + live frame cache (iOS Safari) | ✅ 2026-08-12 |
| 22 | S22 | Druhá NVR jednotka (2 WiFi kamery) | 💡 **IDEA** (možno ani nekúpi) |
| 22a | S22a | **Auto-plánovač analýz** (S22-1): Runner periodicky (30 s) enqueue Sync + Analyze pre záznamy čakajúce na analýzu (background priorita, dedup) — video/klip len na trigger | ✅ 2026-08-13 |
| 22b | S22b | **Live view režimy podľa siete**: default view 8 (1 fps/kamera), view 4 = 5 fps + stránkovanie (CH1–4 / CH5–8), view 1 = MJPEG stream s fps fallbackom (15→10→5→3→1); MJPEG endpoint `?fps=N` | ✅ 2026-08-14 |
| 22c | S22c | **Verejné UI + admin-only settings**: live view default (bez prihlásenia), analýzy + štatistiky analyz verejné; admin login cez `#/admin`; rodinné účty vymazané (len admin); názvy kamier podľa vision analýzy; view 4=3 fps, view 1=10 fps fixne, loading pri prepínaní, mobil 1/2/8 vs desktop 1/4/8 | ✅ 2026-08-14 |
| 22d | S22d | **Live view 1/8 + admin fixy**: len režimy 1/8 (view 2/4 odstránené), dvojklik/tap prepína 1↔8; view 1 = 15 fps; ⚙ Nastavenia v sidebare pre admina (boli schované s analýzami); CH9 aj zo Settings manažmentu; Worker+Sync detaily pre všetkých v stats-bare; názvy s diakritikou (Psia buda (Ares), Chodník (z hora), Záhrada pri fontánke, Hnojisko, Chodník (priamo)) | ✅ 2026-08-14 |
| 22e | S22e | **Sync/retention fix**: duplicitné ⚙ z analýz odstránené; Sync max 1×/hod (AutoPlanner throttle — nezahlcuje NVR); Sync/Purge vlastný maintenance slot (nezávislý od analyze); preempcia neprerušuje Sync/Purge; Purge job sa konečne vytvára (retention 30 dní predtým nikdy nebežal — DB rástla o záznamy, ktoré NVR už nemá); overené: sync completed (unavailable=448), purge completed (clips=881) | ✅ 2026-08-14 |
| 22f | S22f | **Live stream úspora**: IsIdle 300 s → 20 s, cleanup timer 30 s → 10 s (stream zmizne do ~30 s po odchode diváka, predtým 5 min); **fix: ffmpeg proces sa nezabíjal** (len CTS) — visiace ffmpeg žrali CPU + NVR upload; Kill(entireProcessTree) v finally; overené reálne: 1 ffmpeg → 35 s bez requestov → 0 ffmpeg; API 80/80 (+5 testov) | ✅ 2026-08-14 |
| 22g | S22g | **Balík 25 nálezov**: analyze zrýchlené ~4–5× (720p+1fps, CPU-saturácia); Zóny „Pridať zónu" fix; Detekcia vizuály+popisky+switch shared/per-kamera; ikonky kamier odstránené; FR/NFR/sprint texty preč; PWA (manifest+SW+logo); title WatchForge; SVG logo; Analýzy button pod Live; admin tagy preč; dashboard button zo settings preč; dátumy SVK+24h; scroll analýz fix; 985 zbytočných analyze jobov zrušených (availability filter); view 1 fit+20fps; services detaily+verzie; sidebar 264px; stats-bar téma; Kamery panel preč; auto-výber kamery fix (rešpektuje user výber); hláska „analýzy ešte neprebehli" | ✅ 2026-08-14 |
| 22h | S22h | **MCP do Hermes config**: watchforge-mcp systemd služba (127.0.0.1:8770, streamable HTTP), mcp_servers.watchforge v Hermes config.yaml, API token (X-Api-Token) pre agent write operácie; nástroje: search_motion, get_clip, trigger_processing, get_request_status, search_recordings (prefix mcp_watchforge_*) | ✅ 2026-08-14 |
| 22i | S22i | **Live audio výskum (definitívne)**: NVR live monitor audio NEPOSIELA — Extra aj Main stream = len HEVC video NAL (0 audio paketov), AudioEnable=true claim NVR prijíma ale audio nepošle, OPPlayBack Start (Live/ByName/ByTime) claim Ret 100 ale 0 dát, RTSP 401 (zakázaný). **Download stream audio MÁ** (G711A alaw 8 kHz, 6.75 % súboru — nahrávky na NVR audio majú, náš convert `-f hevc` ho stráca), ale streamuje celý segment = 15 min oneskorenie (nie live). **Záver: live zvuk na tomto NVR nie je možný** (hardvérové/firmvérové obmedzenie). Vedľajšie fixy: **DVRIP logout (msg 1001)** v Dispose aj ReconnectAsync (analyze joby zaplnili NVR sessiony → Ret 205), MonitorPlaybackLiveAsync metóda, MCP DI konštruktor | ✅ 2026-08-14 |
| 22k | S22k | **Čistenie + prepínač analýz**: (1) cron 3b2fd1810ed6 odstránený; (2) fotky zone-bg = súčasť web UI (public/zone-bg/, servíruje web server — API endpoint odstránený); (3) **analysesEnabled env prepínač** (WatchForge__Api__AnalysesEnabled + WatchForge__Runner__AnalysesEnabled) — UI skryje Analýzy (sidebar/mobil/stats), runner nevytvára analyze joby; **nastavené na true** (nová logika okien aktívna); (4) **DB vyčistená** (2.78M detekcií, 2022 recordings, 1928 jobov, 116 requestov preč; ostal len admin účet; 397 MB → 112 KB po VACUUM) | ✅ 2026-08-15 |
| 22l | S22l | **Časové okná (nová logika)**: okno = presný 15-min interval (kvartál, concat segmentov); retencia 4 okná = 60 min/kamera (env `WindowMinutes`/`RetentionWindows`/`SyncIntervalMinutes`); sync 15 min (rozsah 75 min, len okná); analyze = najnovšie AKTÍVNE okno naprieč kamerami (priorita vek okna, staršie sa NEdorábajú); **person_pending** skip pre cleanup (detekcia osoby → chránené, UI button „Osoba skontrolovaná" + MCP person_reviewed/delete_recording); **trigger_processing = odmietnutie** (analyzed_windows, search len analyzované okná); video po analýze ostáva (rolling); purge maže okná (FK: clipy+joby pred recordings); DB migrácia user_version 2 | ✅ 2026-08-15 |

**S1–S21 + S22a–S22j hotové (325 .NET + 189 UI + 15 E2E testov, build 0/0) · S22 idea (druhá NVR — nie isté, či bude).**
**Mobilná responzivita (S21b, 2026-08-12):** hamburger drawer + bottom nav, dashboard
panely stacked, live mriežka 2×2, settings nav → horizontálny scroll; live stream pre iOS
cez `GET /api/v1/live/{id}/frame` + LiveFrameCache (persistentný OPMonitor stream → ffmpeg
→ posledný JPEG v cache; 0-3 ms odpoveď namiesto 13 s; Safari nepodporuje MJPEG v `<img>`).
**Produkčné nasadenie LIVE od 2026-08-12:** API :5000 + Runner :8081 + UI :4200 (systemd user
služby), Tailscale serve https://<host>.<tailnet>.ts.net:<port>, login admin (heslo
nastavené), seed NVR + 9 kamier (8 aktívnych od 2026-08-12 — CH8 nainštalovaná). UI proxy
/api/ → API (žiadny CORS, streamuje MJPEG). index.html no-store (Safari mobile cache fix).

---

## Sprint 1 — Foundation (spike + scaffolding)

- [x] **S1-1** Spike: fake NVR test server (TCP, DVRIP handshake emulácia) — prototyp v test projekte ✅ 59/59 testov
- [x] **S1-2** Spike: syntetické 4K HEVC video cez ffmpeg ✅ (6s/4K/25fps/HEVC, OpenCvSharp dekóduje + downscale 1080p realtime)
- [x] **S1-3** Spike: GPU/OpenCV CUDA ✅ (OpenCvSharp4.Cuda 1.0.4 + runtime.linux-x64 Combined = Maxwell support; driver 580>=525; nvcc netreba)
- [x] **S1-4** Projektová štruktúra ✅ (libraries/, applications/server/WatchForge.Api + web/WatchForge.UI, tests/libraries/; build 0 chýb, 94 testov OK; legacy apps/services ostávajú do S4 Runner)
- [x] **S1-5** Directory.Packages.props ✅ (CPM: 14 balíkov centralizovaných, csproj bez Version, build 0 chýb)
- [x] **S1-6** WatchForge.Contracts.Library ✅ (CameraDto, RecordingDto, DetectionDto, CreateRequestDto, RequestStatusDto — camelCase, 5/5 testov)

## Sprint 2 — Processing.Library (SQLite + jobs)

- [x] **S2-1** WatchForge.Interfaces.Library ✅ (IClock, IDateTimeProvider, 5 repo rozhraní, JobPriority/JobStatus state machine, DetectionProfile, entity modely — 6/6 testov)
- [x] **S2-2** SQLite schema ✅ (13 tabuliek, 9 indexov, WAL, FK, idempotentná — 5/5 testov)
- [x] **S2-3** Job state machine ✅ (CanTransitionTo pravidlá, Jobs.cs — 6/6 testov v Interfaces)
- [x] **S2-4** JobRepository ✅ (enqueue, claim s transakciou + prioritou, interrupted→requeue — 7/7 testov)
- [x] **S2-5** RecordingRepository ✅ (CRUD, idempotentný insert podľa nvr_filename, availability, purge query — 6/6 testov)
- [x] **S2-6** DetectionRepository ✅ (batch insert s transakciou, query čas/typ/flag, región 0..1, set flag — 5/5 testov)
- [x] **S2-7** ConfigVersionRepository ✅ (verziovanie, auto-deaktivácia starej, shared fallback — 4/4 testy)
- [x] **S2-8** UserRepository ✅ (seed 5 userov, PBKDF2 hash, reset=zmazanie hashu — 6/6 testov)

## Sprint 3 — DVRIP refactor

- [x] **S3-1** DVRIP.Library: oprava cleanup bugu (RAW súbory sa čistia), filename parsing `HH.MM` koniec + unit testy ✅ (65/65 testov, commit b086e94)
- [x] **S3-2** DVRIP.Library: interface oddelenie (IDvripClient), konfigurácia cez options + testy ✅ (77/77 testov, commit bf8d933)
- [x] **S3-3** Fake NVR test server → testovacia knižnica (reusable pre integračné/E2E testy) ✅ (WatchForge.Testing.FakeNvr, commit 99adaa2)
- [x] **S3-4** Integračné testy download pipeline proti fake NVR (login → file query → playback → dáta) ✅ (82/82 testov + read timeout fix, commit 86acd97)

## Sprint 4 — Runner (processing service)

- [x] **S4-1** WatchForge.Runner: worker loop (poll JOBS, semafor MaxParallelAnalyses=2) ✅ (5/5 testov, ClaimNextByTypeAsync, commit)
- [x] **S4-2** Runner: synchronizácia s NVR (backlog, hourly availability, missed_during_outage) ✅ (NvrSynchronizer + SyncJobHandler + INvr/ICameraRepository, 9 nových testov)
- [x] **S4-3** Runner: download job (DVRIP, .downloading cleanup, retry, interrupted na reštarte) ✅ (DownloadJobHandler + DownloadOptions, 5 nových testov)
- [x] **S4-4** Runner: analysis pipeline (CPU motion na 1080p, regióny → 4K, zápis detekcií) ✅ (AnalyzeJobHandler + FileVideoSource downscale fix, OpenCV infra do knižnice, 4 nové testy)
- [x] **S4-5** Runner: retention/cleanup job (lokálne video hneď, DB po mesiaci, persist výnimka, clips expiry) ✅ (PurgeJobHandler + IClipRepository, 5 nových testov)
- [x] **S4-6** Runner: internal HTTP endpointy (/internal/jobs, /internal/health) ✅ (Kestrel + JobStatusSummary, E2E overené)
- [x] **S4-7** Runner: recovery po reštarte (running→interrupted, preplánovanie, priorita) ✅ (RecoverInterruptedJobsAsync, 2 nové testy)
- [x] **S4-8** Integračné testy: service + fake NVR + SQLite temp (celý tok download→analysis→detections) ✅ (E2E s reálnym MP4 payloadom, fake NVR ukončenie downloadu)

## Sprint 5 — Api (REST)

- [x] **S5-1** WatchForge.Api: scaffold (ASP.NET Core MVC), CORS, error handling ✅ (MVC + /api/v1/system/health, CORS policy cez ApiCorsPolicyProvider, RFC 9457 ProblemDetails 500/404/400, 8/8 testov; legacy /api/videos nahradený)
- [x] **S5-2** Api: auth (login fixný username, set-password/reset, session cookie, API token pre agenta) ✅ (AuthController + ApiAuth session/token, 12 testov)
- [x] **S5-3** Api: GET cameras, GET recordings, GET detections (query) ✅ (3 controllery + auth ochrana, 5 testov)
- [x] **S5-4** Api: POST requests + GET requests/{id} (stav + odhad) ✅ (IRequestRepository, prioritné joby Analyze/ClipExtract, 5 testov)
- [x] **S5-5** Api: clips delivery (video súbor, fotka s detekčným obdĺžnikom — service renderuje) ✅ (ClipExtractJobHandler: ffmpeg cut + OpenCV obdĺžnik + CLIPS; GET /clips/{id} streaming)
- [x] **S5-6** Api: persist, flag/false positive, annotations endpointy ✅ (IPersist/IAnnotationRepository, POST/DELETE persist, flag, annotations + agent→admin user fix)
- [x] **S5-7** Api: profiles (GET/PUT per-camera + shared), users management (admin) ✅ (ProfilesController verziovanie + GET /users, 7 testov)
- [x] **S5-8** Api.Integration.Tests: WebApplicationFactory + fake NVR + SQLite (request→job→clip tok) ✅ (E2E FullFlow: sync→request→analyze→clip→delivery; AnalyzeJobHandler self-contained + ClipExtract enqueue)

## Sprint 6 — Web UI (Angular, design handoff)

- [x] **S6-1** WatchForge.UI scaffold (Angular standalone, Bun toolchain, SK/EN lokalizácia) ✅ (vitest + @analogjs/vitest-angular 1.22.5 (Angular 19 kompat.), I18nService + TranslatePipe SK default/EN fallback, 16/16 testov, build OK)
- [x] **S6-2** Login screen (sign in, prvé nastavenie hesla, forgot=reset; bez OAuth/register) ✅ (AuthService login/set-password/logout/me + currentUser signal, LoginComponent 3 režimy signin/setup/forgot podľa handoff, AppComponent auth gate, 36/36 testov, build OK)
- [x] **S6-3** Dashboard: video player + event panel + zoom timeline (event-first) ✅ (event-first tok detekcie→request→klip cez /api/v1; DashboardComponent + prerobený VideoPlayer (klip z /api/v1/clips/{id} + overlay box) + TimelineComponent testy (zoom/pan/seek) + Sidebar na kamery + ApiService v1; 74/74 testov, build 0 chýb/warningov)
- [x] **S6-4** Dashboard: Recordings/Cameras panely, filter chipy, TODAY stat ✅ (4 panel pily Event/Recordings/Cameras/Timeline, recordings 15-min chunky so sparkline + TOTAL, cameras panel s badge + cameraChange, filter chipy All/Person/Vehicle/High/Flagged, TODAY card s 10-bar chartom; 86/86 testov, build OK; anyComponentStyle budget 6kB→10kB/8kB→14kB kvôli rastu CSS)
- [x] **S6-5** Settings: Profile, Cameras, Detection, Users (admin), Security, System ✅ (SettingsComponent 6 sekcií + SettingsService + 40 location ikon + 10 avatarov; API: PUT /cameras/{id}, PUT /auth/me, POST /auth/change-password, GET /system/info; fix Forbid()→403; 120/120 UI testov, build OK; backend 41+61+34 testov; anyComponentStyle budget 10→14kB)
- [x] **S6-6** Focus zone editor (kreslenie zón, IGNORE/FOCUS) ✅ (ZoneEditorComponent: SVG canvas rect/polygon/freehand + undo/redo/clear + stats, Settings → Focus zones sekcia so zoznamom/Add/Edit/remove, backend ZoneDto + name/shape/points; 143/143 UI testov, API 0 failed, build OK; commit)
- [x] **S6-7** Flag screen (SYSTEM/USER boxy, anotácie) ✅ (FlagScreenComponent: červené SYSTEM boxy + zelené USER anotácie s resize handles, nástroje Select/Rectangle/Text label, undo/redo, „Clear my drawings" maže len moje anotácie, Save flag + anotácie POST/PUT; backend: DELETE + PUT /annotations; 166/166 UI testov, 67/67 API, build OK)
- [x] **S6-8** Témy: Light+Green / Dark+Purple / Contrast+Orange, prefers-color-scheme ✅ (ThemeService + CSS premenné --wf-* + sidebar switcher, 8 nových testov)

## Sprint 7 — MCP + skill

- [x] **S7-1** WatchForge.Mcp: MCP server (HTTP transport) mapujúci REST API ✅ (5 nástrojov: search_motion, get_clip, trigger_processing, get_request_status, search_recordings; smoke test curl, commit 527d755)
- [x] **S7-2** Hermes skill `watchforge`: workflow dokumentácia nad MCP nástrojmi ✅ (SKILL.md: nastavenie, tabuľka nástrojov, „hľadaj pohyb" workflow, REST náhrady)
- [x] **S7-3** Integračné testy MCP tool call + E2E s fake NVR ✅ (6/6: 5 unit + E2E initialize→tools/list→search_motion→get_request_status→get_clip)

## Sprint 8 — GPU + person detection

- [x] **S8-1** GPU: CUDA optical flow detektor (NuGet balík, fallback CPU), benchmark paralelizmu ✅ (CudaOpticalFlowDetector TV-L1 GPU + auto CPU fallback vrátane EntryPointNotFoundException, OpenCvSharp4.Cuda 1.0.4 + runtime 1.0.8, 21/21 testov; GPU benchmark gated — CUDA runtime libs nie sú na hoste nainštalované; **NÁLEZ: runtime.linux-x64 Combined obsahuje LEN sm_100 (Blackwell) cubiny → Quadro M2200 (Maxwell SM 5.0) NIE je podporovaný — GPU verifikácia na M2200 vyžaduje iný runtime/vlastný build OpenCV, pozri Report)**
- [x] **S8-2** Person/object detection (programatická CV, reťazená za motion) ✅ (HogPersonDetector HOG+SVM — nie ML model, IObjectDetector, AnalyzeJobHandler reťazenie: person len pri motion, DetectionType=person + ObjectClass + Confidence, 4 nové testy HOG + 2 integračné)
- [x] **S8-3** Face recognition vrstva (4K crop) — samostatná etapa, otvorené rozhodnutie ML model ✅ (LbphFaceRecognizer: LBP cascade detekcia + LBPH rozpoznanie — klasická CV, žiadny ML model, konzistentné s FR-04; IFaceRecognizer (DetectFaces/Learn/Reset), reťazenie v AnalyzeJobHandler: face LEN po person detekcii (FR-05), DetectionType=face + ObjectClass=identity meno, lbpcascade_frontalface.xml v Assets (CopyToOutputDirectory); 4 HOG-style testy + 2 integračné; 29/29 + 39/39. Pozn.: perzistencia embeddings do FACES/IDENTITIES a 4K crop sú ďalšia etapa)

## Sprint 9 — Deployment

- [x] **S9-1** compose.yaml (4 kontajnery, shared volumes, secrets cez Podman) ✅ (Dockerfile.runner/api/mcp/ui + nginx.conf + entrypoint-runner.sh (Podman secret → env) + .dockerignore; CPU-only build OpenCV v kontajneri (WatchForgeCpuOnly — CUDA .so vyžaduje libcuda runtime; M2200 aj tak nepodporuje sm_100); GTK3 deps; buildy overené: runner naštartuje (8081), mcp/ui OK; NU1510 fix — redundantné PackageReference odstránené)
- [x] **S9-2** Quadlet units + env vars (WATCHFORGE_NVR_AUTH_FILE, API token, DB path) ✅ (deploy/quadlet/: 4 .container + .network, overené podman-system-generator -dryrun; entrypoint-runner.sh podporuje WATCHFORGE_NVR_AUTH_FILE aj Podman secret; env vars dokumentované v unitoch)
- [x] **S9-3** SQLite backup job (VACUUM INTO) + WAL checkpoint ✅ (deploy/scripts/backup-db.py: WAL checkpoint TRUNCATE + VACUUM INTO + rotácia 14 dní; overené na testovacej DB — dáta, rotácia; Quadlet service + timer (denne 04:00, Persistent=true), systemd-analyze verify OK)
- [x] **S9-4** E2E s fake NVR cez compose („hľadaj pohyb 14:00–16:00" → fotka s obdĺžnikom) ✅ (deploy/scripts/e2e.sh: fake NVR host kontajner + seed DB + full tok sync→analyze→clipextract→klipy; **E2E odhalilo 2 produkčné bugy: (1) zdieľaný SqliteConnection nie je thread-safe → repos otvárajú spojenie per operáciu + WAL len pri create DB; (2) RunnerService neclaimoval ClipExtract joby → opravené + regresný test**; E2E PASS — video MP4 + fotky JPEG doručené)
- [x] **S9-5** Dokumentácia: README update, deploy guide, runbook ✅ (README.md prepísaný na aktuálnu architektúru (4 kontajnery, funkcie, deployment, testy); deploy/README.md — build, secrets, compose + Quadlet, backup, E2E, runbook (health, joby, troubleshooting, upgrade), env var tabuľka; deploy/secrets/nvr-auth.yaml.example)

---

## Report rozhodnutí (na konci behu)

- [x] **Zoznam rozhodnutí, ktoré agenti spravili za stakeholdera (s odôvodnením)** (2026-08-08 13:51)
  - **S8-1 (2026-08-08)**: GPU verifikácia na M2200 nie je možná s `OpenCvSharp4.Cuda.runtime.linux-x64` 1.0.8 (Combined): balík obsahuje len `sm_100` (Blackwell) cubiny, M2200 je Maxwell SM 5.0; navyše na hoste chýbajú CUDA 12.8 toolkit runtime libs (libnpp/libcublas/libcudnn/libcufft — len driver libcuda.so.1). Detektor je implementovaný s plnohodnotným CPU fallbackom (overený, 21/21 testov) — CPU cesta ostáva primárna, GPU cesta je gated na stroj s podporovaným GPU (Turing+) alebo na vlastný OpenCV build s Maxwell support (vyžaduje nvcc, ktorý nie je nainštalovaný).
  - **S9-1 (2026-08-08)**: Kontajnerový build Runnera používa **CPU-only OpenCV** (`-p:WatchForgeCpuOnly=true` v Dockerfile.runner) — CUDA build .so vyžaduje libcuda/libnpp runtime, ktoré v kontajneri nie sú, a M2200 aj tak nepodporuje sm_100. `CudaOpticalFlowDetector.cs` sa v CPU-only builde vylúči z kompilácie (Runner ho neregistruje). Do runtime image pridané GTK3/atk/atomic libs (OpenCV highgui linkovacia závislosť aj bez GUI). Odstránené redundantné PackageReference (NU1510) — Hosting/Configuration.Binder poskytuje FrameworkReference Microsoft.AspNetCore.App.
  - **S9-4 (2026-08-08) — E2E odhalilo 2 produkčné bugy**:
    (1) **SQLite thread-safety**: všetky repos zdieľali jeden `SqliteConnection` (singleton), Microsoft.Data.Sqlite connection nie je thread-safe a Runner claimuje joby paralelne → `SqliteTransaction has completed`. Fix: repos si otvárajú spojenie **per operáciu** (`OpenConnectionStringAsync`), WAL/PRAGMA sa nastavujú len pri vytvorení DB (PRAGMA journal_mode=WAL pri každom otvorení vyžaduje exkluzívny zámok → „database is locked"). ClaimNextCoreAsync už nerollbackuje commitnutú transakciu.
    (2) **ClipExtract sa nikdy nespracúval**: RunnerService claimoval len `JobType.Analyze` (komentár tvrdil „Analyze/ClipExtract"), ClipExtract joby ostávali navždy queued → klipy sa negenerovali. Fix: analysis slot claimuje Analyze **alebo** ClipExtract; regresný test `Runner_ClaimsAndExecutesClipExtractJob`.
  - **S9-4 (E2E infra)**: fake NVR host (`applications/tools/WatchForge.FakeNvr.Host`) — FakeDvripServer dostal `IPAddress.Any` + fixný port pre kontajner (default zachovaný: loopback + náhodný port pre testy). E2E skript seeduje DB cez python sqlite3 (po init API štarte, ktorý vytvorí schému); podman kontajnery potrebujú `--network-alias fake-nvr` (compose by alias spravil automaticky).
  - **S8-3 (2026-08-08)**: Face recognition implementovaná **klasickou CV (LBP cascade + LBPH)**, NIE ML modelom — konzistentné s FR-04 (HOG) a rozhodnutím „programatically without AI model". FR-05 povoľuje ML ako jediná vrstva, ale pre prvú funkčnú etapu je LBPH dostatočné (detekcia + rozpoznanie + identity v pamäti). `IFaceRecognizer` je navrhnutý tak, že ML model (napr. DeepFace/ArcFace) sa dá neskôr podsunúť bez zmeny volajúceho kódu. Otvorené etapy: perzistencia embeddings do FACES/IDENTITIES + 4K face crop + UI pre správu identít.
- [x] **Stav každej úlohy, BLOCKED položky s dôvodmi** (2026-08-08 13:51)
  - **53/53 úloh DONE** (Sprint 1: 6, Sprint 2: 8, Sprint 3: 4, Sprint 4: 8, Sprint 5: 8, Sprint 6: 8, Sprint 7: 3, Sprint 8: 3, Sprint 9: 5).
  - **0 BLOCKED, 0 WAITING, 0 NOT STARTED** — kanban je kompletne vyčerpaný, žiadna ďalšia úloha neexistuje.
  - Build celej solution: **0 chýb** (57 warningov — TUnit0018 instance data v Mcp integračných testoch, neblokujúce).
  - Posledný známy stav testov: S8-3 = 29/29 + 39/39 (Runner + Api), predtým 262/262 celkovo.
- [x] **Odporúčania pre ďalšie iterácie** (2026-08-08 13:51)
  - **Sprint 10 navrhovaný obsah** (odvodené z otvorených položiek requirements §4 + architecture §10 + poznámok v kanbane):
    1. **FR-05 dokončenie**: perzistencia embeddings do FACES/IDENTITIES + 4K face crop (S8-3 poznámka „ďalšia etapa"), `IFaceRecognizer.Learn/Reset` napojenie na reálne identity, API pre správu identít.
    2. **GPU verifikácia na vhodnom stroji** (Turing+) alebo vlastný OpenCV build s Maxwell support — S8-1 nález ostáva otvorený; CPU-only je produkčná cesta.
    3. **Web UI E2E (Playwright)**: prihlásenie → timeline → prehrávanie → persist (architecture §8 to má ako voliteľné, doteraz neurobené).
    4. **`GET /api/v1/system/status`** endpoint (architecture §9 tabuľka ho uvádza pre FR-11 — overiť, či existuje, resp. doplniť synchronizáciu/backlog/NVR status).
    5. **NFR-03 benchmark paralelizmu** (GPU 2–3 / CPU 2) — formálne meranie na cieľovej konfigurácii.
    6. **CI pipeline** (GitHub Actions/self-hosted): build + testy + E2E s fake NVR pri každom pushi (zatiaľ len lokálne overovanie).
    7. **Migrácia na produkčné nasadenie** na lokálnom hoste: Quadlet units + secrets + backup timer + prvé reálne spustenie proti NVR (gated, vyžaduje schválenie stakeholdera).

---

## Sprint 10 — FR-05 produkčne (face perzistencia + identity API + UI)

- [x] **S10-1** FaceRepository + IdentityRepository (FACES/IDENTITIES CRUD, embedding BLOB, confidence, temp_crop_path) — repo testy
- [x] **S10-2** Perzistencia embeddings do DB: `IFaceRecognizer` load/save z FACES, Learn/Reset napojené na identity (naštartovanie = načítanie, naučenie = uloženie)
- [x] **S10-3** 4K face crop: crop z originálneho 4K framu (nie 1080p downscale) — prepojenie regiónov 0..1 → pixely 4K
- [x] **S10-4** API pre identity: GET/POST /api/v1/identities, POST learn (crop upload), DELETE identity (aj embeddings), testy
- [x] **S10-5** Web UI sekcia „Identity" (zoznam identít, pridanie tváre/crop, vymazanie) + UI testy

## Sprint 11 — Web UI E2E (Playwright)

- [x] **S11-1** Playwright setup (Bun toolchain, config, smoke test na login)
- [x] **S11-2** E2E: login → dashboard (timeline, prehrávanie klipu)
- [x] **S11-3** E2E: persist/flag flow (fotka s obdĺžnikom → persist/flag/annotations)
- [x] **S11-4** E2E: settings (profily, kamery, focus zóny) + témy switcher

## Sprint 12 — system/status endpoint + GPU decision

- [x] **S12-1** `GET /api/v1/system/status` (sync/backlog/NVR/detekcia stavy) + testy
- [x] **S12-2** Status karta v dashboard UI (NVR online, backlog, posledný sync)
- [x] **S12-3** GPU decision doc — `docs/gpu-analysis.md` (hotový 2026-08-08) + záver pre kanban

## Sprint 13 — Benchmark na reálnych dátach (NVR lokálna lokalita)

- [x] **S13-1** NVR discovery (DHCP): ak `NVRS.host` nedosiahnuteľný → sken LAN na port 34567 (Xiongmai) + login overenie, auto-update host v DB; NVR nemá statickú IP (DHCP), takže kód s tým musí počítať
- [x] **S13-2** Stiahnuť 6×15 min záznamov (6 kamier, dnešný deň) cez Runner/DVRIP do /media
- [x] **S13-3** Benchmark pipeline: download / analyze (CPU) / clip extract časy + NFR-03 paralelizmus (1–4 analýzy)
- [x] **S13-4** Report benchmarkov: do chatu stakeholderovi (tabuľka) + docs/benchmark-YYYY-MM-DD.md

## Sprint 14 — CI pipeline

- [x] **S14-1** GitHub Actions workflow: build riešenia + .NET testy (TUnit projekty)
- [x] **S14-2** CI: Web UI testy (bun/vitest) + E2E s fake NVR (deploy/scripts/e2e.sh)
- [x] **S14-3** CI: status badge + README sekcia, artefakty (image build sanity)

## Report S10–S14 (kompletné 2026-08-08)

**Sprint 10 — FR-05 produkčne** ✅: FaceRepository/IdentityRepository (FACES/IDENTITIES), embeddings perzistencia (Learn→DB, LoadState pri štarte), 4K face crop z originálu, Identity API (GET/POST/learn/DELETE), Web UI Settings→Identity sekcia (SK/EN). Commity `26c5830`…`f96c331`.

**Sprint 11 — Web UI E2E (Playwright)** ✅: 12/12 E2E testov (login, dashboard, flag flow, settings+témy); infra: playwright.config.ts (API:5000 + Angular:4200), tools/e2e-seed, scripts/e2e-api.sh (--no-launch-profile, health readiness). Commity `ecb40f4`…`9fd60ba`.

**Sprint 12 — system/status + GPU** ✅: `GET /api/v1/system/status` (NVR+worker+sync/backlog, repo metódy CountBacklog/CountAllAndCompleted/GetLastSync), status karta v Settings→System, GPU decision doc `docs/gpu-analysis.md` (M2200 Maxwell vs sm_100 — GPU na tomto HW nemožné). Commity `2a11c82`, `50a4069`.

**Sprint 13 — Benchmark na reálnych dátach** ✅:
- S13-1 NVR discovery: `NvrDiscovery` (LAN sken DVRIP port + login + auto-update host v DB) — commit `004052a`
- S13-2: 6×15 min segmentov (4K HEVC, ~900 s) stiahnutých z reálneho NVR cez `tools/nvr-bench` (~7 Mbit/s) — commit `6e14611`
- S13-3: `tools/real-bench` na 15 reálnych segmentoch → **docs/real-benchmark.md** (Farneback 616 ms/f bottleneck, decode 29 ms, HOG 881 ms, 720p=2.1× rýchlejšie, 4 paralelné analýzy OK) — commit `818d5f4`
- S13-4 report: do chatu + docs/real-benchmark.md ✅

**Sprint 14 — CI pipeline** ✅: `.github/workflows/ci.yml` (build+testy, UI job, Playwright E2E job + report artifact), čistý Release build 0/0 (opravených ~100 warningov: HasCount→Count, nullable, TUnit NoWarn v tests/Directory.Build.props), badge v README. Commity `d79c034`, `a7f8038`, `dade1a8`.

**Celkový stav série S10–S14: 18/18 úloh ✅ · testy 298 .NET + 171 UI + 12 E2E · Release build 0 Warning / 0 Error · 13 commitov pushnutých na `watchforge-redesign`**

## Sprint 15 — Meranie paralelného downloadu (S15)

- [x] **S15-1** `tools/nvr-dl-parallel` v1: per-stream 6 Mbit/s konštantne pre 1/2/3/6/8 streamov — NVR neobmedzuje agregovanú šírku (commity `3734f64`, `f9cd489`)
- [x] **S15-2** v2 čisté meranie: rovnakých 16×15-min segmentov pre konfigurácie 4,6,8,10,12,14,16 + porovnanie s v1 (commit `4d5a164`)
- [x] **S15-3** `docs/parallel-download-benchmark.md` — výsledky + interpretácia + odporúčanie pre S16 (commit `ca2ae03`)

**Výsledok:** 4→16 streamov = 20→62 Mbit/s agregovane, per stream 5 Mbit/s drží do 8; 4 streamy ≈ 11 min pre 6×15-min (namiesto 50 min sekvenčne).

## Sprint 16 — Paralelné downloady + analýzy produkčne (S16)

Rozhodnutie na základe reálnych meraní: `docs/parallel-download-benchmark.md` (S15 v2) —
NVR neškrtí agregovanú šírku (20→62 Mbit/s pre 4→16 streamov), per stream drží
5 Mbit/s do 8 streamov. Analýza je bottleneck (Farneback ~616 ms/f @1080p, 15-min
video ≈ 26 min CPU). Pipeline (download N+1 počas analýzy N) už existuje — stačí
zvýšiť sloty.

- [x] **S16-1** Config: `MaxParallelAnalyses = 4` + `MaxParallelDownloads = 4` (defaults v `RunnerOptions`, prepísateľné cez env)
- [x] **S16-2** Testy: defaults=4 + 4 analýzy bežia paralelne (semafor) — Runner 44/44
- [x] **S16-3** Kanban report + docs (benchmark download → `docs/parallel-download-benchmark.md`)

**Výsledok**: 6×15-min záznamov ≈ 69 min (namiesto ~91 min pri 2×), 8 kamier ≈ 73 min
(dve várky 4+4, download sa schová pod analýzu). Pri backfille download 4 sloty =
~4× rýchlejšie sťahovanie.

## Sprint 17 — Face recognition ML upgrade (FR-05 povolené) ✅ HOTOVÉ

- [x] **S17-1** OpenCV DNN infra: YuNet (230 KB) + SFace (37 MB) ONNX modely do Assets (MIT licencia), OnnxFaceRecognizer stub
- [x] **S17-2** YuNet detekcia + 5 landmarkov + affine align (narovnanie tváre)
- [x] **S17-3** SFace **128-dim** embedding + cosine podobnosť vs DB (threshold 0.36), nahradí LBPH histogram
- [x] **S17-4** Migrácia: FACES.embedding (128 float = 512 B, BLOB kompatibilný), Learn/Reset, integračné testy + UI verifikácia

## Sprint 18 — Preempcia (FR-08) + drobnosti ✅ HOTOVÉ

- [x] **S18-1** Per-job CancellationTokenSource (dnes len globálny shutdown token)
- [x] **S18-2** Preempt logika: prioritný job (WhatsApp/Telegram) preruší najnižšie-prioritný bežiaci background job → interrupted → requeue → slot pre prioritný
- [x] **S18-3** FR-13 cache test (expirovaný klip zmazaný + ešte platný uchovaný) ✅ hotové (Purge_NotYetExpiredClip_IsKept, 45/45)
- ~~**S18-4** Export celého denného záznamu v UI (FR-11)~~ → **ZRUŠENÉ 2026-08-10** (stakeholder: zbytočné). Nahradené S20: export SELEKCIE.

## Sprint 19 — DVRIP live view v UI (čaká na implementáciu)

**Rozhodnutie — DVRIP playback streaming (FR-11, otvorená položka §4):** ❗ **OSTÁVA OTVORENÁ** (nie zamietnutá). HW (M2200, CPU-only) nedá live stream analýzu, takže live analýza/restream na UI appku sa nerobí — ale myšlienka „všetko na jednom mieste v rámci web UI" a feasibility DVRIP stream cez API (bez čakania na download) zostáva na stole pre tento sprint. Nerozhodnuté, otvorené.

**Výstupy feasibility (2026-08-10, docs/s18-network-analysis.md):** sieť potiahne 8 kamier
naraz (8×5 Mbit/s = 40 Mbit/s z 1 Gbit NIC = 4 %, NVR zvládne 62 Mbit/s @16 streamov);
⚠️ RTSP na NVR IGNORUJE číslo kanála (všetky URL = rovnaký stream) → live multi-kanál
LEN cez DVRIP; limit nie je sieť, ale CPU decode (8×4K ≈ 8 jadier → náhľad v 720p).
- [x] **S19-1** Feasibility spike: DVRIP OPPlayBack stream → HLS/MP4 cez API (transkód podľa potreby)
- [x] **S19-2** UI live view: prepínanie zobrazenia kamier **1 / 4 / 8** (mriežka), stream z NVR cez API
- [x] **S19-3** Tlačidlo „Analýzy" — prepnutie z live view na detekcie/analýzy (event-first dashboard)
- [x] **S19-4** Fallback zachovaný: dočasné stiahnutie + lokálne streamovanie s cleanup

## Sprint 20 — Export selekcie cez UI (čaká na implementáciu, stakeholder)

- [x] **S20-1** Export vybranej časovej úsečky v UI: používateľ označí interval (napr. 3 minúty) na timeline → stiahne záznam (klip) ako dôkaz
- [x] **S20-2** Reuse ClipExtract jobu (ffmpeg cut s kontextom) — presne ten istý mechanizmus ako dôkazové klipy z detekcií
- [x] **S20-3** Export button v event-first view + stiahnutie MP4 cez /api/v1/clips/{id} streaming

## Sprint 21 — Produkčné nasadenie + test nasadenia (gated, vyžaduje schválenie stakeholdera) — ďalší po S19/S20

- [x] **S21-1** Quadlet units + secrets + backup timer — reálne spustenie na lokálnom hoste proti reálnemu NVR (nie fake NVR!)
- [x] **S21-2** Seed produkčnej DB: 7 (neskôr 8) kamier, používatelia, NVR
- [x] **S21-3** Prvý reálny E2E tok: sync → download → analyze → detekcie → klip (reálne dáta)
- [x] **S21-4** Smoke test UI/API v kontajneroch + skill IP overenie
- [x] **S21-5** **Sprístupnenie web UI cez Tailscale serve** (prístup z vonku/telefónu bez port forwardingu) — `tailscale serve` na laptope + overenie cez tailnet

## Sprint 22 — Druhá NVR jednotka pre 2 WiFi kamery (💡 IDEA — nie je isté, či bude)

**Kontext:** 2 nové WiFi kamery so solárom → 9. kanál na existujúcom NVR by bol posledný
a WiFi kamery na inej subsieti komplikujú routovanie. Nápad: samostatná NVR
jednotka (nový HW) na nahrávanie WiFi kamier. **IDEA — stakeholder možno ani nekúpi,
spraví sa len ak k nákupu príde.** Ak áno, potrebné detaily: NVR značka/model,
WiFi kamery Xiongmai vs ONVIF, sieť lokálna vs vlastná.
- [ ] **S22-1** Multi-NVR podpora v DB a sync (NVRS tabuľka už existuje — NvrSynchronizer už iteruje všetky NVR, overiť otestovaním)
- [ ] **S22-2** WatchForge: pripojenie 2. NVR (host, port, creds) + discovery pre viac NVR
- [ ] **S22-3** UI: výber NVR/kamery v dashboard (kamery z oboch NVR, prefix podľa NVR)
- [ ] **S22-4** HW: výber vhodnej lacnej NVR jednotky (4ch, 1× SATA, ONVIF) — odporúčanie + kúpa
- [ ] **S22-5** Záloha: 2. NVR má vlastný disk + backup timer (rozšíriť backup-db.py na 2 NVR)

## Report S17+S18 (dokončené 2026-08-09)

**Sprint 17 — Face ML upgrade (FR-05)** ✅: `OnnxFaceRecognizer` namiesto LBPH —
YuNet (FaceDetectorYN, detekcia + 5 landmarkov + NMS) → affine align → SFace
128-dim embedding → cosine threshold 0.36. Modely YuNet 232 KB + SFace 38.7 MB
(MIT, OpenCV Zoo) v Assets. Perzistencia = 128 float (512 B) BLOB (S10-2 vzor).
DI v Runner+Api prepnuté; LBPH ostáva v knižnici. 8 testov — 310 .NET zelených.
Commit `39d7667`.

**Sprint 18 — Preempcia (FR-08)** ✅: per-job CancellationTokenSource
(namiesto globálneho shutdown tokenu) + preempt logika: keď sú analysis sloty
plné a čaká job s vyššou prioritou (WhatsApp 100 > Telegram 90 > WebUI 80 >
Background 10), preruší najnižšie-prioritný bežiaci job → interrupted →
requeue → slot pre prioritný. JobExecutor propaguje OCE pri zrušení ct (nie
Failed). JobRepository `PeekNextByTypeAsync` + `RequeueInterruptedJobAsync`.
2 testy — Runner 47/47, spolu **310 .NET testov zelených**, build 0/0. Commit `906e1ed`.

**FR-13 cache test** ✅: `Purge_NotYetExpiredClip_IsKept` (ešte platný klip = uchovaný).

**Celkový stav: 325 .NET + 189 UI + 15 E2E testov · Release build 0 Warning / 0 Error**
