#!/usr/bin/env bash

set -euo pipefail

APP_USER="einsatz"
APP_GROUP="einsatz"
APP_ROOT="/opt/einsatzueberwachung"
SERVER_DIR="$APP_ROOT/server"
MOBILE_DIR="$APP_ROOT/mobile"
DATA_DIR="$APP_ROOT/data"
REPORT_DIR="$DATA_DIR/berichte"

if [[ $EUID -ne 0 ]]; then
	echo "Bitte als root ausfuehren."
	exit 1
fi

echo "[1/8] Pakete installieren"
apt-get update
apt-get install -y nginx ufw curl rsync unzip dotnet-runtime-9.0

echo "[2/8] Systembenutzer sicherstellen"
if ! id "$APP_USER" >/dev/null 2>&1; then
	useradd --system --create-home --shell /usr/sbin/nologin "$APP_USER"
fi

echo "[3/8] Verzeichnisstruktur anlegen"
mkdir -p "$SERVER_DIR" "$MOBILE_DIR"
mkdir -p "$APP_ROOT/scripts" "$APP_ROOT/backups" "$DATA_DIR" "$REPORT_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$APP_ROOT"

echo "[4/8] Deployment-Artefakte kopieren"
if [[ -d "./publish/server" ]]; then
	cp -r ./publish/server/* "$SERVER_DIR/"
fi

if [[ -d "./publish/mobile" ]]; then
	cp -r ./publish/mobile/* "$MOBILE_DIR/"
fi

chown -R "$APP_USER":"$APP_GROUP" "$APP_ROOT"

echo "[5/8] Nginx Konfiguration"
cp ./deploy/nginx/einsatzueberwachung.conf /etc/nginx/sites-available/einsatzueberwachung
ln -sf /etc/nginx/sites-available/einsatzueberwachung /etc/nginx/sites-enabled/einsatzueberwachung
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl restart nginx

echo "[6/8] systemd Services"
cp ./deploy/systemd/einsatzueberwachung-server.service /etc/systemd/system/
cp ./deploy/systemd/einsatzueberwachung-mobile.service /etc/systemd/system/
cp ./deploy/systemd/einsatzueberwachung-backup.service /etc/systemd/system/
cp ./deploy/systemd/einsatzueberwachung-backup.timer /etc/systemd/system/
cp ./deploy/systemd/einsatzueberwachung-healthcheck.service /etc/systemd/system/
cp ./deploy/systemd/einsatzueberwachung-healthcheck.timer /etc/systemd/system/

install -m 755 ./deploy/scripts/backup.sh "$APP_ROOT/scripts/backup.sh"
install -m 755 ./deploy/scripts/health-check.sh "$APP_ROOT/scripts/health-check.sh"
install -m 755 ./deploy/scripts/apply-update.sh "$APP_ROOT/scripts/apply-update.sh"

cat >/etc/sudoers.d/einsatzueberwachung-update <<EOF
${APP_USER} ALL=(root) NOPASSWD: /bin/systemctl restart einsatzueberwachung-server.service, /bin/systemctl restart einsatzueberwachung-mobile.service
EOF
chmod 440 /etc/sudoers.d/einsatzueberwachung-update

systemctl daemon-reload
systemctl enable einsatzueberwachung-server.service
systemctl enable einsatzueberwachung-mobile.service
systemctl enable einsatzueberwachung-backup.timer
systemctl enable einsatzueberwachung-healthcheck.timer
systemctl restart einsatzueberwachung-server.service
systemctl restart einsatzueberwachung-mobile.service
systemctl restart einsatzueberwachung-backup.timer
systemctl restart einsatzueberwachung-healthcheck.timer

echo "[7/8] UFW Regeln setzen"
ufw allow 22/tcp
ufw allow 443/tcp
ufw allow 51820/udp
ufw --force enable

echo "[8/8] Fertig"
echo "Status pruefen mit: systemctl status einsatzueberwachung-server.service"
echo "Status pruefen mit: systemctl status einsatzueberwachung-mobile.service"
echo "Timer pruefen mit: systemctl status einsatzueberwachung-backup.timer"
echo "Timer pruefen mit: systemctl status einsatzueberwachung-healthcheck.timer"
