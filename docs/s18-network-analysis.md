# WatchForge S18 — Sieťová analýza: 8 kamier naraz (2026-08-09/10)

Merané na: lokálny host ↔ reálny NVR (lokálna lokalita, <nvr-ip>), nočný beh.

## Záver (TL;DR)

**Áno, sieť potiahne stream z 8 kamier naraz — s ~25× rezervou.**
Limitom NIE je sieť, ale NVR/CPU strana. A pozor: RTSP na tomto NVR
ignoruje číslo kanála — multi-kanál je možný IBA cez DVRIP protokol.

## 1. Sieťová infraštruktúra (merané)

| Parameter | Hodnota | Metóda |
|---|---|---|
| Laptop NIC | **1 Gbit/s** | `/sys/class/net/enp0s31f6/speed` |
| Latencia NVR | 3.2 ms, 0% loss | ping |
| RTSP 554 | otvorený | TCP connect |
| DVRIP 34567 | otvorený | TCP connect |

## 2. RTSP test — NVR ignoruje číslo kanála (nález!)

Všetky testované formáty RTSP URL vracajú ROVNAKÝ obraz:

| URL forma | Výsledok |
|---|---|
| `Streaming/Channels/101…115` | 4K HEVC, ale **rovnaká scéna** |
| `/1`, `/2` | 4K HEVC, rovnaká scéna |
| `ch0_0.uhd`, `ch1_0.uhd` | 4K HEVC, rovnaká scéna |
| 8 súbežných RTSP | 8× ten istý stream (~1.1 Mbit/s each) |

Dôkaz: priemerný jas framu = 75.32–75.34 pre všetky „kanály"
(pixelová odchýlka 0.3 → identický obraz). Predošlé rozdiely md5
boli len pohyb v rámci tej istej scény.

**RTSP na Movols/Xiongmai NVR nie je multi-kanálový prístupový bod.**

## 3. DVRIP (produkčný protokol) — funguje, merané

| Meranie | Výsledok |
|---|---|
| 16 paralelných downloadov (S15 v2) | **62 Mbit/s agregovane** |
| Per stream @ 8 | **~4–5 Mbit/s** (4K HEVC) |
| Kanály | 6 kamier, rôzne segmenty (473/361/456/722 MB…) |

## 4. Bilancia pre 8 kamier

```
Požiadavka:     8 × 5 Mbit/s = 40 Mbit/s
Kapacita NIC:   1000 Mbit/s   (1 Gbit)
Využitie:       ~4 %          (rezerva ~25×)
NVR schopnosť:  62 Mbit/s @16 streamov ≥ 40 Mbit/s @8
```

## 5. Odporúčania pre S18 (DVRIP streaming cez API)

1. **Streamovať cez DVRIP** (DvripClient), NIE RTSP — RTSP je tu jednokanálový.
2. **Bitrate per stream ~4–5 Mbit/s** (4K HEVC) → API transkód na H.264
   (želané v FR-11) je vhodný, ak UI vyžaduje nižšie rozlíšenie.
3. **NVR zvládne 8 súbežných** (62 ≥ 40 Mbit/s) — bez úprav.
4. **CPU laptopu**: 8× 4K decode = ~8 jadier → pre „živý náhľad 8 kamier"
   odporúčame sub-stream/downscale (720p), nie 4K decode 8×.
5. Sieť nie je úzke hrdlo — CPU decode a NVR session limit áno.
