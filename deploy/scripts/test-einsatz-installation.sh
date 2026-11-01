#!/usr/bin/env bash
# Test der Hilfsfunktionen des Installationsmanagers ohne root und ohne Netz (laeuft in der CI).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
EINSATZ_INSTALLATION_TEST=1
# shellcheck source=../einsatz-installation
source "$HERE/../einsatz-installation"
FEHLER=0
ok() { echo "ok   $1"; }
bad() { echo "FEHLER $1"; FEHLER=1; }

[[ "$(ip_zahl 10.10.0.1)" == 168427521 ]] && ok "ip_zahl" || bad "ip_zahl $(ip_zahl 10.10.0.1)"

# Ueberlappung von Netzen
ueberlappt 10.10.0.0/24 10.10.0.0/24 && ok "gleiches Netz" || bad "gleiches Netz"
ueberlappt 10.10.0.0/24 10.0.0.0/8 && ok "/24 in /8" || bad "/24 in /8"
ueberlappt 10.0.0.0/8 10.10.0.0/24 && ok "/8 enthaelt /24" || bad "/8 enthaelt /24"
ueberlappt 10.10.0.0/24 10.10.0.77 && ok "Einzeladresse im Netz" || bad "Einzeladresse"
ueberlappt 10.10.0.0/24 10.10.1.0/24 && bad "Nachbarnetz" || ok "Nachbarnetz ueberlappt nicht"
ueberlappt 192.168.234.0/24 192.168.0.0/16 && ok "192.168/16" || bad "192.168/16"
ueberlappt 172.29.77.0/24 172.16.0.0/12 && ok "172.16/12" || bad "172.16/12"
ueberlappt 10.10.0.0/24 172.16.0.0/12 && bad "fremde Bereiche" || ok "fremde Bereiche ueberlappen nicht"

# Belegte Netze aus ip-Ausgabe: ohne default, lo und wg0
ip() {
	case "$*" in
		"-4 route show") printf 'default via 192.168.178.1 dev eth0\n192.168.178.0/24 dev eth0 proto kernel\n10.77.0.0/24 dev wg0 proto kernel\n' ;;
		"-4 -o addr show") printf '1: lo    inet 127.0.0.1/8 scope host lo\n2: eth0    inet 192.168.178.20/24 brd x\n5: wg0    inet 10.77.0.1/24 scope global wg0\n' ;;
	esac
}
[[ "$(belegte_netze | tr '\n' ' ')" == "192.168.178.0/24 192.168.178.20/24 " ]] && ok "belegte Netze ohne default, lo, wg0" \
	|| bad "belegte Netze: $(belegte_netze | tr '\n' ' ')"
unset -f ip

# Wahl des VPN-Netzes
belegte_netze() { printf '%s\n' "${BELEGT[@]}"; }
BELEGT=(192.168.178.0/24 192.168.178.20/24)
[[ "$(freies_vpn_netz)" == 10.10.0.0/24 ]] && ok "Heimnetz 192.168.178: Standardnetz" || bad "Wahl $(freies_vpn_netz)"
BELEGT=(10.10.0.0/24 10.10.0.5/24)
[[ "$(freies_vpn_netz)" == 10.77.0.0/24 ]] && ok "Heimnetz 10.10.0: naechstes Netz" || bad "Wahl $(freies_vpn_netz)"
BELEGT=(10.0.0.0/8)
[[ "$(freies_vpn_netz)" == 172.29.77.0/24 ]] && ok "Firmennetz 10/8: 172.29.77" || bad "Wahl $(freies_vpn_netz)"
BELEGT=(0.0.0.0/1 128.0.0.0/1)
freies_vpn_netz >/dev/null && bad "alles belegt" || ok "alles belegt: kein Netz"

# Konfiguration der WireGuard-App wie im Server (VpnDevices.BuildClientConfig)
SOLL='[Interface]
PrivateKey = PRIV
Address = 10.77.0.10/32
DNS = 10.77.0.1

[Peer]
PublicKey = PUB
Endpoint = staffel-x.mywire.org:51820
AllowedIPs = 10.77.0.0/24
PersistentKeepalive = 25'
[[ "$(client_konfig PRIV 10.77.0.10 10.77.0.1 PUB staffel-x.mywire.org:51820 10.77.0.0/24)" == "$SOLL" ]] \
	&& ok "Geraete-Konfiguration mit DNS" || bad "Geraete-Konfiguration"
client_konfig PRIV 10.77.0.10 "" PUB e:1 n | grep -q '^DNS' && bad "DNS ohne dnsmasq" || ok "ohne DNS keine DNS-Zeile"

# Release-Antwort von GitHub
JSON='{"tag_name":"v1.14.0","assets":[{"name":"livetracking.zip","browser_download_url":"https://x/l.zip"},
 {"name":"linux-x64-server-1.14.0.zip","browser_download_url":"https://x/s.zip"}]}'
[[ "$(release_aus_json <<<"$JSON" | tr '\n' ' ')" == "v1.14.0 https://x/s.zip " ]] && ok "Release-Paket gefunden" || bad "Release"
release_aus_json <<<'{"tag_name":"v1","assets":[]}' >/dev/null 2>&1 && bad "Release ohne Paket" || ok "Release ohne Paket erkannt"

exit $FEHLER
