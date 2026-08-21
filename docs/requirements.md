# WatchForge — Normalized Requirements (FR/NFR)

> Status: normalizovaná verzia požiadaviek, východiskový dokument pre architektúru a kanban.
>
> Zdroj: pôvodný konverzačný draft `docs/product-requirements.md` (zjednotený do tohto dokumentu 2026-08-15) — všetky rozhodnutia boli potvrdené stakeholderom počas zberu requirements 2026-08-06.
>
> Konvencia: každá požiadavka má ID, popis a akceptačné kritérium (AK), podľa ktorého sa overí splnenie. Kanban úlohy odkazujú na tieto ID.

## 0. Kontext

WatchForge je self-hosted systém pre enhanced movement detection nad NVR systémom Movols (lokálna lokalita). Systém sťahuje uzavreté záznamy z NVR cez DVRIP, analyzuje ich vlastnou detekciou (motion → person/object → face), ukladá vyhľadateľné metadata do jednej SQLite databázy a poskytuje výsledky cez web UI a AI agenta (MCP). Lokálne video je len dočasné — NVR zostáva autoritatívnym úložiskom originálov.

Cieľová platforma: **lokálny host** (Ubuntu, rootless Podman, NVIDIA Quadro M2200 4 GB VRAM, i7-7820HQ, 31 GB RAM, ~377 GB voľného disku).

Merané dáta (2026-08-06): HEVC/H.265 4K (3840×2160) @ 25 fps, ~4.8 Mbit/s; 15-min segment ≈ 620–700 MB; event klip (≤2 min) ≈ 5.5–25 MB; NVR na `<nvr-ip>:34567` (lokálna LAN); ~5.2 GB/hod nových záznamov pri 8 kamerách bez konverzií.

---

## 1. Functional Requirements (FR)

### FR-01 Synchronizácia s NVR

- **Popis**: Systém priebežne synchronizuje stav s NVR. Pri štarte zistí rozdiel medzi lokálne známymi segmentmi a segmentmi dostupnými na NVR; dobieha backlog (všetko, čo NVR ešte má, bez tvrdého stropu). Počas behu zisťuje nové uzavreté záznamy (štandardné 15-min segmenty aj kratšie NVR event klipy). Dostupnosť originálov sa kontroluje každú hodinu.
- **Detaily**:
  - NVR cyklu nahrávok sa systém nedotýka (NVR si ho spravuje sám).
  - Segmenty, ktoré NVR medzitým vymazalo, sa v DB označia ako `missed during outage` (nezmažú sa okamžite).
  - NVR prihlasovacie údaje a adresa: `<nvr-ip>:34567`, user `<nvr-username>` (konfigurovateľné, v secrets).
- **AK**: Po štarte so 7 dňami histórie na NVR systém dobehne všetky dostupné segmenty. Po hodinovej synchronizácii sú segmenty zmazané z NVR označené `unavailable`/`missed during outage` v DB.

### FR-02 Download pipeline (DVRIP)

- **Popis**: Service sťahuje záznamy z NVR cez DVRIP protokol (reusable library). Download prebieha výhradne v service kontajneri; žiadne súborové handoff medzi kontajnermi. Lokálny video súbor je dočasný processing workspace.
- **Detaily**:
  - Čiastočne stiahnuté súbory (`.downloading`) sa po reštarte mažú a job prebieha odznova.
  - Sťahovanie rešpektuje limity súbežnosti a sieťovú kapacitu.
  - Po úspešnej analýze sa lokálny video súbor odstráni.
  - Download/konverzia nesmú vytvárať dlhodobé duplicity (MKV+RAW+MP4 zároveň) — jediný dočasný formát a jednoznačný cleanup po každom kroku.
- **AK**: Stiahnutý segment je po analýze odstránený z disku. Po reštarte počas downloadu je čiastočný súbor zmazaný a job prebehne odznova. V pracovnom priečinku nikdy neexistujú duplicitné formáty toho istého segmentu naraz.

### FR-03 Detekcia pohybu (Motion detection)

- **Popis**: Základná detekčná vrstva nad každým stiahnutým záznamom (15-min segmenty aj event klipy) — bez ohľadu na NVR detekciu (NVR detekcia nikdy nie je filter). Implementácia: OpenCV Farneback dense optical flow, CPU aj GPU backend.
- **Detaily**:
  - Analýza beží na **fixnom 1080p downscale** (4× redukcia plochy z 4K; 720p sa nepoužíva — 9× redukcia, nedostatočná).
  - Výstup: čas detekcie, regióny pohybu (bounding regions), intenzita; regióny prepočítané späť na 4K koordináty.
  - **Každá** kamera sa analyzuje nezávisle.
