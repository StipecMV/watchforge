#!/usr/bin/env python3
"""Produkčný seed WatchForge DB (S21-2): NVR (lokálna lokalita) + 8 kamier + user admin.

Použitie: python3 deploy/scripts/seed-prod.py <path/to/watchforge.db>
- Idempotentný: INSERT OR IGNORE (opakované spustenie je bezpečné).
- Heslo: user admin sa vytvorí BEZ hesla (hash='') → prvý login cez
  POST /api/v1/auth/set-password (štandardný WatchForge flow).
- NVR heslo sa NEUKLADÁ — číta sa z env WATCHFORGE_NVR_PASSWORD (secret).
"""
import sqlite3, sys, datetime

db = sys.argv[1] if len(sys.argv) > 1 else "watchforge.db"
conn = sqlite3.connect(db)
c = conn.cursor()
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%fZ')

# USERS — len admin (heslo nastavené manuálne v produkcii).
# 2026-08-14: rodinné účty (standard) odstránené — web UI je verejné pre
# neprihlásených (live view + analýzy), prihlasuje sa LEN admin cez /admin.
c.execute("INSERT OR IGNORE INTO USERS (username, password_hash, role, avatar_id, locale, created_at) "
          "VALUES ('admin','','admin',1,'sk',?)", (now,))

# NVRS — lokálna lokalita (Xiongmai NBD80S16S-KL, <nvr-ip>)
c.execute("""INSERT OR IGNORE INTO NVRS (site_id, host, port, username, password_secret_env)
             VALUES ('site-a','<nvr-ip>',34567,'<nvr-username>','WATCHFORGE_NVR_PASSWORD')""")

# Kamery — 8 aktívnych (CH1–CH8), CH9 nepripojená (neaktívna).
# Názvy podľa vision analýzy framov + úpravy stakeholdera (2026-08-14):
# CH1 Psia buda (Ares), CH3 Chodník (z hora), CH4 Záhrada pri fontánke,
# CH5 Hnojisko, CH7 Chodník (priamo) — s diakritikou.
cameras = [
    (1, 'Psia buda (Ares)',      1),
    (2, 'Vjazdová brána',         1),
    (3, 'Chodník (z hora)',       1),
    (4, 'Záhrada pri fontánke',   1),
    (5, 'Hnojisko',               1),
    (6, 'Hospodársky dvor',       1),
    (7, 'Chodník (priamo)',       1),
    (8, 'Záhrada pri dome',       1),  # nainštalovaná 2026-08-12
    (9, 'Kamera 9',               0),  # WiFi kandidát
]
for channel, name, active in cameras:
    c.execute("INSERT OR IGNORE INTO CAMERAS (nvr_id, channel, friendly_name, icon_id, is_active) "
              "VALUES (1, ?, ?, 'camera', ?)", (channel - 1, name, active))

conn.commit()
conn.close()
print(f"seed OK: {db} (NVR + {len(cameras)} kamier + user admin)")
