#!/usr/bin/env python3
"""WatchForge SQLite backup job (S9-3).

Bezpečná záloha bežiacej WAL databázy:
  1. WAL checkpoint (TRUNCATE) — premietne WAL do hlavného súboru,
  2. VACUUM INTO — atomická, kompaktná kópia (funguje aj pri aktívnych čitateľoch),
  3. Rotácia starých záloh (retention, default 14 dní).

Použitie:
  backup-db.py <db_path> [backup_dir] [retention_days]

Príklad (Quadlet timer):
  /usr/bin/python3 /deploy/scripts/backup-db.py /data/watchforge.db /data/backups 14
"""
import datetime
import glob
import os
import sqlite3
import sys
import time


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: backup-db.py <db_path> [backup_dir] [retention_days]", file=sys.stderr)
        return 2

    db_path = os.path.abspath(sys.argv[1])
    backup_dir = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.dirname(db_path)
    retention_days = int(sys.argv[3]) if len(sys.argv) > 3 else 14

    if not os.path.isfile(db_path):
        print(f"DB neexistuje: {db_path} — preskakujem (prvý štart?).", file=sys.stderr)
        return 0

    os.makedirs(backup_dir, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_path = os.path.join(backup_dir, f"watchforge-{stamp}.db")

    conn = sqlite3.connect(db_path, timeout=30)
    try:
        # 1) WAL checkpoint — premietne pending WAL dáta do hlavného súboru
        conn.execute("PRAGMA wal_checkpoint(TRUNCATE);")

        # 2) VACUUM INTO — konzistentná kompaktná kópia (žiadne live locks)
        #    Pozn.: VACUUM INTO vyžaduje konštantný názov (bez bind parametrov).
        conn.execute(f"VACUUM INTO '{backup_path.replace(chr(39), chr(39) * 2)}';")
    finally:
        conn.close()

    size_mb = os.path.getsize(backup_path) / (1024 * 1024)
    print(f"Backup OK: {backup_path} ({size_mb:.1f} MB)")

    # 3) Rotácia starých záloh
    cutoff = time.time() - retention_days * 86400
    removed = 0
    for old in glob.glob(os.path.join(backup_dir, "watchforge-*.db")):
        if old == backup_path:
            continue
        try:
            if os.path.getmtime(old) < cutoff:
                os.remove(old)
                removed += 1
        except OSError:
            pass
    if removed:
        print(f"Rotácia: odstránených {removed} starých záloh (> {retention_days} dní)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