- **AK**: Pre 15-min segment s pohybom systém vráti aspoň jeden motion event s regiónmi v 4K koordinátoch. Segment bez pohybu vráti prázdny zoznam eventov (ale je evidovaný ako spracovaný).

### FR-04 Detekcia osôb/objektov (Person/Object detection)

- **Popis**: Samostatná detekčná vrstva; spúšťa sa len na základe výsledkov motion detection (pipeline reťazenie — nie na snímkach bez pohybu). Klasifikuje pohybujúce sa objekty: človek, auto, zviera a podobne.
- **Detaily**:
  - Výstup: bounding box, trieda objektu, confidence.
  - **Implementácia je programatická (non-ML / klasické CV techniky), NIE AI model** — stakeholder rozhodnutie.
  - Voliteľné filtrovanie falošných pohybov (pohyb bez klasifikovateľného objektu).
- **AK**: Ak motion detection nájde pohyb v regióne, kde je človek, systém vráti person detekciu s bounding boxom. Bez pohybu sa person detekcia nespúšťa.

### FR-05 Face recognition (databáza tvárí)

- **Popis**: Najvyššia detekčná vrstva — identifikácia osôb („kto to bol“), vlastná databáza tvárí, embeddings, identity.
- **Detaily**:
  - Spúšťa sa, keď person detection identifikuje osobu; face crop sa vyreže z **originálneho 4K framu** (nie z 1080p downscalu) — embeddings potrebujú maximum detailov.
  - Výstup: identity match + confidence.
  - **Implementované (S10 + S17)**: YuNet detekcia + affine align + SFace 128-dim embedding (cosine threshold 0.36); perzistencia embeddings do FACES/IDENTITIES; Identity API (GET/POST/learn/DELETE) + UI sekcia „Identity" (Settings, admin).
- **AK**: Pri známej osobe v zábere systém vráti identity match s confidence. Neznáma osoba sa označí ako neznáma/nová.

### FR-06 Per-camera detekčné profily a verziovaná konfigurácia

- **Popis**: Každá kamera má vlastný detekčný profil (IGNORE zóny, FOCUS zóny, citlivosť) + zdieľaný profil pre spoločné zóny, ktoré sa nemajú analyzovať (napr. vrytý čas/overlay HUD — ~2 spoločné zóny). Na jednu kameru môže byť viac profilov (deň/noc). Terminológia: **zóna** — IGNORE zóna = maska/exclusion región, FOCUS zóna = focus región.
- **Detaily**:
  - Konfigurácia je verziovaná: pri analýze sa ukladá verzia/snapshot použitej konfigurácie.
  - Zmena konfigurácie platí **iba pre nové analýzy**; staré záznamy majú v DB poznámku, že konfigurácia bola v čase analýzy iná. Reanalýza histórie nie je automatická.
- **AK**: Zmena IGNORE zóny na kamere 2 neovplyvní už spracované segmenty; nové analýzy ju rešpektujú. DB obsahuje verziu konfigurácie pre každý segment.

### FR-07 SQLite dátový model

- **Popis**: Jedna SQLite databáza (WAL mode) ako jediný zdroj pravdy. Logické oddelenie dát podľa `camera_id` (a `nvr_id`/`site_id` od začiatku — single-site funkcionalita, ale model pripravený na rozšírenie). Fyzicky oddelené databázy pre kamery sa nepoužívajú.
- **Entita (návrh, spresní architektúra)**:
  - `recordings` — segmenty: id, nvr_id, camera_id, zdroj (15-min/event klip), NVR filename, begin/end time, dostupnosť na NVR, stav (unavailable, missed during outage), čas zistenia nedostupnosti, plánovaný purge, persist značka, config verzia, stav analýzy.
  - `detections` — typ, čas, interval, regióny/bounding box, trieda, intensity/confidence, algoritmus verzia, väzba na recording.
  - `faces` / `identities` — embeddings, identity, confidence (vrstva 6.3).
  - `jobs` — download/analysis joby: stav (queued/running/interrupted/completed/failed), priorita, zdroj požiadavky, payload, error info.
  - `users` — web UI používatelia (max 5): admin + rodinní používatelia; role admin/štandardný.
  - `config_versions` — snapshoty detekčnej konfigurácie.
  - `persist_flags` — história persist/zachovaj značiek.
