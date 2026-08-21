# Benchmark detekcie pohybu — 4 algoritmy (S22p, 2026-08-19)

Porovnanie **Farneback / MOG2 / KNN / AbsDiff** na reálnych stiahnutých NVR
nahrávkach (1080p, 1 fps, maxWidth 1920). Merané `tools/mog2-bench`.

## Výsledky (čas/snímok + počet snímok s pohybom)

### Video 16.27.57 (M udalosť, 13 framov)
| Algoritmus | ms/snímok | s pohybom |
|---|---|---|
| Farneback | 587 | 12/13 |
| MOG2 | **81** | 8/13 |
| KNN | 137 | 10/13 |
| AbsDiff | **32** | 12/13 |

### Video 17.00.00 (plná nahrávka CH8, 235 framov)
| Algoritmus | ms/snímok | s pohybom |
|---|---|---|
| Farneback | 638 | 234/235 |
| MOG2 | **93** | 150/235 |
| KNN | 100 | 232/235 |
| AbsDiff | **67** | 222/235 |

### Video 19.16.13 (M, 21 framov)
| Algoritmus | ms/snímok | s pohybom |
|---|---|---|
| Farneback | 599 | 20/21 |
| MOG2 | **99** | 16/21 |
| KNN | 117 | 18/21 |
| AbsDiff | **79** | 20/21 |

### Video 20.05.43 (M, 32 framov)
| Algoritmus | ms/snímok | s pohybom |
|---|---|---|
| Farneback | 560 | 31/32 |
| MOG2 | **37** | 16/32 |
| KNN | 48 | 29/32 |
| AbsDiff | **18** | 28/32 |

## Závery

1. **Všetky 3 alternatívy sú výrazne rýchlejšie ako Farneback** (~600 ms/f):
   - MOG2: **~5×** (37–99 ms/f)
   - KNN: **~5–12×** (48–137 ms/f)
   - AbsDiff: **~8–30×** (18–79 ms/f) — najrýchlejší

2. **Filtrovanie falošných pozitív (menej šumu + menej HOG práca):**
   - **MOG2 najsilnejšie filtruje**: 16/32 vs Farneback 31/32 na 20.05; 150/235 vs 234/235 na 17.00
   - KNN citlivejší (blízko Farnebacku: 232/235, 29/32)
   - AbsDiff veľmi citlivý (222/235, 28/32 — len o málo menej ako Farneback)

3. **Interpretácia pre výber do hlavného UI:**
   - **MOG2 = najlepší pomer rýchlosť/čistota** (robustný, málo FP, ~5× rýchlejší) — potvrdzuje predošlý A/B (14 videí)
   - KNN = ak chceš citlivosť medzi MOG2 a Farneback, ~5× rýchlejší
   - AbsDiff = najrýchlejší, ale najviac FP — vhodný skôr ako rýchla referenčná/pre-check metóda

4. **DIS / TV-L1 nie sú dostupné:** DIS nie je v OpenCvSharp 4.13; TV-L1 (`CreateDualTVL1`) segfaultuje natively — nahradené AbsDiff (frame differencing, druhá rodina). GPU TV-L1 neprichádza do úvahy (Quadro M2200 nie je podporovaný).

## Odporúčanie

**V hlavnom projekte prepnúť default na MOG2** (`DetectionOptions.Algorithm = MotionAlgorithm.Mog2`),
Farneback ako fallback (cez factory). KNN nechať ako konfigurovateľnú citlivejšiu voľbu.
AbsDiff ako najrýchlejšiu referenciu/pre-check.

Detailnejšie precision/recall dá samostatná porovnávacia appka
(`tools/motion-compare-ui.html` + `tools/motion-export`).
