#!/usr/bin/env bash
# Einmalige Einrichtung fuer VPN-Geraete per QR-Code und DNS im VPN (Einsatzueberwachung ab 1.10).
#
#   sudo bash deploy/scripts/setup-vpn.sh <domain>     z. B. dein-einsatz.mywire.org
#   sudo bash deploy/scripts/setup-vpn.sh              nur VPN-Geraete, ohne DNS
#
# Voraussetzung: WireGuard laeuft auf diesem Server als wg0 (deploy/wireguard/wg0.conf.example).
# Das Skript kann mehrfach ausgefuehrt werden.
set -euo pipefail

APP_USER="einsatz"
WG_IF="wg0"
HERE="$(cd "$(dirname "$0")" && pwd)"
DOMAIN="${1:-}"

[[ $EUID -eq 0 ]] || { echo "Bitte als root ausfuehren (sudo)."; exit 1; }
ip link show "$WG_IF" >/dev/null 2>&1 || { echo "WireGuard-Schnittstelle $WG_IF nicht gefunden."; exit 1; }
if [[ -n "$DOMAIN" && ! "$DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]]; then
	echo "Ungueltiger Domainname: $DOMAIN"; exit 1
fi

WG_CIDR="$(ip -4 -o addr show dev "$WG_IF" | awk '{print $4}' | head -n1)"
WG_IP="${WG_CIDR%/*}"
echo "WireGuard: $WG_IF mit $WG_CIDR"

echo "[1/3] Hilfsprogramm fuer VPN-Geraete installieren"
# Gehoert root und ist fuer den Dienstbenutzer nicht beschreibbar – sonst koennte der Dienst es veraendern und als root ausfuehren
install -o root -g root -m 755 "$HERE/einsatz-wg-peer" /usr/local/sbin/einsatz-wg-peer
SUDOERS=/etc/sudoers.d/einsatzueberwachung-vpn
echo "$APP_USER ALL=(root) NOPASSWD: /usr/local/sbin/einsatz-wg-peer" >"$SUDOERS.tmp"
chmod 440 "$SUDOERS.tmp"
visudo -cf "$SUDOERS.tmp" >/dev/null
mv "$SUDOERS.tmp" "$SUDOERS"
sudo -u "$APP_USER" sudo -n /usr/local/sbin/einsatz-wg-peer info >/dev/null && echo "  Dienstbenutzer darf das Hilfsprogramm ausfuehren."

if [[ -z "$DOMAIN" ]]; then
	echo "[2/3] DNS im VPN uebersprungen (keine Domain angegeben)"
else
	echo "[2/3] DNS im VPN: $DOMAIN -> $WG_IP"
	DEBIAN_FRONTEND=noninteractive apt-get install -y dnsmasq >/dev/null
	# dnsmasq lauscht nur im VPN und darf sich NICHT als DNS dieses Servers eintragen (sonst loest der Server
	# selbst keine Namen mehr auf, weil dnsmasq nicht auf 127.0.0.1 lauscht)
	if [[ -f /etc/default/dnsmasq ]]; then
		sed -i '/^DNSMASQ_EXCEPT=/d' /etc/default/dnsmasq
		echo 'DNSMASQ_EXCEPT="lo"' >>/etc/default/dnsmasq
	fi
	UPSTREAM=/etc/resolv.conf
	[[ -f /run/systemd/resolve/resolv.conf ]] && UPSTREAM=/run/systemd/resolve/resolv.conf
	cat >/etc/dnsmasq.d/einsatzueberwachung.conf <<EOF
# Einsatzueberwachung: DNS nur fuer Geraete im VPN ($WG_IF)
listen-address=$WG_IP
bind-interfaces
# /etc/hosts des Servers nicht verwenden (dort kann der eigene Name auf 127.0.0.1 zeigen)
no-hosts
# Im VPN zeigt die Domain auf den Server im VPN – gleiche Adresse wie von aussen, gueltiges Zertifikat
address=/$DOMAIN/$WG_IP
resolv-file=$UPSTREAM
EOF
	dnsmasq --test
	systemctl enable dnsmasq >/dev/null 2>&1
	systemctl restart dnsmasq
	if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
		ufw allow in on "$WG_IF" to any port 53 >/dev/null
		echo "  Firewall: DNS (53) im VPN freigegeben."
	fi
	systemctl is-active --quiet dnsmasq && echo "  dnsmasq laeuft."
fi

echo "[3/3] Hinweise"
cat <<EOF
  - In der Einsatzueberwachung: Einstellungen -> System -> VPN-Geraete. Adresse des VPN-Servers eintragen
    (z. B. ${DOMAIN:-<domain>}:$(wg show "$WG_IF" listen-port)), dann Geraete per QR-Code anlegen.
EOF
if [[ -n "$DOMAIN" ]]; then
	echo "  - Neue Geraete bekommen DNS = $WG_IP und erreichen https://$DOMAIN mit gueltigem Zertifikat."
else
	echo "  - Neue Geraete bekommen keinen DNS-Eintrag und erreichen den Server ueber $WG_IP."
fi
if [[ -n "$DOMAIN" ]]; then
	cat <<EOF
  - Damit https://$DOMAIN im VPN die volle Oberflaeche zeigt, die neue deploy/nginx/einsatzueberwachung-public.conf
    uebernehmen (VPN-Geraete -> Port 5000, alle anderen nur Team-Ansicht) und nginx neu laden:
      sudo nginx -t && sudo systemctl reload nginx
  - Pruefen von einem VPN-Geraet: nslookup $DOMAIN $WG_IP   (Antwort: $WG_IP)
EOF
fi
