# WatchForge — GPU analýza: prečo CUDA nejde použiť (2026-08-08)

> Dokument pre research / rozhodnutie. Zistenia sú z reálneho overenia na stroji
> (nie teória). Vetva `watchforge-redesign`, súvisiace: S8-1, S9-1, Report rozhodnutí.

## Záver (jedna veta)

**Na tomto stroji (lokálny host, Quadro M2200) nie je GPU akcelerácia OpenCV možná
s aktuálnym NuGet balíkom — a to ani po doinštalovaní CUDA runtime knižníc.**
Problém je v architektúre GPU vs. binárky, nie v konfigurácii.

## Hardvér a softvér (overené)

| Komponent | Hodnota | Ako overené |
|---|---|---|
| GPU | NVIDIA Quadro M2200 (Maxwell, SM 5.2, 2015) | `nvidia-smi` |
| VRAM | 4 GB | `nvidia-smi` |
| Driver | 580.173.02 (CUDA 13.0 driver-level) | `nvidia-smi` |
| OpenCV .NET | OpenCvSharp4 4.13.0.20260627 | csproj |
| CUDA balík | OpenCvSharp4.Cuda 1.0.4 + runtime.linux-x64 1.0.8 | Directory.Packages.props |
| CUDA build balíka | CUDA Toolkit 12.8 (podľa README balíka) | README.cuda.md |

## Zistenia (fakty + príkazy)

### 1. Balík obsahuje LEN cubiny pre Blackwell (SM 10.0)

```bash
$ strings ~/.nuget/packages/opencvsharp4.cuda.runtime.linux-x64/1.0.8/native/libOpenCvSharpExtern.so \
  | grep -oE "sm_[0-9]+|compute_[0-9]+" | sort | uniq -c | sort -rn
    300 sm_100
    151 compute_100
      4 compute_1
```

- **Žiadne `sm_50/52` (Maxwell), `sm_75` (Turing), `sm_80/86` (Ampere), `sm_89` (Ada)**.
- `sm_100` = Blackwell (RTX 50-series). Balík je fakticky „Blackwell-only" napriek tomu,
  že README tvrdí „Combined = SM 7.5–10.0". To je rozpor README vs. realita — overené
  `strings` na 860 MB .so.
- GPU vie spustiť len cubiny pre svoju architektúru (alebo PTX JIT). M2200 je SM 5.2,
  takže `sm_100` SASS nespustí a PTX pre staršie architektúry v balíku nie je.

### 2. Chýbajú CUDA 12.8 runtime knižnice (nielen driver)

```bash
$ ldd libOpenCvSharpExtern.so | grep "not found"
    libnppc.so.12     => not found
    libnppial.so.12   => not found
    libnppicc.so.12   => not found
    libnppidei.so.12  => not found
    libnppif.so.12    => not found
    libnppig.so.12    => not found
    libnppim.so.12    => not found
    libnppist.so.12   => not found
    libnppisu.so.12   => not found
    libnppitc.so.12   => not found
    ... (ďalšie: libcublas, libcufft, libcudart, ...)
```

- Na hoste je len driver knižnica `libcuda.so.1` (ovládač). OpenCV CUDA moduly sa
  dynamicky linkujú na CUDA 12.8 runtime (NPP, cuBLAS, cuFFT, cuDNN, cudart), ktoré
  tam nie sú (`ldconfig -p` nič nenašiel).
- Overené aj: `find / -name "libcudart*" -o -name "libnpp*"` → prázdne.

### 3. Driver samotný GPU vidí, ale to nestačí

- `nvidia-smi` funguje, GPU je v systéme (P8, 38 °C, 18 % util).
- Driver API (`libcuda.so.1`) je len rozhranie — výpočtové knižnice (runtime) sú zvlášť.

## Prečo to „vôbec" nejde (logická reťaz)

1. OpenCV CUDA build vyžaduje CUDA 12.8 runtime libs → **nie sú nainštalované**.
2. Aj keby sa doinštalovali, `libOpenCvSharpExtern.so` obsahuje len `sm_100` cubiny →
   **M2200 (SM 5.2) ich fyzicky nespustí** (inštrukčná sada pre inú architektúru).
3. Jediná cesta, ako to obísť, je binárka skompilovaná pre Maxwell (SM 5.0/5.2) —
   čiže vlastný OpenCV build, nie NuGet balík.

## Čo by to vyžadovalo (možnosti, ak by sme GPU chceli)

| Možnosť | Náročnosť | Poznámka |
|---|---|---|
| **A. Vlastný OpenCV build pre Maxwell** | Vysoká | Vyžaduje CUDA Toolkit + nvcc (nie je nainštalovaný), kompilácia OpenCV 4.13 + contrib (moduly), hodiny build času, riziko nestability |
| **B. GPU s Turing+ (SM 7.5+)** | Stredná (nákup) | RTX 20-series a novšie by zvládli `sm_75+` cubiny — ale NuGet 1.0.8 má fakticky len sm_100, takže by aj tak bolo treba iný balík/build |
| **C. CPU-only (súčasný stav)** | Nízka | **Produkčná cesta**: Farneback + HOG na 1080p, ~27 ms/frame = ~36 fps (merané), 2 paralelné analýzy default |

## Odporúčanie

- **Ostať na CPU-only** ako produkčnej ceste (S8-1/S9-1 rozhodnutie, plnohodnotné).
- GPU znovu riešiť až ak príde stroj s Turing+ GPU **a** balík OpenCvSharp4.Cuda
  s podporou `sm_75+` (overiť `strings` pred nákupom — README „Combined" klame).
- Prípadný vlastný Maxwell build zdokumentovať ako samostatný šprint (odhliadnuc od
  toho, že M2200 má len 4 GB VRAM a 4K analýza by sa do nej aj tak nezmestila — downscale
  na 1080p je nutný bez ohľadu na backend).

## Ako overiť budúci balík (checklist pred nákupom GPU)

```bash
# 1. Stiahnuť balík a skontrolovať SM cubiny
strings ~/.nuget/packages/opencvsharp4.cuda.runtime.linux-x64/<ver>/native/libOpenCvSharpExtern.so \
  | grep -oE "sm_[0-9]+" | sort -u
# 2. Skontrolovať runtime závislosti
ldd <cesta>/libOpenCvSharpExtern.so | grep "not found"
# 3. Porovnať s architektúrou GPU (nvidia-smi --query-gpu=compute_cap)
nvidia-smi --query-gpu=compute_cap --format=csv
```
