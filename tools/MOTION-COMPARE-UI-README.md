# S22p — Porovnávacia appka detekcie pohybu (samostatná)

Samostatná webová appka na **A/B porovnanie 4 detektorov pohybu** na jednom videu,
s možnosťou označiť nálezy (šum / nález / manuálny nález) a vytvoriť **ground-truth
CSV** pre vyhodnotenie precision/recall.

## Algoritmy a farby (na videu)

| Farba | Algoritmus | Rodina | Pozn. |
|---|---|---|---|
| 🔵 Modrá | Farneback | optical flow | pôvodný default |
| 🔴 Červená | MOG2 | background subtraction | S22o, rýchly, robustný na tiene |
| 🟡 Žltá | KNN | background subtraction | S22p |
| 🟢 Zelená | AbsDiff | frame differencing | S22p, najrýchlejšia 2. rodina |
| 🟠 Oranžová | manuálny nález | — | nakreslíš rukou, keď ani jeden nenašiel |

> **Prečo AbsDiff a nie DIS?** DIS (Dense Inverse Search) nie je v OpenCvSharp 4.13
> wrapperovaný. Náhrada TV-L1 (`DenseOpticalFlowExt.CreateDualTVL1`) **segfaultuje**
> (signal 11) na tomto prostredí pri `Calc` na akomkoľvek rozlíšení (overené 320×240
> aj 1080p). Preto 4. algoritmus = **AbsDiff (frame differencing)** — rýchla, stabilná
> druhá rodina detekcie.

## Ako spustiť

1. **Vygenerovať detekcie** (na svojom videu):
   ```
   dotnet run --project tools/motion-export -- "video.mp4" "out.json" 1000
   # intervalMs default 1000 (1 fps); 500 = 2 fps
   ```
2. **Otvoriť appku** v prehliadači:
   ```
   # buď priamo (file://) — podpora file inputu je natívna:
   firefox tools/motion-compare-ui.html
   # alebo cez http server (ak by file:// blokoval):
   python3 -m http.server 8000 --directory tools/  # → http://localhost:8000/motion-compare-ui.html
   ```
3. V appke:
   - **Načítať JSON detekcií** (tlačidlo hore) → vygenerovaný `out.json`
   - **Načítať video** (rovnaký súbor ako bol do exportu)
   - Prehrávaj / seekuj → na ploche sa kreslia obdĺžniky podľa farby algoritmu
   - Checkboxy dole zapínajú/vypínajú jednotlivé algoritmy

## Označovanie nálezov

| Tlačidlo | Význam |
|---|---|
| 🔴 Šum (false positive) | algoritmus hlásil, ale nie je to nález |
| 🟢 Nález (true positive) | správne hlásený nález |
| 🟠 Nález — algoritmy nenašli | zapne kreslenie → ťahaním myšou/prstom nakreslíš **oranžový obdĺžnik** (manuálny ground-truth) |

Každé označenie sa viaže na **aktuálny frame** (podľa časovej pozície videa).
**⬇ Export CSV** vytvorí súbor `watchforge-ab-labels.csv`:
```
frameIdx,timestampMs,oznacenie,manualX,manualY,manualW,manualH
```

## Čo z toho vyčítaš

CSV + percento framov s detekciou per algoritmus (z `out.json`) dajú:
- **precision** = nálezy správne / všetky hlásené
- **recall** = nálezy / (nálezy + oranžové manuálne)
- ktorý algoritmus má najlepší pomer na reálnej scéne (denná / nočná / tiene)

## Prepnutie algoritmu v hlavnom projekte (bod 5 zadania)

Detektor v `AnalyzeJobHandler` sa prepína **jednou hodnotou configu** —
`DetectionOptions.Algorithm` (enum `MotionAlgorithm`). Factory `MotionDetectorFactory`
vráti príslušný detektor; pri zmene vybraného algoritmu z appky stačí zmeniť túto
hodnotu (napr. cez env/config) a hlavné UI ho začne používať. Žiadna ďalšia zmena
volajúceho kódu netreba.

## Súbory
- `tools/motion-export/` — nástroj: video → JSON detekcií (4 algoritmy)
- `tools/motion-compare-ui.html` — samostatná appka (browser, bez build)
- `tools/mog2-bench/` — benchmark: 4 detektory vs N videí (čas/snímok, zrýchlenie)
