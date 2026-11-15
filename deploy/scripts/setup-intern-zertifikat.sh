#!/usr/bin/env bash
# Einmalige Einrichtung: gueltiges Let's-Encrypt-Zertifikat fuer die interne Adresse (nur im VPN erreichbar),
# z. B. el.dein-einsatz.mywire.org → VPN-IP des Servers (Standard 10.10.0.1). Ersetzt das selbst ausgestellte Zertifikat, dem die EL-App nicht vertraut.
# Die Pruefung laeuft ueber einen TXT-Eintrag bei Dynu (DNS-01), der Server muss dafuer nicht von aussen erreichbar sein.
#
#   sudo bash deploy/scripts/setup-intern-zertifikat.sh                       el.<domain> aus staffel.conf
#   sudo bash deploy/scripts/setup-intern-zertifikat.sh <interne-adresse>     z. B. el.dein-einsatz.mywire.org
#
# Bei dns_anbieter=andere (einsatz-staffel) fragt certbot nach einem TXT-Eintrag, der von Hand beim DNS-Anbieter
# gesetzt wird; die Verlaengerung muss dann alle 90 Tage ebenso von Hand laufen.
#
# Voraussetzung: certbot, curl und python3 sind installiert; bei Dynu zeigt der A-Eintrag der internen Adresse auf die
# VPN-IP des Servers; der API-Key steht in Dynu unter „API Credentials“. Kann mehrfach ausgefuehrt werden.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
HOST="${1:-$(bash "$HERE/einsatz-staffel" wert intern_adresse)}"
VPN_IP="$(bash "$HERE/einsatz-staffel" wert vpn_server_ip)"
ANBIETER="$(bash "$HERE/einsatz-staffel" wert dns_anbieter)"
CONF="/etc/einsatzueberwachung/dynu.conf"
HOOK="/usr/local/sbin/einsatz-dynu-dns"
CRT="/etc/ssl/certs/einsatz.crt"
KEY="/etc/ssl/private/einsatz.key"

[[ $EUID -eq 0 ]] || { echo "Bitte als root ausfuehren (sudo)."; exit 1; }
[[ "$HOST" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ && "$HOST" == *.* ]] || { echo "Aufruf: setup-intern-zertifikat.sh <interne-adresse>"; exit 1; }
for p in certbot curl python3; do command -v "$p" >/dev/null || { echo "$p fehlt (apt install $p)."; exit 1; }; done

echo "[1/5] Adresse pruefen"
IP="$(python3 -c 'import socket,sys; print(socket.gethostbyname(sys.argv[1]))' "$HOST" 2>/dev/null || true)"
if [[ "$IP" == "$VPN_IP" ]]; then
	echo "  $HOST → $IP"
else
	echo "  WARNUNG: $HOST zeigt nicht auf die VPN-IP $VPN_IP (${IP:-nicht aufloesbar}). A-Eintrag beim DNS-Anbieter pruefen."
fi

if [[ "$ANBIETER" == andere ]]; then
	echo "[2/5] DNS-Anbieter ohne Schnittstelle: TXT-Eintrag von Hand"
	echo "  certbot zeigt gleich Name und Wert eines TXT-Eintrags. Diesen beim DNS-Anbieter anlegen, kurz warten,"
	echo "  dann in certbot mit Enter bestaetigen."
	echo "[3/5] Zertifikat ueber DNS-Pruefung ausstellen"
	certbot certonly --manual --preferred-challenges dns -d "$HOST" --cert-name "$HOST" --agree-tos \
		--keep-until-expiring --deploy-hook "systemctl reload nginx"
else
	echo "[2/5] Dynu-API-Key"
	mkdir -p /etc/einsatzueberwachung
	if [[ ! -s "$CONF" ]]; then
		read -rsp "  API-Key aus Dynu (API Credentials), Eingabe wird nicht angezeigt: " API_KEY
		echo
		[[ -n "$API_KEY" ]] || { echo "  Kein Key eingegeben."; exit 1; }
		umask 077
		printf 'api_key=%s\n' "$API_KEY" >"$CONF"
		unset API_KEY
	fi
	chown root:root "$CONF"
	chmod 600 "$CONF"
	install -o root -g root -m 755 "$HERE/einsatz-dynu-dns" "$HOOK"
	echo "  Gespeichert in $CONF (nur root lesbar)."

	echo "[3/5] Zertifikat ueber DNS-Pruefung ausstellen"
	# Die Hooks landen in der Verlaengerungs-Konfiguration von certbot; der vorhandene certbot-Timer verlaengert automatisch
	certbot certonly --manual --preferred-challenges dns \
		--manual-auth-hook "$HOOK auth" --manual-cleanup-hook "$HOOK cleanup" \
		-d "$HOST" --cert-name "$HOST" --non-interactive --agree-tos --keep-until-expiring \
		--deploy-hook "systemctl reload nginx"
fi

echo "[4/5] nginx auf das neue Zertifikat umstellen"
# Die interne nginx-Konfiguration verweist weiter auf einsatz.crt/einsatz.key; das alte selbst ausgestellte bleibt als Sicherung
for f in "$CRT" "$KEY"; do
	if [[ -e "$f" && ! -L "$f" ]]; then
		mv "$f" "$f.selbst-$(date +%Y%m%d)"
		echo "  Altes Zertifikat gesichert: $f.selbst-$(date +%Y%m%d)"
	fi
done
ln -sfn "/etc/letsencrypt/live/$HOST/fullchain.pem" "$CRT"
ln -sfn "/etc/letsencrypt/live/$HOST/privkey.pem" "$KEY"
nginx -t
systemctl reload nginx

echo "[5/5] Pruefen"
# Vom Server selbst ueber die VPN-IP; ein gueltiges Zertifikat ist Voraussetzung, sonst bricht curl ab
if CODE="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$HOST:443:${IP:-$VPN_IP}" "https://$HOST/")"; then
	echo "  https://$HOST/ antwortet mit Zertifikat von Let's Encrypt (HTTP $CODE)."
else
	echo "  WARNUNG: https://$HOST/ nicht pruefbar (siehe Meldung oben)."
fi
echo
echo "Fertig. In der EL-App als Server eintragen: $HOST (WireGuard an)."
if [[ "$ANBIETER" == andere ]]; then
	echo "WICHTIG: Ohne Dynu verlaengert sich das Zertifikat nicht selbst. Vor Ablauf (90 Tage) dieses Skript erneut ausfuehren."
else
	echo "Automatische Verlaengerung pruefen: certbot renew --dry-run --cert-name $HOST"
fi
