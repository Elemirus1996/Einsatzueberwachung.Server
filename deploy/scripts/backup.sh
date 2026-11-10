#!/usr/bin/env bash
# Naechtliches Server-Backup. Laeuft als Benutzer 'einsatz', damit die App Backups in
# Einstellungen → Wartung auflisten und loeschen kann.

set -euo pipefail

APP_ROOT="${APP_ROOT:-/opt/einsatzueberwachung}"
BACKUP_ROOT="${BACKUP_ROOT:-$APP_ROOT/backups}"
# Anzahl Backups, die nach diesem Lauf vorhanden sind (inklusive des neuen)
RETENTION_COUNT="${RETENTION_COUNT:-5}"
# Unter diesem freien Platz wird kein Backup erstellt (sonst laeuft die Platte voll, vgl. Issue #181)
MIN_FREE_GB="${MIN_FREE_GB:-15}"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
TMP_DIR="$BACKUP_ROOT/tmp-$TIMESTAMP"
ARCHIVE="$BACKUP_ROOT/einsatz-backup-$TIMESTAMP.tar.gz"
OK=0

cleanup() {
  rm -rf "$TMP_DIR"
  # Halbes Archiv nach Fehler nicht liegen lassen
  [[ $OK -eq 1 ]] || rm -f "$ARCHIVE"
}
trap cleanup EXIT

mkdir -p "$BACKUP_ROOT"

# Reste abgebrochener Laeufe entfernen
find "$BACKUP_ROOT" -maxdepth 1 -type d -name "tmp-*" -exec rm -rf {} +

# Alte Backups VOR dem neuen loeschen: die neuesten RETENTION_COUNT-1 bleiben
if (( RETENTION_COUNT > 0 )); then
  find "$BACKUP_ROOT" -maxdepth 1 -type f -name "einsatz-backup-*.tar.gz" -printf '%f\n' \
    | sort -r | tail -n +"$RETENTION_COUNT" \
    | while IFS= read -r alt; do rm -f -- "$BACKUP_ROOT/$alt"; done
fi

FREE_GB=$(df -BG --output=avail "$BACKUP_ROOT" | tail -1 | tr -dc '0-9')
if (( FREE_GB < MIN_FREE_GB )); then
  echo "Abbruch: nur ${FREE_GB} GB frei (mindestens ${MIN_FREE_GB} GB noetig)" >&2
  exit 1
fi

mkdir -p "$TMP_DIR"
EXCLUDES=(--exclude "*.db" --exclude "*.db-wal" --exclude "*.db-shm" --exclude "*.db-journal")

if [[ -d "$APP_ROOT/server" ]]; then
  rsync -a "${EXCLUDES[@]}" \
    --exclude "logs/" \
    --exclude "wwwroot/" \
    --exclude "*.log" \
    "$APP_ROOT/server/" "$TMP_DIR/server/"
fi

if [[ -d "$APP_ROOT/data" ]]; then
  # data/updates enthaelt nur heruntergeladene Update-Pakete – nicht jede Nacht mitsichern
  rsync -a "${EXCLUDES[@]}" --exclude "/updates/" "$APP_ROOT/data/" "$TMP_DIR/data/"
fi

# SQLite konsistent sichern (laufende App, WAL aktiv) – cp kann eine kaputte Kopie liefern
for dir in "$APP_ROOT/server" "$APP_ROOT/data"; do
  [[ -d "$dir" ]] || continue
  find "$dir" -type f \( -name "*.db" -o -name "*.sqlite" -o -name "*.sqlite3" \) -print0 \
    | while IFS= read -r -d '' db_file; do
        rel_path="${db_file#"${APP_ROOT}"/}"
        mkdir -p "$TMP_DIR/databases/$(dirname "$rel_path")"
        sqlite3 "$db_file" ".backup '$TMP_DIR/databases/$rel_path'"
      done
done

cat > "$TMP_DIR/backup-meta.txt" <<META
created_at=$TIMESTAMP
host=$(hostname)
retention_count=$RETENTION_COUNT
META

tar -C "$TMP_DIR" -czf "$ARCHIVE" .
OK=1

echo "Backup erstellt: $ARCHIVE"
