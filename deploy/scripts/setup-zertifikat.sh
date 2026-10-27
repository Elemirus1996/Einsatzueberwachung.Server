#!/usr/bin/env bash
# Einmalige Einrichtung: Zertifikat der oeffentlichen Domain per Webroot verlaengern und den Knopf
# „Jetzt erneuern“ (Einstellungen → System, Hinweis auf Startseite/EL) freischalten (Einsatzueberwachung ab 1.11.6).
#
#   sudo bash deploy/scripts/setup-zertifikat.sh <domain>     z. B. dein-einsatz.mywire.org
#
# Voraussetzung: certbot ist installiert, die neue einsatzueberwachung-public.conf (Port-80-Block mit
# /.well-known/acme-challenge/ aus /var/www/certbot) ist aktiv. Kann mehrfach ausgefuehrt werden.
set -euo pipefail

APP_USER="einsatz"
WEBROOT="/var/www/certbot"
HERE="$(cd "$(dirname "$0")" && pwd)"
DOMAIN="${1:-}"

[[ $EUID -eq 0 ]] || { echo "Bitte als root ausfuehren (sudo)."; exit 1; }
[[ "$DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || { echo "Aufruf: setup-zertifikat.sh <domain>"; exit 1; }
command -v certbot >/dev/null || { echo "certbot fehlt (apt install certbot)."; exit 1; }

echo "[1/4] Webroot und Konfiguration"
mkdir -p "$WEBROOT" /etc/einsatzueberwachung
printf 'domain=%s\nwebroot=%s\n' "$DOMAIN" "$WEBROOT" >/etc/einsatzueberwachung/zertifikat.conf
chown root:root /etc/einsatzueberwachung/zertifikat.conf
chmod 644 /etc/einsatzueberwachung/zertifikat.conf

echo "[2/4] Pruefen, ob Let's Encrypt den Webroot erreicht"
PROBE="einsatz-probe-$RANDOM"
mkdir -p "$WEBROOT/.well-known/acme-challenge"
echo ok >"$WEBROOT/.well-known/acme-challenge/$PROBE"
if curl -fsS --max-time 10 "http://$DOMAIN/.well-known/acme-challenge/$PROBE" >/dev/null; then
	echo "  http://$DOMAIN/.well-known/acme-challenge/ erreichbar."
else
	echo "  WARNUNG: Pruefpfad nicht erreichbar – neue einsatzueberwachung-public.conf uebernommen und nginx neu geladen?"
fi
rm -f "$WEBROOT/.well-known/acme-challenge/$PROBE"

echo "[3/4] Hilfsprogramm fuer den Knopf „Jetzt erneuern“"
# Gehoert root und ist fuer den Dienstbenutzer nicht beschreibbar
install -o root -g root -m 755 "$HERE/einsatz-zertifikat" /usr/local/sbin/einsatz-zertifikat
SUDOERS=/etc/sudoers.d/einsatzueberwachung-zertifikat
echo "$APP_USER ALL=(root) NOPASSWD: /usr/local/sbin/einsatz-zertifikat erneuern, /usr/local/sbin/einsatz-zertifikat info" >"$SUDOERS.tmp"
chmod 440 "$SUDOERS.tmp"
visudo -cf "$SUDOERS.tmp" >/dev/null
mv "$SUDOERS.tmp" "$SUDOERS"
sudo -u "$APP_USER" sudo -n /usr/local/sbin/einsatz-zertifikat info >/dev/null && echo "  Dienstbenutzer darf das Hilfsprogramm ausfuehren."

echo "[4/4] Zertifikat einmalig ueber den Webroot neu ausstellen"
# --force-renewal nur hier: erst dann schreibt certbot den Webroot-Weg in seine Verlaengerungs-Konfiguration,
# sonst versucht die automatische Verlaengerung weiter den alten Weg. Der Knopf in der App erzwingt nichts.
certbot certonly --webroot -w "$WEBROOT" -d "$DOMAIN" --cert-name "$DOMAIN" \
	--non-interactive --agree-tos --force-renewal --deploy-hook "systemctl reload nginx"
echo
echo "Fertig. Automatische Verlaengerung pruefen: certbot renew --dry-run"
