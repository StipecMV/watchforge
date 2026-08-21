# WatchForge CPU benchmark (2026-08-08)

Merané na: lokálnom hoste (8 cores, CPU-only produkčná cesta), nástroj
`tools/cpu-benchmark` (syntetické videá cez ffmpeg, Release build, 25–30 framov/m).

> ⚠️ **Korekcia S8-1**: pôvodný benchmark „27 ms/frame @1080p ≈ 36 fps" meral na
> **320×240 framoch** (FrameWithRect), NIE na 1080p. Na 320×240 je Farneback 0.4–0.5 ms/f.
> Realita na 1080p je o dva rády iná — pozri nižšie.

## 1. Baseline (syntetické testsrc2 — extrémne textúrovaný worst-case)

| Rozlíšenie | Farneback | HOG | Combo (FB→HOG) |
|---|---|---|---|
| 720p (1280×720) | 270–281 ms/f (4 fps) | 116 ms/f (9 fps) | 386 ms/f (3 fps) |
| 1080p (1920×1080) | 614–623 ms/f (2 fps) | 281–283 ms/f (4 fps) | 922–937 ms/f (1 fps) |
| 1440p (2560×1440) | 1188–1212 ms/f (1 fps) | 526–532 ms/f (2 fps) | 1741–1763 ms/f (1 fps) |

## 2. Bottleneck analýza (1080p)

| Fáza | Čas | Poznámka |
|---|---|---|
| decode (ffmpeg/VideoCapture) | 5.9–8.5 ms/f (118–171 fps) | **Nie je bottleneck** |
| Farneback čistý (testsrc2) | ~619 ms/f | worst-case textúry |
| Farneback čistý (realistické video) | **5.2 ms/f (193 fps)** | sivá plocha + pohyb boxu |
| HOG v combo (realistické) | ~553 ms/f | **BOTTLENECK na 1080p** |

**Záver bottleneck**: na realistickom kamera-like videu je Farneback lacný (5 ms/f),
**HOG person detekcia je dominantná fáza** (~280 ms/f na testsrc2, ~550 ms/f v combo na
realistickom) — je to O(n×window) algoritmus, na 1080p prechádza ~500k okien.
Decode a preprocess nie sú problém (≤9 ms/f).

## 3. Kalibrácia vs S8-1

| Rozlíšenie | Farneback | fps |
|---|---|---|
| 320×240 (S8-1 „1080p benchmark") | 0.4–0.5 ms/f | 2209–2786 fps |
| 1080p (reálne) | 614 ms/f (testsrc2) / 5 ms/f (realistické) | 2 / 193 fps |

→ **S8-1 „36 fps @1080p" neplatí pre 1080p.** Odporúčanie: benchmark vždy uvádzať
aj rozlíšenie framu; S8-1 meranie prerobiť na 1080p.

## 4. Škálovateľnosť (1080p, combo pipeline, 25 f/analýza)

| Paralelné analýzy | Wall time | CPU utilizácia | Poznámka |
|---|---|---|---|
| 1 | 20.5–25.4 s | 40–41 % | 1 jadro ~saturácia |
| 2 | 30.4–38.1 s | 53–56 % | ~1.5× wall vs 1 |
| 4 | 43.2–53.6 s | 77–80 % | ~2.1× wall vs 1 |

→ **CPU sa škáluje sublineárne** (4 analýzy ≈ 2× wall času 1 analýzy, nie 4×), CPU
utilizácia rastie (41→80 %), takže stroj zvládne 4 paralelné streamy bez degradujúceho
thrashovania — ale wall čas na analýzu rastie. Pre ≥25 fps by potreboval ~4× rýchlejší
jednojadrový výkon na HOG.

## 5. 4K vstup (3840×2160) — downscale kompromis

| maxWidth | Source rozlíšenie | Farneback+decode | fps |
|---|---|---|---|
| 1920 (1080p) | 1920×1080 | 701–764 ms/f | 1 |
| 1280 (720p) | 1280×720 | 375 ms/f | 3 |
| bez downscalu (4K raw) | 3840×2160 | 2920–2974 ms/f | 0.3 |

→ **4K raw je nepoužiteľné na CPU** (3 s/frame). Downscale na 720p je 4× lacnejší ako
1080p. Pretože detekčné regióny sú normalizované 0..1 (platia pre 4K), downscale na
720p je rozumný kompromis pre CPU-only (ak HOG nebeží na každom frami).

## 6. Závery pre produkčné nastavenie

1. **Farneback nie je problém** na realistickom videu (5 ms/f @1080p) — môže bežať na
   1080p alebo aj 1440p.
2. **HOG je bottleneck** — bežať ho NIE na každom frami, len na motion framoch
   (súčasná pipeline to už robí: person len pri motion) a ideálne na downscale (720p)
   alebo s väčším WinStride / menším Scale.
3. **4K vstup**: downscale na 720p pre CPU-only (4× lacnejšie) alebo 1080p, ak je
   potrebná presnosť HOG.
4. **Paralelizmus**: 2–4 analýzy sú OK (CPU 53–80 %), produkčný default 2 je
   konzervatívny a správny pre 15-min segmenty (nie realtime).
5. **Latencia end-to-end** (decode→Farneback→HOG): ~560–930 ms/frame na 1080p
   (realistické→worst-case) — pre event klipy (2 fps vzorkovanie) = ~1.1–1.9 s na
   analyzovaný frame.

## 7. Podmienky pre GPU investíciu (odpoveď research agentovi)

| Podmienka | Stav |
|---|---|
| CPU bottleneck potvrdený meraniami? | ✅ ÁNO pre HOG (~280–550 ms/f @1080p); Farneback samotný NIE je bottleneck na realistickom videu |
| Existuje NuGet/predkompilovaný build pre SM 8.6/8.9? | ⚠️ Nie je overené — aktuálny balík 1.0.8 má len sm_100; nutné overiť `strings` pred nákupom |
| Aký výkonnostný cieľ nie je na CPU dosiahnuteľný? | Realtime ≥25 fps HOG na 1080p (~4× rýchlejší jednojadrový výkon); 4K raw analýza (3 s/f na CPU) |

→ GPU nákup (Turing+/RTX 30/40) dáva zmysel **len ak** benchmark na reálnom NVR
zázname potvrdí, že HOG na 1080p reálne brzdí produkčný throughput (S13-3 merania
na 6×15 min záznamoch) — a ak existuje balík s podporou cieľovej SM architektúry.

## Ako zopakovať

```bash
cd watchforge
dotnet run --project tools/cpu-benchmark --configuration Release -- --frames 30
```
