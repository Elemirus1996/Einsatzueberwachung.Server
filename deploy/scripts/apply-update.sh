#!/usr/bin/env bash

set -euo pipefail

PACKAGE_PATH="${1:-}"
TARGET_VERSION="${2:-unknown}"
APP_ROOT="/opt/einsatzueberwachung"
SERVER_DIR="$APP_ROOT/server"
MOBILE_DIR="$APP_ROOT/mobile"
UPDATE_ROOT="$APP_ROOT/updates"
WORK_DIR="$UPDATE_ROOT/work-$TARGET_VERSION"
BACKUP_DIR="$UPDATE_ROOT/backup-$TARGET_VERSION"
LOCK_FILE="$UPDATE_ROOT/update.lock"

if [[ -z "$PACKAGE_PATH" ]]; then
  echo "Usage: $0 <package-path> [version]"
  exit 2
fi

if [[ ! -f "$PACKAGE_PATH" ]]; then
  echo "Package not found: $PACKAGE_PATH"
  exit 2
fi

mkdir -p "$UPDATE_ROOT"
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "Another update process is running"
  exit 3
fi

rm -rf "$WORK_DIR" "$BACKUP_DIR"
mkdir -p "$WORK_DIR" "$BACKUP_DIR/server" "$BACKUP_DIR/mobile"

if [[ "$PACKAGE_PATH" == *.zip ]]; then
  unzip -q "$PACKAGE_PATH" -d "$WORK_DIR"
elif [[ "$PACKAGE_PATH" == *.tar.gz ]]; then
  tar -xzf "$PACKAGE_PATH" -C "$WORK_DIR"
else
  echo "Unsupported package format: $PACKAGE_PATH"
  exit 4
fi

# Backup current deployment for rollback
if [[ -d "$SERVER_DIR" ]]; then
  rsync -a --delete "$SERVER_DIR/" "$BACKUP_DIR/server/"
fi
if [[ -d "$MOBILE_DIR" ]]; then
  rsync -a --delete "$MOBILE_DIR/" "$BACKUP_DIR/mobile/"
fi

# Locate payload directories. Supports either root files or server/mobile subfolders.
PAYLOAD_SERVER="$WORK_DIR"
PAYLOAD_MOBILE=""
if [[ -d "$WORK_DIR/server" ]]; then
  PAYLOAD_SERVER="$WORK_DIR/server"
fi
if [[ -d "$WORK_DIR/mobile" ]]; then
  PAYLOAD_MOBILE="$WORK_DIR/mobile"
fi

rsync -a --delete "$PAYLOAD_SERVER/" "$SERVER_DIR/"
if [[ -n "$PAYLOAD_MOBILE" ]]; then
  rsync -a --delete "$PAYLOAD_MOBILE/" "$MOBILE_DIR/"
fi

mkdir -p "$UPDATE_ROOT"
echo "$TARGET_VERSION" > "$UPDATE_ROOT/current-version.txt"
cp "$PACKAGE_PATH" "$UPDATE_ROOT/last-package$(basename "$PACKAGE_PATH" | sed 's/.*\(\.[^.]*\)$/\1/')"

# Restart services (requires sudoers rule for user 'einsatz').
sudo /bin/systemctl restart einsatzueberwachung-server.service
sudo /bin/systemctl restart einsatzueberwachung-mobile.service

# health probe after restart
sleep 4
if ! curl -fsS "http://127.0.0.1:5000/health" >/dev/null; then
  echo "Server health check failed, rolling back"
  rsync -a --delete "$BACKUP_DIR/server/" "$SERVER_DIR/"
  if [[ -d "$BACKUP_DIR/mobile" ]]; then
    rsync -a --delete "$BACKUP_DIR/mobile/" "$MOBILE_DIR/"
  fi
  sudo /bin/systemctl restart einsatzueberwachung-server.service
  sudo /bin/systemctl restart einsatzueberwachung-mobile.service
  exit 5
fi

echo "Update applied successfully: $TARGET_VERSION"
