#!/usr/bin/env bash
# Verhaltenstest der oeffentlichen nginx-Vorlage (laeuft in der CI, braucht nginx, python3, openssl, sudo).
# Ein Echo-Server auf 5000/5002 meldet, welcher Port erreicht wurde und welche Kopfzeilen ankamen. Geprueft wird:
#  - jeder Pfad bekommt Host und X-Forwarded-Proto (sonst baut die App Weiterleitungen auf 127.0.0.1 – 1.10.0)
#  - von aussen nur Team-Pfade (Port 5002), „/“ ist 404
#  - Geraete aus dem VPN-Netz landen auf Port 5000 (volle Oberflaeche)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CONF="$HERE/../nginx/einsatzueberwachung-public.conf"
DOMAIN="$(awk '/server_name/ {gsub(";",""); print $2; exit}' "$CONF")"
T="$(mktemp -d)"
cleanup() { sudo nginx -c "$T/nginx.conf" -s stop 2>/dev/null || true; kill "$ECHO_PID" 2>/dev/null || true; rm -rf "$T"; }
trap cleanup EXIT

openssl req -x509 -newkey rsa:2048 -nodes -keyout "$T/key.pem" -out "$T/cert.pem" -subj "/CN=$DOMAIN" -days 1 2>/dev/null

# Echo-Server: antwortet mit Port, Host und X-Forwarded-Proto der Anfrage
cat >"$T/echo.py" <<'PY'
import http.server, socketserver, threading
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = f"port={self.server.server_address[1]} host={self.headers.get('Host')} proto={self.headers.get('X-Forwarded-Proto')}".encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
for port in (5000, 5002):
    s = socketserver.TCPServer(("127.0.0.1", port), H); threading.Thread(target=s.serve_forever, daemon=True).start()
threading.Event().wait()
PY
python3 "$T/echo.py" & ECHO_PID=$!

render() { # $1 = 1: 127.0.0.1 gilt als VPN-Geraet
	sed -e "s#/etc/letsencrypt/live/[^/]*/fullchain.pem#$T/cert.pem#; s#/etc/letsencrypt/live/[^/]*/privkey.pem#$T/key.pem#" "$CONF" >"$T/public.conf"
	if [[ "$1" == 1 ]]; then sed -i 's#^\(\s*\)default\s*0;#\1default 0;\n\1127.0.0.1/32 1;#' "$T/public.conf"; fi
	printf 'pid %s/nginx.pid;\nerror_log %s/error.log;\nevents {}\nhttp {\n  access_log off;\n  include %s/public.conf;\n}\n' "$T" "$T" "$T" >"$T/nginx.conf"
}
start() { sudo nginx -c "$T/nginx.conf" -s stop 2>/dev/null || true; sleep 0.5; sudo nginx -c "$T/nginx.conf"; sleep 0.5; }
get() { curl -sk --resolve "$DOMAIN:443:127.0.0.1" -o "$T/body" -w '%{http_code}' "https://$DOMAIN$1"; }

fail=0
expect() { # $1 Pfad, $2 erwarteter Port oder 404
	local code body; code="$(get "$1")"; body="$(cat "$T/body")"
	if [[ "$2" == 404 ]]; then
		[[ "$code" == 404 ]] && echo "OK   $1 -> 404" || { echo "FEHLER $1: erwartet 404, bekam $code"; fail=1; }
		return
	fi
	if [[ "$code" == 200 && "$body" == *"port=$2"* && "$body" == *"host=$DOMAIN"* && "$body" == *"proto=https"* ]]; then
		echo "OK   $1 -> $body"
	else
		echo "FEHLER $1: $code $body (erwartet port=$2 host=$DOMAIN proto=https)"; fail=1
	fi
}

echo "--- von aussen"
render 0; start
for p in /team/login /team /api/team-mobile/state /api/team-mobile/traccar /hubs/team-mobile/negotiate /_blazor/negotiate /_framework/blazor.web.js \
	/app.css /app.jlypqgwbzp.css /leaflet-interop.k3x9a1b2c3.js /Einsatzueberwachung.Server.os4ukl7apd.styles.css /css/layout.ab12cd34ef.css /js/layout-tools.ab12cd34ef.js /favicon.png; do
	expect "$p" 5002
done
expect / 404
expect /einstellungen 404
expect /downloads/app-settings.json 404
expect /css/../einstellungen 404

echo "--- aus dem VPN"
render 1; start
for p in / /einstellungen /leitung/anmelden /hubs/einsatz/negotiate /_blazor/negotiate /team/login; do expect "$p" 5000; done

[[ $fail == 0 ]] && echo "Alle nginx-Tests bestanden." || exit 1