- **AK**: Všetky detekcie sú dohľadateľné podľa kamery, času a typu. DB prežije reštart bez straty commitnutých záznamov.

### FR-08 Job orchestration a prioritizácia

- **Popis**: Front jobov s prioritami. Background analýza má nižšiu prioritu ako interaktívna požiadavka; interaktívna požiadavka môže prerušiť background job (segment sa po vybavení reštartuje odznova). Job orchestration cez SQLite tabuľku `jobs` (persistencia = recovery po reštarte) + interné HTTP REST endpointy service (`/internal/jobs`, `/internal/jobs/{id}`, `/internal/health`).
- **Detaily**:
  - Priorita podľa zdroja požiadavky: **WhatsApp > Telegram > Hermes Web UI > system > background**.
  - Limit interaktívnej požiadavky sa vzťahuje len na nespracované nahrávky — tie sa procesujú postupne s prioritou; používateľ dostane spätnú väzbu a odhad času.
  - Kapacita (S16, merané): **MaxParallelAnalyses = 4** + **MaxParallelDownloads = 4** (defaults, env prepísateľné); paralelizmus overený benchmarkom (S13/S15).
  - Preempcia (S18, FR-08): keď sú analysis sloty plné a čaká job s vyššou prioritou, preruší sa najnižšie-prioritný bežiaci job → interrupted → requeue; **Sync/Purge sa neprerušujú** (vlastný maintenance slot).
  - Pri reštarte: `running` joby → `interrupted` + preplánovať; prioritný job → zdvihnúť okamžite.
  - Nesplniteľné prioritné požiadavky (nahrávky už vymazané z NVR) → zrušiť a označiť `priority missed during outage`.
- **AK**: Interaktívna požiadavka z WhatsAppu sa spracuje pred požiadavkou z Web UI. Po reštarte sa prerušené joby preplánujú; prioritné sa spustia okamžite.

### FR-09 REST API

- **Popis**: Jediné systémové rozhranie pre web UI aj agenta. Endpointy pokrývajú: zoznam kamier/segmentov, query detekcií (čas, kamera, typ), stav spracovania/jobov, trigger on-demand spracovania, media delivery (klip/obrázok), konfiguráciu profilov, persist značky, autentifikáciu.
- **Detaily**:
  - Agent-friendly JSON odpovede + odkazy na média (download).
  - Web UI volá REST API samostatne (vlastný kontajner).
  - Interná komunikácia medzi API a service: HTTP REST + SQLite job store. **gRPC sa nepoužíva.**
- **AK**: Agent aj web UI dokážu vykonať rovnaký query cez REST API a dostať konzistentné odpovede.

### FR-10 MCP server + agent skill

- **Popis**: MCP server (HTTP transport) mapuje REST API na MCP nástroje pre agenta (napr. `mcp_watchforge_search_motion`, `mcp_watchforge_get_clip`, `mcp_watchforge_trigger_processing`). Po nasadení MCP sa vytvorí **skill** pre agenta ako workflow dokumentácia nad MCP nástrojmi (ako interpretovať požiadavku, ktoré nástroje zavolať, ako odpovedať).
- **AK**: Agent v Hermes (cez MCP) dokáže zodpovedať „bol tam človek medzi 14:00–16:00?“ a doručiť fotku/video. Skill existuje a dokumentuje workflow.

### FR-11 Web UI

- **Popis**: Angular web UI (vlastný kontajner) — live view kamier, event—first dashboard, settings, focus zone editor, flag screen, témy. **Verejné UI od S22c**: live view + analýzy + štatistiky bez prihlásenia (live view je default), admin login cez `#/admin`.
- **Detaily (implementovaný stav)**:
  - **Live view**: režimy **1 / 8** (view 2/4 odstránené S22d; dvojklik/tap prepína 1↔8), stream z NVR cez DVRIP OPMonitor → ffmpeg → LiveFrameCache (JPEG polling `?fps=N` / MJPEG), fps podľa siete (view 8 = 1 fps, view 1 = 15 fps), `POST /live/{id}/release` uvoľní stream do ~30 s bez diváka (S22f).
  - **Analýzy/Detekcie**: event-first dashboard (timeline, filter chipy, TODAY stat), verejné.
  - **Settings (admin-only)**: Profile, Cameras, Detection (popisky + semafor prahu), Focus zones, Users, Security, System (služby+verzie).
  - **Focus zone editor**: kreslenie zón (Rectangle/Polygon/Freehand, undo/redo), pozadie = denné foto kamery (cron 08:00, `zone-background`).
  - **Flag screen**: systémové detekcie vs užívateľské anotácie, nástroje Select/Rectangle/Text, „Clear my drawings".
  - **Export selekcie (S20)**: používateľ označí interval na timeline → ClipExtract job → MP4 dôkazový klip.
  - Lokalizácia: **slovenčina default, angličtina ako backup**; dátumy SVK DD.MM.YYYY + 24h.
  - PWA (manifest + service worker + ikony), logo, témy Light+Green / Dark+Purple / Contrast+Orange.
