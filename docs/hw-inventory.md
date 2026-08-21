# WatchForge — HW inventár NVR + kamery (2026-08-10)

Zdroj: DVRIP priame dotazy (GetInfo/SystemInfo, StorageInfo, WorkState — msg 1020),
RTSP ffprobe, web rozhranie, oficiálny datasheet Xiongmai (NBD80S16S-KL.pdf).

## 1. NVR — zariadenie

| Parameter | Hodnota |
|---|---|
| **Model** | **NBD80S16S-KL** (Xiongmai/Movols 16ch 4K H.265 NVR board) |
| Typ (DeviceType) | **HVR** (Hybrid Video Recorder), ChannelNum=9 |
| Firmware | **V4.03.R11.C6380233.12201.140000.0000001** (build 2024-06-17) |
| Serial | `<serial>` |
| PID | `<pid>` |
| HW verzia | „Unknown" (board bez verzie) |
| Uptime | 0x0019D516 ≈ 1 693 974 min ≈ 3.2 roka |
| DigChannel | 9 digitálnych kanálov (6 aktívnych) |

## 2. Disk (StorageInfo — dôležité pre výmenu)

| Parameter | Hodnota |
|---|---|
| Počet diskov | **1** (PlysicalNo 0, PartNumber 1) |
| **Celková kapacita** | **1 907 729 MB ≈ 1.86 TB** (0x001D1C11 × 1 MB) |
| Voľné miesto | **0 MB** — cyklické prepisovanie (disk stále plný) |
| Typ slotu | **1× SATA** (datasheet) |
| **Max podporovaný disk** | **až 14 TB** (datasheet) |
| Záznamy v disku | 2026-07-31 20:28 → 2026-08-10 12:06 (~10 dní) |

