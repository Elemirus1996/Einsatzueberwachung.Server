#!/usr/bin/env bash
# Tests fuer einsatz-wg-peer mit nachgebautem wg/ip (ohne root, ohne echtes WireGuard).
# Aufruf: bash deploy/scripts/test-einsatz-wg-peer.sh
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/bin" "$WORK/conf"
export EINSATZ_WG_TEST_CONF_DIR="$WORK/conf"
export FAKE_STATE="$WORK/peers"
: >"$FAKE_STATE"

# Nachbau von wg: Peers in einer Datei (pubkey<TAB>ip), Schluessel aus einem Zaehler
cat >"$WORK/bin/wg" <<'EOF'
#!/usr/bin/env bash
case "$1" in
	genkey) n=$(( $(wc -l <"$FAKE_STATE") + 1 )); printf 'PRIVATE%036d=\n' "$n" ;;
	pubkey) read -r priv; n="${priv//[!0-9]/}"; printf 'PUB+%039d=\n' "$((10#$n))" ;;
	show)
		case "$3" in
			public-key) echo "SERVERKEYSERVERKEYSERVERKEYSERVERKEYSERVERK=" ;;
			listen-port) echo 51820 ;;
			allowed-ips) awk -F'\t' '{print $1 "\t" $2 "/32"}' "$FAKE_STATE" ;;
			dump) printf 'priv\tpub\t51820\toff\n'; awk -F'\t' '{print $1 "\t(none)\t1.2.3.4:5\t" $2 "/32\t1792900000\t0\t0\toff"}' "$FAKE_STATE" ;;
		esac ;;
	set)
		if [[ "$5" == "remove" ]]; then grep -vF "$4" "$FAKE_STATE" >"$FAKE_STATE.tmp" || true; mv "$FAKE_STATE.tmp" "$FAKE_STATE"
		else printf '%s\t%s\n' "$4" "${6%/32}" >>"$FAKE_STATE"; fi ;;
esac
EOF
cat >"$WORK/bin/ip" <<'EOF'
#!/usr/bin/env bash
echo "5: wg0    inet 10.10.0.1/24 scope global wg0"
EOF
chmod +x "$WORK/bin/wg" "$WORK/bin/ip"
export PATH="$WORK/bin:$PATH"

cat >"$WORK/conf/wg0.conf" <<'EOF'
[Interface]
Address = 10.10.0.1/24
ListenPort = 51820
PrivateKey = SERVERPRIVATE

# Laptop von Hand eingetragen
[Peer]
PublicKey = MANUALMANUALMANUALMANUALMANUALMANUALMANUA=
AllowedIPs = 10.10.0.10/32
EOF

PEER="$HERE/einsatz-wg-peer"
ok() { echo "OK   $1"; }
bad() { echo "FEHLER $1"; exit 1; }

out="$(bash "$PEER" info)"
grep -q '^address=10.10.0.1/24$' <<<"$out" && grep -q '^listen_port=51820$' <<<"$out" && ok "info" || bad "info: $out"

a="$(bash "$PEER" add)"; ip1="$(sed -n 's/^ip=//p' <<<"$a")"; pub1="$(sed -n 's/^public_key=//p' <<<"$a")"
[[ "$ip1" == "10.10.0.11" ]] && ok "add nimmt erste freie Adresse (10.10.0.10 von Hand belegt)" || bad "add ip=$ip1"
grep -q '^private_key=' <<<"$a" && ok "add gibt privaten Schluessel einmal aus" || bad "kein private_key"
priv1="$(sed -n 's/^private_key=//p' <<<"$a")"
grep -qF "$priv1" "$WORK/conf/wg0.conf" && bad "privater Schluessel in der Konfiguration" || ok "privater Schluessel nicht gespeichert"
grep -qF "PublicKey = $pub1" "$WORK/conf/wg0.conf" && ok "Peer dauerhaft eingetragen" || bad "Peer fehlt in wg0.conf"

b="$(bash "$PEER" add)"; ip2="$(sed -n 's/^ip=//p' <<<"$b")"
[[ "$ip2" == "10.10.0.12" ]] && ok "zweites Geraet bekommt naechste Adresse" || bad "add2 ip=$ip2"

s="$(bash "$PEER" status)"
grep -qF "$pub1"$'\t'"10.10.0.11"$'\t'"1792900000" <<<"$s" && ok "status liefert Schluessel, IP, letzten Kontakt" || bad "status: $s"

bash "$PEER" remove "$pub1" >/dev/null
grep -qF "$pub1" "$WORK/conf/wg0.conf" && bad "Peer nach remove noch in wg0.conf" || ok "remove entfernt Peer aus der Konfiguration"
grep -qF "MANUALMANUAL" "$WORK/conf/wg0.conf" && grep -q '^\[Interface\]' "$WORK/conf/wg0.conf" && ok "andere Peers und [Interface] bleiben" || bad "Konfiguration beschaedigt"
grep -qF "$pub1" "$FAKE_STATE" && bad "Peer laeuft noch" || ok "remove sperrt Peer sofort aus"

c="$(bash "$PEER" add)"; ip3="$(sed -n 's/^ip=//p' <<<"$c")"
[[ "$ip3" == "10.10.0.11" ]] && ok "frei gewordene Adresse wird wiederverwendet" || bad "add3 ip=$ip3"

for args in "remove ../../etc/passwd" "remove abc" "remove" "add x" "info x" "status x" "rm -rf" "" ; do
	# shellcheck disable=SC2086
	if bash "$PEER" $args >/dev/null 2>&1; then bad "ungueltiger Aufruf angenommen: '$args'"; fi
done
ok "ungueltige Aufrufe werden abgelehnt"

echo "Alle Tests bestanden."