- **AK**: Používateľ v UI vidí live kamery (1/8), nájde detekcie na timeline, prehrá klip, upraví profil kamery, označí segment persist, prejde cez focus zone editor a flag screen. UI je v slovenčine s prepnutím do angličtiny.

### FR-12 Autentifikácia web UI

- **Popis**: **Verejné UI od S22c (2026-08-14)** — live view, analýzy a štatistiky sú prístupné **bez prihlásenia**; prihlasovanie je len pre **admina** (settings, flag/persist/export/requests). SQLite tabuľka `users`; agent (MCP→API) používa API token v secrets, nie používateľský login.
- **Detaily**:
  - Rodinné účty **vymazané z DB** (S22c) — zostáva len **admin**; login cez `#/admin` (sidebar tlačidlo).
  - `password_hash` môže byť prázdny reťazec → pri prvom prihlásení je používateľ vyzvaný nastaviť heslo.
  - **Reset password** (forgot password) = vymazanie `password_hash` v DB úplne → pri ďalšom prihlásení používateľ vloží nové heslo. Žiadny email server.
  - Session cookie pre web UI; žiadny JWT pre agenta (API token `X-Api-Token`).
- **AK**: Neprihlásený návštevník vidí live view a analýzy; admin sa prihlási cez `#/admin` a má settings/flag/export. Používateľ s prázdnym heslom je vyzvaný nastaviť ho; reset hesla vymaže hash úplne.

### FR-13 Retention a cleanup

- **Popis**: Lokálne video sa maže **hneď** pri zistení nekonzistencie s NVR (originál už nie je dostupný), okrem segmentov označených persist. SQLite záznamy (recording metadata + detekcie) sa mažú **po mesiaci** od označenia za nedostupný.
- **Detaily**:
  - Persist/zachovaj: segment drží lokálne, kým používateľ persist neodznačí; po odznačení platia štandardné pravidlá. DB záznam persisted segmentu sa tiež maže po mesiaci od označenia nedostupnosti.
  - Dočasné výsledky požiadaviek sa cacheujú (2–3 hodiny, konfigurovateľné) a potom čistia.
  - Download/konverzia: žiadne dlhodobé duplicity.
  - **Implementované (S4-5/S22e)**: Purge job vytvára AutoPlanner 1×/hod (vlastný maintenance slot, neprerušovaný preempciou); retention 30 dní; overené reálne (clips=881, files=926 purged).
- **AK**: Segment zmazaný z NVR zmizne z lokálneho disku do hodiny od synchronizácie (ak nie je persisted); z DB po mesiaci. Persisted segment prežije NVR purge a zmizne až po odznačení.

### FR-14 Media delivery (agent)

- **Popis**: Žiadne verejné URL/stream linky cez Tailscale/funnel (bezpečnostné riziko). Doručenie je jednotné pre všetky chat platformy: **agent stiahne klip a pošle ho ako video súbor**; obrázok sa posiela **vždy s vizualizáciou detekcie** (obdĺžnik okolo objektu).
- **Detaily**:
  - Default odpoveď agenta: **fotka**; video na explicitné vyžiadanie (skill môže video navrhnúť).
  - Klip okolo udalosti: default **15 s pred + 15 s po**; prispôsobiteľné požiadavkou (napr. „30 s pred a 30 po“).
- **AK**: Agent na požiadavku „pošli mi to“ doručí fotku s detekčným obdĺžnikom; na „pošli video“ doručí video súbor s 15+15 s kontextom.

### FR-15 Interaktívne query (agent/web)

- **Popis**: Overenie požiadavky (dátum/čas), vyhľadanie detekcií v SQLite, identifikácia intervalov udalostí, prioritné stiahnutie potrebných úsekov, analýza ak ešte nebola, doručenie výsledku, cleanup.
- **Detaily**: Ak požiadavka vyžaduje nespracované nahrávky, používateľ dostane feedback + odhad času. Event-first: výsledok = označené detekčné úseky (max pár minút), nie hodiny videa.
- **AK**: Query „pohyb na kamere 3 od 15:00 do 16:00“ vráti zoznam udalostí s časmi a možnosťou doručiť média.