→ **Kupovať: 2.5"/3.5" SATA HDD alebo SSD, 2 TB a viac; NVR zvládne až 14 TB.**
   (Board je malý 118×46 mm — pravdepodobne 2.5" slot; overiť pri výmene.)

## 3. Kamery (WorkState — živé bitraty, 2026-08-10 po pripojení 7. kamery; 8. kamera CH8 nainštalovaná 2026-08-12)

| Kanál | Názov (UI, S22d) | Bitrate (K/s) | Mbit/s | Nahráva | Poznámka |
|---|---|---|---|---|---|
| CH1 | **Psia buda (Ares)** | 5297→170 | 5.2→0.2 | ✅ | statická scéna v čase merania |
| CH2 | **Vjazdová brána** | 5966→710 | 6.0→0.7 | ✅ | |
| CH3 | **Chodník (z hora)** | 5182→90 | 5.2→0.1 | ✅ | |
| CH4 | **Záhrada pri fontánke** | 7702→1150 | 7.7→1.2 | ✅ | |
| CH5 | **Hnojisko** | 6282→1320 | 6.3→1.3 | ✅ | |
| CH6 | **Hospodársky dvor** | 4821→1150 | 4.8→1.2 | ✅ | |
| CH7 | **Chodník (priamo)** | **790** | **0.8** | ✅ | **NOVÁ 7. kamera (pripojená 10. 8.)** |
| CH8 | **Záhrada pri dome** | 0→aktívne | — | ✅ od 2026-08-12 | **8. kamera nainštalovaná 12. 8.** |
| CH9 | Kamera 9 (neaktívna) | 0 | — | ❌ | voľný — kandidát pre WiFi (S22 IDEA) |

**Celkom: 8 kamier aktívnych** (ráno 35.2 Mbit/s ≈ 0.36 TB/deň; aktuálne statické scény ~5 Mbit/s).
Kamery: **4K HEVC (3840×2160), ~15 fps** (ffprobe: r_frame_rate 15000/1001).
Názvy kamier v UI boli určené vision analýzou (S22c) a neskôr upravené s diakritikou (S22d).

**Obrazové parametre (GetConfig Camera.Param, 2026-08-10) — VŠETKY KAMERY POCTIVO:**

| Kanál | Deň/Noc | WB | Gain | Expozícia | nf D/N | IRCUT | Flip/Mirror | AeSens | BLC | Motion Level |
|---|---|---|---|---|---|---|---|---|---|---|
| CH1 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH2 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH3 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH4 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH5 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH6 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** |
| CH7 | 0 (Auto) | 0 (Auto) | Auto/50 | 2 (auto) | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** ✅ (bolo 3) |
| CH8 | 0xFFFFFFFF (neplatné) | 0 | Auto/50 | 2 | 3/3 | 0/1 | 0/0 | 5 | 0 | **6** ✅ (bolo 3) |
| CH9 | neexistuje (Ret 607) | — | — | — | — | — | — | — | — | — |

**Zistenia:**
- CH1–7 majú identické obrazové nastavenia (všetko Auto) — default konfigurácia
- **CH7 (nová kamera) a CH8 majú Motion Level 3** (nižšia citlivosť ako CH1–6: Level 6)
- **CH8** vracia Camera.Param s `DayNightColor=0xFFFFFFFF` (kanál existuje, kamera nepripojená/neplatná)
- **CH9** (index 8) neexistuje v Camera.Param (Ret 607) — NVR má reálne 8 HW kanálov + 1 (DigChannel=9)

**Nastavenia detekcie pohybu (Detect.MotionDetect) — všetky kanály:**
Enable=true, Level 6 (CH7–8: **3 → 6 nastavené 2026-08-10 cez SetConfig**, overené),
RecordEnable=true, SnapEnable=true, MessageEnable=true, FTPEnable=false, Region = celá plocha.

## 4. Parametre nastaviteľné NVR/kamerám

**NVR (z datasheetu):**
- Video: 16×4K vstup, H.265AI/H.265+ (kompatibilné H.264), 4K display
- Playback: 4K(1)/5M(1+1)/4M(2+1)/3M(2+2)/1080P(4+1) kanálov súčasne
- Recording: Manual > Alarm > Motion > Timing
- Sieť: 1× RJ45 **100M** ethernet, DHCP/FTP/DNS/DDNS/NTP/UPnP/EMAIL
- ONVIF, GB28181, cloud (MYEYE), web CMS
- Audio: G.711A, 1× 3.5mm in/out
- Výstupy: HDMI 4K + VGA 1080P, 2× USB 2.0
- Napájanie: 12V/2A, <10W (bez HDD)

## 4b. AKTUÁLNE NASTAVENIA (GetConfig, 2026-08-10) — čo je reálne nastavené

**Sieť NVR (`NetWork.NetCommon`):**
| Parametr | Hodnota |
|---|---|
| HostIP | **<nvr-ip>** |
| Gateway | <gateway-ip> |
| Submask | 0x00FCFFFF → 255.255.252.0 (/22) |
| MAC | <mac> |
| TCP port | 34567 (DVRIP), UDP 34568, HTTP 80, SSL 8443 |
| TCPMaxConn | 10 |
| MonMode | TCP, TransferPlan AutoAdapt |

**Detekcia pohybu (`Detect.MotionDetect`) — všetkých 7+ kanálov:**
| Parametr | Hodnota |
|---|---|
| Enable | **true** (zapnuté na všetkých kanáloch) |
| Level | 6 |
| RecordEnable | true (pohyb → nahrávanie) |
| SnapEnable | true (pohyb → fotka) |
| MessageEnable | true (pohyb → push/notifikácia) |
| Region | celá plocha (0xFFFFFFFF × 16) |

**Obraz kamery CH1 (`Camera.Param.[0]`):**
| Parametr | Hodnota |
|---|---|
| DayNightColor | 0 (Auto D/N) |
| WhiteBalance | 0 (Auto) |
| AutoGain | 1 (auto), Gain 50 |
| EsShutter | 2 (auto expozícia), ExposureParam MostTime 0x10000 |
| Day/Night nfLevel | 3/3 (denná/nočná redukcia šumu) |
| IRCUTMode | 0, IrcutSwap 1 (IR filter swap) |
| PictureFlip/Mirror | 0/0 (vypnuté) |
| RejectFlicker | 0 |
| AeSensitivity | 5 |
| BLCMode | 0 (BLC vypnuté) |

**Kodek/rozlíšenie (z RTSP ffprobe + WorkState):** 4K HEVC, ~15 fps,
bitrate per kanál 4.8–7.7 Mbit/s (aktuálne, WorkState).

**Poznámka:** `Encode.Encode` vracia `[null]` — NVR je HVR a encode konfigurácia
pre IP kamery sa riadi na strane kamery (NVR len passthrough), resp. cez NVR
menu priamo pri Xiongmai kamerách. Pre tretie strany: bitrate/fps sa nastavuje
v kamere, NVR len nahráva.

## 5. WiFi kamera so solárom — záver

**ÁNO, pripojiteľná** — podmienky:
1. NVR má **voľné kanály** (CH7–9, celkom 16 kanálov).
2. WiFi kamera musí byť **dostupná cez sieť z NVR** (IP kamera cez LAN/WiFi bridge).
   - Ak je na inej WiFi subsieti → potrebné **routovanie medzi subsieťami**
     (router/NVR musia vidieť jej IP; NVR podporuje IP kamery tretích strán cez ONVIF).
   - NVR má len 100M port — WiFi kamera s vlastným bitrate ~5 Mbit/s je v pohode.
3. Kamera so solárom: NVR len nahráva — solár/batéria je na kamere, nie NVR.
4. Odporúčanie: ONVIF kompatibilná kamera (H.265, ≤ 4K) — NVR ju pridá ako kanál 7.

## 6. Zaujímavosti / poznámky

- **RTSP na NVR ignoruje číslo kanála** (všetky URL = rovnaký stream) — S18 nález.
- WorkState poskytuje živé bitraty — môžeme ich použiť pre monitoring v UI (nápad).
- Disk je plný (0 MB) — cyklické prepisovanie funguje, ale pred výmenou HDD
  odporúčame zálohu dôležitých záznamov (backup-db.py je len DB, nie videá).
