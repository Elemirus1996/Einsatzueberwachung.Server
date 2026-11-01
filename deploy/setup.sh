#!/usr/bin/env bash
# Grundinstallation des Servers. Werte der Staffel (Domain, VPN-Netz, VPN-Port) kommen aus
# /etc/einsatzueberwachung/staffel.conf, vorher setzen mit:
#   sudo bash deploy/scripts/einsatz-staffel setzen domain <name>
# Kann mehrfach ausgefuehrt werden.

set -euo pipefail

APP_USER="einsatz"
APP_GROUP="einsatz"
APP_ROOT="/opt/einsatzueberwachung"
SERVER_DIR="$APP_ROOT/server"
DATA_DIR="$APP_ROOT/data"
REPORT_DIR="$DATA_DIR/berichte"
DEPLOY="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$DEPLOY")"
STAFFEL="$DEPLOY/scripts/einsatz-staffel"
CRT="/etc/ssl/certs/einsatz.crt"
KEY="/etc/ssl/private/einsatz.key"

if [[ $EUID -ne 0 ]]; then
	echo "Bitte als root ausfuehren."
	exit 1
fi

echo "[0/9] Einstellungen der Staffel pruefen"
if ! bash "$STAFFEL" pruefen; then
	echo "Erst die Einstellungen setzen, z. B.:"
	echo "  sudo bash deploy/scripts/einsatz-staffel setzen domain <name>"
	exit 1
fi
bash "$STAFFEL" zeigen | sed 's/^/  /'
VPN_IP="$(bash "$STAFFEL" wert vpn_server_ip)"
VPN_PORT="$(bash "$STAFFEL" wert vpn_port)"

echo "[1/9] Pakete installieren"
apt-get update
apt-get install -y nginx ufw curl rsync unzip chrony certbot python3 openssl wireguard
# Der Server braucht die ASP.NET-Core-Laufzeit (enthaelt die .NET-Laufzeit). Ubuntu 22.04 und 24.04 haben .NET 9
# nicht in den eigenen Quellen, dort kommt es aus der Paketquelle „dotnet/backports“.
if ! apt-cache show aspnetcore-runtime-9.0 >/dev/null 2>&1; then
	echo "  aspnetcore-runtime-9.0 nicht in den Paketquellen, Quelle dotnet/backports einrichten"
	apt-get install -y software-properties-common
	add-apt-repository -y ppa:dotnet/backports
	apt-get update
fi
apt-get install -y aspnetcore-runtime-9.0

echo "[1b/9] Zeit-Synchronisierung (chrony) aktivieren"
systemctl enable chrony
systemctl restart chrony

echo "[2/9] Systembenutzer sicherstellen"
if ! id "$APP_USER" >/dev/null 2>&1; then
	useradd --system --create-home --shell /usr/sbin/nologin "$APP_USER"
fi

echo "[3/9] Verzeichnisstruktur anlegen"
mkdir -p "$SERVER_DIR"
mkdir -p "$APP_ROOT/scripts" "$APP_ROOT/backups" "$DATA_DIR" "$REPORT_DIR"
chown -R "$APP_USER":"$APP_GROUP" "$APP_ROOT"