### FR-16 Flag, false positive a anotácie (potvrdené)

- **Popis**: Používateľ môže v UI označiť udalosť ako **Flag** (významná, pre zdieľanie/následné kroky) alebo ako **False positive** (falošný poplach). Na flag screen môže vytvárať vlastné **anotácie** (užívateľské kreslené boxy/labely nad systémovými detekciami).
- **Detaily**:
  - Flag/false positive feedback sa ukladá k detekcii v SQLite (kto, kedy, aký stav) a **algoritmus s ním musí do budúcna počítať** — slúži na lepšiu analýzu falošných poplachov v konkrétnych oblastiach (napr. tieniace zóny, stromy).
  - Anotácie: užívateľské boxy (zelené „USER ·") sa ukladajú oddelene od systémových detekcií (červené „SYSTEM ·"); „Clear my drawings" maže len užívateľské anotácie.
  - Dashboard filter chipy zahŕňajú **Flagged** filter (zobraziť len označené udalosti).
  - Flag súvisí s persist značkou (FR-13), ale je to samostatná akcia na úrovni udalosti.
- **AK**: Používateľ označí udalosť ako false positive; stav sa uloží k detekcii a je viditeľný v query. Užívateľská anotácia (box s textom) sa uloží oddelene od systémovej detekcie a prežije reload stránky. Flagged filter vráti len označené udalosti.

---

## 2. Non-Functional Requirements (NFR)

### NFR-01 Robustnosť (najvyššia priorita)

- **Popis**: Systém musí byť odolný voči okrajovým prípadom — výpadok elektriny, reštart laptopu, výpadok NVR, výpadok siete. Nesmie „spadnúť ako domček z karát“.
- **AK**: Po hard reštarte počas downloadu aj analýzy sa systém obnoví: commitnuté SQLite dáta sú nedotknuté, prerušené joby sa preplánujú, čiastočné súbory sa vyčistia.

### NFR-02 Objem dát a kapacita

- **Popis**: Pri 8 kamerách ≈ 5.2 GB/hod nových záznamov (bez konverzií); ~20 GB/hod ak by sa všetko konvertovalo (to sa nerobí). Disk 377 GB voľných — systém nesmie archivovať video, len krátkodobo spracovávať.
- **AK**: Systém udrží stabilný stav pri 24/7 behu s 8 kamerami bez rastu disku (okrem SQLite) — lokálne video existuje len počas spracovania a cache okna.

### NFR-03 Výkon a paralelizmus

- **Popis**: **MaxParallelAnalyses = 4** + **MaxParallelDownloads = 4** (S16, defaults prepísateľné cez env). Analýza beží na 1080p downscale; produkcia je CPU-only (Farneback ~616 ms/frame @1080p — pozri `docs/real-benchmark.md`).
- **AK**: Pri default konfigurácii nezahltí systém CPU/RAM tak, že by ovplyvnil ostatné služby (Hermes, Hindsight, Firefox) — merateľné cez load average.

### NFR-04 GPU/CPU backend

- **Popis**: **Produkčná cesta je CPU-only** (rozhodnutie S8-1/S9-1, 2026-08-08): `OpenCvSharp4.Cuda.runtime.linux-x64` (Combined) obsahuje len sm_100 (Blackwell) cubiny → Quadro M2200 (Maxwell SM 5.0) **nie je podporovaný**; navyše na hoste chýbajú CUDA 12.8 runtime libs (len driver libcuda.so.1). GPU cesta je gated na stroj s Turing+ GPU alebo vlastný OpenCV build (vyžaduje nvcc — nie je nainštalovaný). CPU fallback (Farneback + HOG) je plnohodnotný a overený.
- **AK**: Systém beží CPU-only s rovnakými detekčnými výsledkami (výkon sa líši, výsledok nie).

### NFR-05 Bezpečnosť

- **Popis**: Žiadne verejne vystavené služby (žiadny Tailscale funnel). **Verejné UI (S22c)**: live view + analýzy sú zámerne verejné (rodinné sledovanie), ale **write operácie a konfigurácia sú admin-only**. NVR prihlasovacie údaje v konfigurácii (nie v kóde). Prístup cez Tailscale serve (nie verejné porty).
- **AK**: Neprihlásený návštevník vidí live view a analýzy, ale nemôže meniť konfiguráciu, flagovať, persistovať ani exportovať. Žiadny endpoint nie je verejne dostupný mimo lokálnej siete/Tailscale.

