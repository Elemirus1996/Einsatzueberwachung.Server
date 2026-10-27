#!/usr/bin/env bash
# Test des Hilfsprogramms einsatz-zertifikat ohne root und ohne echtes certbot (laeuft in der CI).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
mkdir -p "$T/webroot"
printf 'domain=einsatz.example.org\nwebroot=%s\n' "$T/webroot" >"$T/zertifikat.conf"

# Nachgebautes certbot: Verhalten ueber FAKE_CERTBOT, Aufruf wird mitgeschrieben
cat >"$T/certbot" <<'SH'
#!/usr/bin/env bash
echo "$@" >>"$(dirname "$0")/aufrufe"
case "$FAKE_CERTBOT" in
	neu) echo "Successfully received certificate."; exit 0 ;;
	alt) echo "Certificate not yet due for renewal; no action taken."; exit 0 ;;
	*)   echo "Challenge failed for domain einsatz.example.org"; exit 1 ;;
esac
SH
chmod +x "$T/certbot"
export EINSATZ_ZERT_CONF="$T/zertifikat.conf" EINSATZ_ZERT_CERTBOT="$T/certbot"
TOOL="$HERE/einsatz-zertifikat"
fail=0
pruefe() { # $1 Beschreibung, $2 erwartete Zeile, $3 Ausgabe
	if grep -qx "$2" <<<"$3"; then echo "OK   $1"; else echo "FEHLER $1: erwartet '$2', bekam:"; echo "$3"; fail=1; fi
}

pruefe "info"            "domain=einsatz.example.org" "$(bash "$TOOL" info)"
pruefe "erneuert"        "ergebnis=erneuert"          "$(FAKE_CERTBOT=neu bash "$TOOL" erneuern)"
pruefe "nicht faellig"   "ergebnis=nicht_faellig"     "$(FAKE_CERTBOT=alt bash "$TOOL" erneuern)"
pruefe "fehler"          "ergebnis=fehler"            "$(FAKE_CERTBOT=kaputt bash "$TOOL" erneuern || true)"
grep -q -- "--webroot -w $T/webroot -d einsatz.example.org --cert-name einsatz.example.org" "$T/aufrufe" \
	&& echo "OK   certbot mit Webroot und Domain aus der Konfiguration" || { echo "FEHLER certbot-Aufruf: $(cat "$T/aufrufe")"; fail=1; }

# Freie Argumente werden nicht durchgereicht
pruefe "unbekannter Befehl" "" "$(bash "$TOOL" 'erneuern; rm -rf /' 2>/dev/null || true)"

# Kaputte Konfiguration
printf 'domain=bad domain;rm\nwebroot=%s\n' "$T/webroot" >"$T/zertifikat.conf"
pruefe "ungueltige Domain" "ergebnis=fehler" "$(bash "$TOOL" erneuern 2>/dev/null || true)"

[[ $fail == 0 ]] && echo "Alle Tests fuer einsatz-zertifikat bestanden." || exit 1
