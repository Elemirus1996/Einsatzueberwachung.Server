#!/usr/bin/env bash
# Test von einsatz-staffel ohne root (laeuft in der CI): Werte setzen und pruefen, Vorlagen fuellen.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/einsatz-staffel"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
export EINSATZ_STAFFEL_CONF="$T/staffel.conf"
FEHLER=0
ok() { echo "ok   $1"; }
bad() { echo "FEHLER $1"; FEHLER=1; }
st() { bash "$TOOL" "$@"; }

# Ohne Datei: pruefen meldet das, Standardwerte gelten
st pruefen >/dev/null 2>&1 && bad "pruefen ohne Datei" || ok "pruefen ohne Datei schlaegt fehl"
[[ "$(st wert vpn_netz)" == "10.10.0.0/24" && "$(st wert vpn_port)" == "51820" ]] && ok "Standardwerte" || bad "Standardwerte"
[[ "$(st wert vpn_server_ip)" == "10.10.0.1" ]] && ok "Server-IP aus Standardnetz" || bad "Server-IP"

# Ungueltige Werte werden abgelehnt und nicht gespeichert
for falsch in "domain staffel" "domain -x.de" "domain a_b.de" "vpn_netz 10.10.0.0/16" "vpn_netz 8.8.8.0/24" \
	"vpn_netz 172.32.1.0/24" "vpn_netz 10.300.0.0/24" "vpn_port 0" "vpn_port 70000" "vpn_port abc" "dns_anbieter cloudflare" \
	"gibtsnicht 1"; do
	# shellcheck disable=SC2086
	st setzen $falsch 2>/dev/null && bad "angenommen: $falsch" || true
done
[[ ! -f "$EINSATZ_STAFFEL_CONF" ]] && ok "ungueltige Werte abgelehnt" || bad "Datei trotz Fehler angelegt"

# Gueltige Werte, Domain in Kleinbuchstaben, andere Schluessel bleiben erhalten
st setzen domain Staffel-X.MyWire.org
st setzen vpn_netz 192.168.77.0/24
st setzen vpn_port 51999
st setzen dns_anbieter andere
st setzen vpn_netz 10.77.3.0/24
[[ "$(st wert domain)" == "staffel-x.mywire.org" ]] && ok "Domain klein gespeichert" || bad "Domain: $(st wert domain)"
[[ "$(st wert vpn_netz)" == "10.77.3.0/24" && "$(st wert vpn_port)" == "51999" && "$(st wert dns_anbieter)" == "andere" ]] \
	&& ok "Werte ueberschrieben und erhalten" || bad "Werte: $(st zeigen | tr '\n' ' ')"
[[ "$(st wert vpn_server_ip)" == "10.77.3.1" && "$(st wert intern_adresse)" == "el.staffel-x.mywire.org" ]] \
	&& ok "abgeleitete Werte" || bad "abgeleitet: $(st zeigen | tr '\n' ' ')"
st pruefen >/dev/null && ok "pruefen mit vollstaendiger Datei" || bad "pruefen: $(st pruefen)"
for netz in 172.16.0.0/24 172.31.255.0/24 192.168.0.0/24; do
	st setzen vpn_netz "$netz" 2>/dev/null || bad "privates Netz abgelehnt: $netz"
done
st setzen vpn_netz 10.77.3.0/24

# Datei wird gelesen, nicht ausgefuehrt; Fremdes faellt in pruefen auf
echo 'domain=$(touch '"$T"'/ausgefuehrt)' >>"$EINSATZ_STAFFEL_CONF"
st wert domain >/dev/null
[[ ! -e "$T/ausgefuehrt" ]] && ok "Datei wird nicht ausgefuehrt" || bad "Datei wurde ausgefuehrt"
st pruefen >/dev/null && bad "pruefen nimmt kaputte Domain" || ok "pruefen meldet kaputte Domain"
st setzen domain staffel-x.mywire.org
echo "vpn_netzz=1" >>"$EINSATZ_STAFFEL_CONF"
grep -q "unbekannter Schluessel 'vpn_netzz'" <<<"$(st pruefen || true)" && ok "pruefen meldet Tippfehler" || bad "Tippfehler nicht gemeldet"
st setzen domain staffel-x.mywire.org

# Vorlagen: keine Beispielwerte mehr, Nachbaradressen (10.10.0.10) bleiben unberuehrt
PUB="$(st vorlage "$HERE/../nginx/einsatzueberwachung-public.conf")"
INT="$(st vorlage "$HERE/../nginx/einsatzueberwachung.conf")"
grep -q 'dein-einsatz\|10\.10\.0' <<<"$PUB$INT" && bad "Beispielwerte in Vorlage uebrig" || ok "Beispielwerte ersetzt"
grep -q "server_name staffel-x.mywire.org;" <<<"$PUB" && grep -q "live/staffel-x.mywire.org/fullchain.pem" <<<"$PUB" \
	&& ok "Domain in public-Vorlage" || bad "Domain fehlt in public-Vorlage"
grep -qE '^\s+10\.77\.3\.0/24\s+1;' <<<"$PUB" && ok "VPN-Netz in public-Vorlage" || bad "VPN-Netz fehlt in public-Vorlage"
[[ "$(grep -c 'allow 10.77.3.0/24;' <<<"$INT")" == 2 ]] && ok "VPN-Netz in interner Vorlage" || bad "VPN-Netz in interner Vorlage"
printf 'a 10.10.0.1\nb 10.10.0.10\nc 10.10.0.1/24\n' >"$T/v"
[[ "$(st vorlage "$T/v" | tr '\n' ' ')" == "a 10.77.3.1 b 10.10.0.10 c 10.77.3.1/24 " ]] && ok "nur exakte Server-IP ersetzt" \
	|| bad "Ersetzung: $(st vorlage "$T/v" | tr '\n' ' ')"

# Vorlage mit Domain, aber keine Domain gesetzt: Abbruch statt Beispiel-Domain
printf 'vpn_netz=10.10.0.0/24\n' >"$EINSATZ_STAFFEL_CONF"
st vorlage "$HERE/../nginx/einsatzueberwachung-public.conf" >/dev/null 2>&1 && bad "Vorlage ohne Domain" || ok "Vorlage ohne Domain bricht ab"
st vorlage "$HERE/../nginx/einsatzueberwachung.conf" >/dev/null && ok "interne Vorlage braucht keine Domain" || bad "interne Vorlage ohne Domain"

exit $FEHLER
