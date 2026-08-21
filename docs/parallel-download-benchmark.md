# WatchForge — Benchmark paralelného downloadu z NVR (S15)

Merané: 2026-08-09, reálny NVR (lokálna lokalita, <nvr-ip>), nástroj `tools/nvr-dl-parallel`.

## Metodika

**v2 (čisté meranie):** rovnakých **16×15-min segmentov** (6 kamier, najnovšie plné
segmenty ≥300 MB, ~7.7 GB spolu) sa stiahlo pre KAŽDÚ konfiguráciu paralelizmu
(4, 6, 8, 10, 12, 14, 16). Časy sú priamo porovnateľné — rovnaké dáta, len iný
počet súbežných streamov.

**v1 (predošlé, nepriame):** rôzne segmenty (p1 = 15-min, p2–p8 = 20–250 MB) —
časy NIE sú porovnateľné naprieč configmi; per-stream rýchlosť áno.

## Výsledky v2 (rovnakých 16×15-min, ~7672 MB na konfiguráciu)

| Paralelizmus | Celkový čas (s) | Agregovaná rýchlosť (Mbit/s) | Per stream (Mbit/s) |
|---|---|---|---|
| 4 | 3013.4 | 20 | 5 |
| 6 | 1999.2 | 31 | 5 |
| 8 | 1536.4 | 40 | 5 |
| 10 | 1374.7 | 45 | 4 |
| 12 | 1384.3 | 44 | 4 |
| 14 | 1125.3 | 55 | 4 |
| 16 | 993.6 | 62 | 4 |

## Porovnanie v1 vs v2

| Paralelizmus | v1 agregovane (Mbit/s) | v2 agregovane (Mbit/s) | v1 per stream | v2 per stream |
|---|---|---|---|---|
| 4 | — | 20 | — | 5 |
| 6 | 29 | 31 | 6 | 5 |
| 8 | 29 | 40 | 6 | 5 |
| 10 | — | 45 | — | 4 |
| 12 | — | 44 | — | 4 |
| 14 | — | 55 | — | 4 |
| 16 | — | 62 | — | 4 |

## Interpretácia

1. **Agregovaná rýchlosť rastie s paralelizmom takmer lineárne** (20 → 62 Mbit/s
   pre 4 → 16 streamov). **NVR neškrtí agregovanú šírku** ani pri 16 súbežných
   downloadoch (v1 naznačoval saturáciu ~30 Mbit/s, ale to bol artefakt rôznych
   segmentov).
2. **Per stream klesá len mierne** (5 → 4 Mbit/s) — strata efektivity pri vysokej
   paralelizácii je malá.
3. **Zrýchlenie 4→16: 3.03×** (3013 s → 994 s) pri 4× paralelizme.
4. 16×15-min segmentov (7.7 GB) pri paralelizme 16 = **16.6 min**; pri 4 = 50.2 min.

## Odporúčanie pre S16 (paralelné downloady v Runneri)

- **MaxParallelDownloads = 4–6** je bezpečný default (20–31 Mbit/s agregovane,
  5 Mbit/s per stream, žiadna degradácia) — produkčné 6×15-min (≈2.7 GB) stiahne
  za **~8–12 min** namiesto ~50 min sekvenčne.
- 6–8 streamov dáva 31–40 Mbit/s — vhodné, ak NVR kapacita a sieť dovolia;
  per-stream ešte drží 5 Mbit/s.
- Nad 8 streamov per stream klesá na 4 Mbit/s — efektivita klesá, ale agregovane
  stále rastie (16 → 62 Mbit/s). Vhodné len pre hromadné backfilly.

## Reprodukovateľnosť

```bash
dotnet build tools/nvr-dl-parallel/nvr-dl-parallel.csproj --configuration Release
tools/nvr-dl-parallel/bin/Release/net10.0/nvr-dl-parallel 4,6,8,10,12,14,16
```