### NFR-06 Deployment

- **Popis**: 4 komponenty: (1) Runner, (2) REST API, (3) web UI (Angular), (4) MCP server. **Produkcia (2026-08-12+) beží ako systemd user služby** (`watchforge-runner.service` :8081, `watchforge-api.service` :5000, `watchforge-web.service` :4200, `watchforge-mcp.service` :8770) — nie kontajnery; Dockerfile/compose/Quadlet zostávajú ako build variant a pre E2E s fake NVR (`deploy/scripts/e2e.sh`). Zdieľané volume: SQLite (WAL) + dočasná media cache. Žiadne súborové handoff medzi kontajnermi pre spracovanie.
- **AK**: systemd user služby zdvihnú všetky 4 komponenty; service beží nonstop; reštart nepoškodí dáta; backup timer (04:00, VACUUM INTO, 14 dní) beží.

### NFR-07 Technologický stack

- **Popis**: Backend .NET 10/C#; Web UI Angular; frontend toolchain preferovaný Bun.js (na lokálny host zatiaľ nie je — inštalácia súčasťou setup fázy); databáza SQLite; video/NVR: DVRIP, ffmpeg, OpenCV; deployment rootless Podman; libraries testované (TUnit) a pripravené na NuGet publikovanie.
- **AK**: Build a testy prejdú na lokálny host s .NET 10 SDK; web build prejde s Bun (alebo npm fallback).

### NFR-08 Lokalizácia a UX

- **Popis**: Web UI slovensky (default) + anglicky (backup), texty externalizované. Event-first UX pre agenta aj UI.
- **AK**: Prepnutie jazyka v UI funguje bez zmeny logiky (konzistentné s yay_see_sharp prístupom).

---

## 3. Mapovanie na komponenty (náčrt pre architektúru)

| Kontajner | Zodpovednosť | Kľúčové FR |
|---|---|---|
| Processing service | DVRIP download, detekčný pipeline (motion→person→face), job orchestration, SQLite zápisy, cleanup, media cache produkcia | FR-01..FR-08, FR-13 |
| REST API | Jediné systémové rozhranie: query, job trigger, media delivery, konfigurácia, auth | FR-09, FR-11..FR-15 |
| Web UI (Angular) | Prehliadanie, prehrávanie, timeline, konfigurácia profilov, persist, SK/EN | FR-11, FR-12, FR-13 |
| MCP server | Mapuje REST API na MCP nástroje pre agenta | FR-10 |

## 4. Otvorené položky — stav (2026-08-15)

Všetky pôvodné otvorené položky sú **vyriešené implementáciou**:

- ~~Presný dátový model~~ → SQLite schema 13 tabuliek, WAL, FK, indexy (S2-2); DB verziovaná (`PRAGMA user_version`).
- ~~GPU cesta: NuGet CUDA vs vlastný build~~ → **CPU-only produkcia** (S8-1/S9-1; GPU nemožné na M2200 — sm_100 vs Maxwell).
- ~~Benchmark paralelizmu~~ → S13 (real-bench) + S15 (parallel download) + S16 (4+4 sloty): `docs/real-benchmark.md`, `docs/parallel-download-benchmark.md`, `docs/cpu-benchmark.md`.
- ~~Web UI prehrávanie: DVRIP stream vs fallback~~ → **live view implementovaný** (S19/S22b–S22j) cez DVRIP OPMonitor + LiveFrameCache; RTSP je na NVR jednokanálový (S18 analýza).
- ~~Face recognition: AI/ML model~~ → **YuNet + SFace ONNX** (S17) — OpenCV DNN modely (MIT), nie ML tréning.
- ~~Bun.js inštalácia~~ → Bun toolchain v CI aj lokálne (S6/S14).
- ~~SQLite backup stratégia~~ → denný `VACUUM INTO` + WAL checkpoint TRUNCATE, rotácia 14 dní (S9-3).
- **Live audio z NVR** → **nie je možné** (S22i, 2026-08-14): live monitor streamy nemajú audio; download stream má G711A alaw so 15-min oneskorením.
- **Druhá NVR jednotka** → 💡 IDEA (S22), nie je isté, či bude — závisí od nákupu WiFi kamier; multi-NVR stĺpce v DB sú pripravené (YAGNI).