echo "[4/9] Deployment-Artefakte kopieren"
if [[ -d "$REPO/publish/server" ]]; then
	cp -r "$REPO"/publish/server/* "$SERVER_DIR/"
fi

chown -R "$APP_USER":"$APP_GROUP" "$APP_ROOT"

echo "[5/9] Vorlaeufiges Zertifikat fuer die interne Adresse"
# Nur wenn noch keins da ist; setup-intern-zertifikat.sh ersetzt es spaeter durch ein gueltiges
if [[ -e "$CRT" && -e "$KEY" ]]; then
	echo "  Vorhanden: $CRT"
else
	openssl req -x509 -nodes -newkey rsa:2048 -days 90 -subj "/CN=einsatz" \
		-addext "subjectAltName=IP:$VPN_IP" -keyout "$KEY" -out "$CRT" 2>/dev/null
	chmod 600 "$KEY"
	echo "  Selbst ausgestellt (90 Tage): $CRT"
fi

echo "[6/9] Nginx Konfiguration"
bash "$STAFFEL" vorlage "$DEPLOY/nginx/einsatzueberwachung.conf" >/etc/nginx/sites-available/einsatzueberwachung
ln -sf /etc/nginx/sites-available/einsatzueberwachung /etc/nginx/sites-enabled/einsatzueberwachung
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl restart nginx

echo "[7/9] systemd Services"
cp "$DEPLOY"/systemd/einsatzueberwachung-server.service /etc/systemd/system/
cp "$DEPLOY"/systemd/einsatzueberwachung-backup.service /etc/systemd/system/
cp "$DEPLOY"/systemd/einsatzueberwachung-backup.timer /etc/systemd/system/
cp "$DEPLOY"/systemd/einsatzueberwachung-healthcheck.service /etc/systemd/system/
cp "$DEPLOY"/systemd/einsatzueberwachung-healthcheck.timer /etc/systemd/system/

install -m 755 "$DEPLOY"/scripts/backup.sh "$APP_ROOT/scripts/backup.sh"
install -m 755 "$DEPLOY"/scripts/health-check.sh "$APP_ROOT/scripts/health-check.sh"
install -m 755 "$DEPLOY"/scripts/apply-update.sh "$APP_ROOT/scripts/apply-update.sh"
install -o root -g root -m 755 "$STAFFEL" /usr/local/sbin/einsatz-staffel

cat >/etc/sudoers.d/einsatzueberwachung-update <<EOF
${APP_USER} ALL=(root) NOPASSWD: /bin/systemctl restart einsatzueberwachung-server.service
EOF
chmod 440 /etc/sudoers.d/einsatzueberwachung-update

systemctl daemon-reload
systemctl enable einsatzueberwachung-server.service
systemctl enable einsatzueberwachung-backup.timer
systemctl enable einsatzueberwachung-healthcheck.timer
systemctl restart einsatzueberwachung-server.service
systemctl restart einsatzueberwachung-backup.timer
systemctl restart einsatzueberwachung-healthcheck.timer

echo "[8/9] WireGuard (wg0)"
WG_CONF="/etc/wireguard/wg0.conf"
if [[ -f "$WG_CONF" ]]; then
	# Vorhandene Konfiguration nicht anfassen, nur auf Abweichung von staffel.conf hinweisen
	if ! grep -qE "^\s*Address\s*=\s*$VPN_IP/24\s*$" "$WG_CONF" || ! grep -qE "^\s*ListenPort\s*=\s*$VPN_PORT\s*$" "$WG_CONF"; then
		echo "  WARNUNG: $WG_CONF passt nicht zu staffel.conf ($VPN_IP/24, Port $VPN_PORT). Nicht geaendert."
	else
		echo "  Vorhanden: $WG_CONF"
	fi
else
	umask 077
	PRIV="$(wg genkey)"
	cat >"$WG_CONF" <<EOF
[Interface]
Address = $VPN_IP/24
ListenPort = $VPN_PORT
PrivateKey = $PRIV
EOF
	unset PRIV
	umask 022
	echo "  Angelegt: $WG_CONF ($VPN_IP/24, Port $VPN_PORT). Geraete kommen per QR-Code aus der Oberflaeche dazu."
fi
systemctl enable wg-quick@wg0 >/dev/null 2>&1
systemctl is-active --quiet wg-quick@wg0 || systemctl start wg-quick@wg0

echo "[9/9] UFW Regeln setzen"
# Laufende SSH-Verbindung nicht aussperren, auch wenn SSH nicht auf 22 laeuft
SSH_CONN="${SSH_CONNECTION:-}"
ufw allow 22/tcp
if [[ -n "$SSH_CONN" && "${SSH_CONN##* }" != 22 ]]; then
	ufw allow "${SSH_CONN##* }/tcp"
fi
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow "$VPN_PORT/udp"
ufw --force enable

echo "Fertig"
echo "Status pruefen mit: systemctl status einsatzueberwachung-server.service"
echo "Timer pruefen mit: systemctl status einsatzueberwachung-backup.timer"
echo "Timer pruefen mit: systemctl status einsatzueberwachung-healthcheck.timer"
echo "WireGuard pruefen mit: wg show wg0"
