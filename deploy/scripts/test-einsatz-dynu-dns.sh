#!/usr/bin/env bash
# Test des certbot-Hooks einsatz-dynu-dns ohne root und ohne Netz (laeuft in der CI): nachgebautes curl
# beantwortet die Dynu-API und schreibt jeden Aufruf mit.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
printf 'api_key=geheim=123\n' >"$T/dynu.conf"

cat >"$T/curl" <<'SH'
#!/usr/bin/env bash
# Methode, Header-Datei, Koerper und URL mitschreiben; Antwort je nach Pfad
methode=GET; koerper=""; url=""; header=""
while [[ $# -gt 0 ]]; do
	case "$1" in
		-X) methode="$2"; shift 2 ;;
		-H) [[ "$2" == @* ]] && header="$(cat "${2#@}")"; shift 2 ;;
		--data) koerper="$2"; shift 2 ;;
		--max-time) shift 2 ;;
		-*) shift ;;
		*) url="$1"; shift ;;
	esac
done
echo "$methode $url $koerper" >>"$(dirname "$0")/aufrufe"
grep -q '^API-Key: geheim=123$' <<<"$header" || { echo "Header ohne API-Key" >&2; exit 22; }
case "$methode $url" in
	"GET https://api.dynu.com/v2/dns/getroot/el.dein-einsatz.mywire.org")
		echo '{"statusCode":200,"id":4711,"domainName":"dein-einsatz.mywire.org","hostname":"el.dein-einsatz.mywire.org","node":"el"}' ;;
	"POST https://api.dynu.com/v2/dns/4711/record") echo '{"statusCode":200,"id":99}' ;;
	"GET https://api.dynu.com/v2/dns/4711/record")
		echo '{"statusCode":200,"dnsRecords":[
			{"id":98,"nodeName":"_acme-challenge.el","recordType":"TXT","textData":"anderer-wert"},
			{"id":99,"nodeName":"_acme-challenge.el","recordType":"TXT","textData":"pruefwert"},
			{"id":5,"nodeName":"el","recordType":"A","ipv4Address":"10.10.0.1"}]}' ;;
	"DELETE https://api.dynu.com/v2/dns/4711/record/99") echo '{"statusCode":200}' ;;
	*) echo "unerwartet: $methode $url" >&2; exit 22 ;;
esac
SH
chmod +x "$T/curl"
export EINSATZ_DYNU_CONF="$T/dynu.conf" EINSATZ_DYNU_CURL="$T/curl" EINSATZ_DYNU_WARTEN=0
export CERTBOT_DOMAIN="el.dein-einsatz.mywire.org" CERTBOT_VALIDATION="pruefwert"
TOOL="$HERE/einsatz-dynu-dns"
fail=0
pruefe() { # $1 Beschreibung, $2 erwartetes Muster, $3 Text
	if grep -qF -- "$2" <<<"$3"; then echo "OK   $1"; else echo "FEHLER $1: erwartet '$2', bekam:"; echo "$3"; fail=1; fi
}

AUSGABE="$(bash "$TOOL" auth)"
pruefe "auth meldet Eintrag" "TXT _acme-challenge.el.dein-einsatz.mywire.org gesetzt" "$AUSGABE"
pruefe "auth legt TXT an" 'POST https://api.dynu.com/v2/dns/4711/record {"nodeName": "_acme-challenge.el", "recordType": "TXT", "textData": "pruefwert", "state": true, "ttl": 90}' "$(cat "$T/aufrufe")"
if grep -q geheim <<<"$AUSGABE$(cat "$T/aufrufe")"; then echo "FEHLER Key in Ausgabe oder Befehlszeile"; fail=1; else echo "OK   Key weder in Ausgabe noch Befehlszeile"; fi

: >"$T/aufrufe"
bash "$TOOL" cleanup
pruefe "cleanup loescht passenden Eintrag" "DELETE https://api.dynu.com/v2/dns/4711/record/99" "$(cat "$T/aufrufe")"
if grep -q "record/98\|record/5" "$T/aufrufe"; then echo "FEHLER cleanup loescht fremde Eintraege"; fail=1; else echo "OK   cleanup laesst fremde Eintraege"; fi

if CERTBOT_DOMAIN='el.x;rm -rf /' bash "$TOOL" auth 2>/dev/null; then echo "FEHLER ungueltige Domain angenommen"; fail=1; else echo "OK   ungueltige Domain abgelehnt"; fi
if EINSATZ_DYNU_CONF="$T/fehlt" bash "$TOOL" auth 2>/dev/null; then echo "FEHLER ohne Konfiguration"; fail=1; else echo "OK   ohne Konfiguration abgebrochen"; fi

exit $fail
