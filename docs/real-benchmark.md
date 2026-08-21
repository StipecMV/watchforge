# WatchForge REÁLNY benchmark (S13-3) — 2026-08-08

Merané na: lokálnom hoste (8 cores, CPU-only produkčná cesta), nástroj `tools/real-bench`.
Dáta: **15×15 min segmentov stiahnutých z reálneho NVR (lokálna lokalita, <nvr-ip>)** —
6 kamier, 4K HEVC (3840×2160), ~900 s/segment, ~7 Mbit/s download rýchlosť.
Segmenty v `/tmp/nvr-bench` (raw HEVC → MP4 stream copy, validované ffprobe).

## 1. Baseline (1080p downscale, reálne dáta)

| Pipeline | ms/frame | fps |
|---|---|---|
| **Farneback** (motion) | **~616–641** | 1.6 |
| **HOG** (person) | **~881–921** | 1.1 |
| **Combo** (Farneback→HOG) | **~895–917** | 1.1 |

> ⚠️ **Rozdiel oproti syntetike**: cpu-benchmark na realistickom syntetickom videu
> (sivá + box) ukázal Farneback 5 ms/f — reálne kamerové zábery (textúry, stromy,
> šum, pohyb) sú **~100× náročnejšie**: Farneback ~616–641 ms/f. testsrc2 worst-case
> (614 ms/f) je teda REALISTICKÝ odhad pre produkciu, nie pesimistický.
> (Dva behy s 25 frames na 15 segmentoch: 616/881/895 ms/f a 641/921/917 ms/f —
> rozdiel je bežecký šum ~4 %.)

## 2. Bottleneck (reálne dáta, 1080p)

| Fáza | ms/frame | fps |
|---|---|---|
| **Decode-only** (HEVC 4K→1080p) | **28–29** | 35 |
| **Farneback čistý** | **~587–613** | 1.7 |
| HOG (len motion framy) | ~265–330 | — |

**Záver: decode NIE je bottleneck (35 fps). Farneback je dominantný (~600 ms/f).**
Produkčná pipeline už reťazí HOG len na motion framoch — HOG ~900 ms/f je len
„horná hranica", reálne beží na menšine framov.

## 3. Škálovateľnosť (paralelné analýzy, 1080p, 25 frames)

| Paralelné analýzy | Wall (N videí) | CPU util | Wall / video |
|---|---|---|---|
| 1 | 17.0 s (1 video) | 30 % | 17.0 s |
| 2 | 24.2 s (2 videá) | 57 % | 12.1 s |
| 4 | 40.2 s (4 videá) | 81 % | 10.1 s |

**Paralelizmus funguje**: pri 4 analýzach klesne čas/video na 10.1 s (vs 17 s
sekvenčne) = 1.7× zrýchlenie, CPU util 81 % (8 jadier vyťažených). 
→ **NFR-03 CPU=2 je konzervatívne; dá sa zvýšiť na 4.**

## 4. 4K downscale kompromis (reálne dáta)

| maxWidth | Rozlíšenie | ms/frame | fps |
|---|---|---|---|
| 1920 | 1080p | **599–608** | 1.6 |
| 1280 | 720p | **265–284** | 3.7 |
| — (4K raw) | 2160p | **2377** | 0.4 |

**720p downscale = 2.1–2.3× rýchlejšie ako 1080p.** 4K raw analýza je nepoužiteľná
(0.4 fps). Downscale na 1080p s metadata mapovaním na 4K súradnice (S4-4 návrh)
je správne produkčné nastavenie; 720p je voľba pre slabší HW.

## 5. Download výkon (NVR → disk, vedľajší nález S13-2)

| Kanál | Veľkosť | Čas | Rýchlosť |
|---|---|---|---|
| CH1 | 473 MB | 610 s | 7 Mbit/s |
| CH2 | 361 MB | 467 s | 6 Mbit/s |
| CH3 | 456 MB | 588 s | 7 Mbit/s |
| CH4 | 783 MB | 1009 s | 7 Mbit/s |
| CH5 | 484 MB | 624 s | 7 Mbit/s |
| CH6 | 437 MB | 563 s | 7 Mbit/s |

~7 Mbit/s per download → **6×15 min segmentov ≈ 60 min** (sekvenčne). Download
nie je prekážkou (beží na pozadí), ale 6 paralelných kanálov by to skrátilo na ~10 min.

## 6. Odporúčané produkčné nastavenie

1. **Analýza 1080p downscale** (maxWidth=1920) — zachované, metadata mapované na 4K
2. **MaxParallelAnalyses = 4** (namiesto 2) — 8 jadier to zvládne, CPU util 74 %
3. **Download paralelizmus** — zvážiť 2-3 paralelné DVRIP downloady (7 Mbit/s each)
4. **GPU investícia** — odporúčaná LEN ak reálna záťaž vyžaduje realtime ≥25 fps
   (CPU: 1.6 fps na 1080p — HOG+Farneback na reálnych dátach); podmienky v `docs/gpu-analysis.md`

## 7. Merania sú reprodukovateľné

```bash
# 1. Stiahnuť segmenty (ak treba znova)
dotnet run --project tools/nvr-bench --configuration Release
# 2. Benchmark na reálnych dátach (25 frames, všetky .mp4 v adresári)
tools/real-bench/bin/Release/net10.0/real-bench /tmp/nvr-bench 25
```
