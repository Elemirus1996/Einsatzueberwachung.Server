# Neuinstallation für eine Staffel

Diese Anleitung richtet die Einsatzüberwachung auf einem neuen Server ein, Schritt für Schritt in dieser Reihenfolge.
Für Updates eines bestehenden Servers und Hintergründe siehe [DEPLOYMENT.md](DEPLOYMENT.md).

Vorausgesetzt werden Grundkenntnisse in Linux (SSH, `sudo`, Dateien bearbeiten) und Zugang zum Router der Staffel.

Platzhalter in dieser Anleitung:

| Platzhalter | Bedeutung | Beispiel |
| --- | --- | --- |
| `<domain>` | öffentliche Adresse der Staffel, darüber erreichen die Teams `/team` | `staffel-musterstadt.mywire.org` |
| `el.<domain>` | interne Adresse für Einsatzleitung und EL-App, nur im VPN erreichbar | `el.staffel-musterstadt.mywire.org` |
| `<version>` | Version des Servers aus den GitHub-Releases | `1.13.0` |

## 0. Vorher klären (Checkliste)

Erst wenn alle Punkte erfüllt sind, mit Schritt 1 beginnen.

- [ ] **Rechner:** eigener Rechner oder Mietserver (VPS) mit Ubuntu Server 22.04 oder 24.04, 64 Bit (x86_64), dauerhaft
      eingeschaltet. Der Server ist nur für `linux-x64` gebaut, ein Raspberry Pi (ARM) geht nicht.
- [ ] **Öffentliche IPv4-Adresse:** Der Internetanschluss braucht eine eigene öffentliche IPv4. Bei DS-Lite oder CGNAT
      (häufig bei Kabel, Glasfaser und Starlink) geht es nicht. Erkennbar daran, dass der Router keine IPv4 oder eine aus
      `100.64.0.0/10` als WAN-Adresse anzeigt, oder dass sie von der Adresse abweicht, die eine „Wie ist meine
      IP“-Webseite anzeigt. Ohne öffentliche IPv4: Server auf einem Mietserver betreiben oder beim Anbieter eine öffentliche
      IPv4 bestellen.
- [ ] **Portfreigaben im Router** auf den Server: `443/tcp` (Team-Zugang), `80/tcp` (Verlängerung des Zertifikats),
      `51820/udp` (WireGuard). Bei einem Mietserver entfällt das, dort aber dieselben Ports in der Firewall im
      Kundenmenü des Anbieters freigeben (z. B. Strato, IONOS, Hetzner), besonders `51820/udp`.
- [ ] **Domain bei Dynu:** Ein kostenloser Hostname bei [Dynu](https://www.dynu.com) (z. B. `….mywire.org`) mit
      API-Key (empfohlen). Bei wechselnder IP die DDNS-Aktualisierung im Router oder mit dem Dynu-Client einrichten.
      Andere DNS-Anbieter gehen auch, dann muss das interne Zertifikat alle 90 Tage von Hand erneuert werden.
- [ ] **Kein fremder IPv6-Eintrag:** Viele Domain-Anbieter setzen selbst einen AAAA-Eintrag. Zeigt er nicht auf den
      Server, erreichen Handys im Mobilfunk weder VPN noch Team-Zugang. Löschen oder auf die IPv6 des Servers setzen.
- [ ] **VPN-Netz frei:** Das VPN nutzt `10.10.0.0/24`. Nutzt das Heimnetz am Server diesen Bereich schon, in Schritt 2
      ein anderes Netz wählen (z. B. `10.77.0.0/24`). Der Installationsmanager wählt selbst ein freies Netz.
- [ ] **Passwort der Einsatzleitung** festlegen: gemeinsam für die Staffel, mindestens 10 Zeichen.
- [ ] **Geräte:** WireGuard-App auf allen Geräten der Einsatzleitung (Laptop, Tablet, Handy).

## Installation mit dem Installationsmanager (empfohlen)

Der Installationsmanager führt durch alle Schritte unten. Was der Server selbst kann, erledigt er. Für alles andere
(Portfreigaben im Router, Einträge beim DNS-Anbieter) sagt er, was zu tun ist, wartet und prüft danach. Er kann
jederzeit abgebrochen (Strg+C) und erneut gestartet werden, erledigte Schritte erkennt er.

Auf dem frischen Server (per SSH angemeldet):

```bash
curl -fsSLO https://raw.githubusercontent.com/Elemirus1996/Einsatzueberwachung.Server/main/deploy/einsatz-installation
sudo bash einsatz-installation
```

Er holt das neueste Release von GitHub, prüft Prozessor, Ubuntu-Version, Speicher, öffentliche IPv4 und
Domain, wählt ein VPN-Netz, das sich nicht mit dem Heimnetz überschneidet, und richtet Server, WireGuard,
Zertifikate und das erste VPN-Gerät (QR-Code im Terminal) ein. Am Ende zeigt er Einrichtungscode und Adressen.

Zustand später prüfen, ohne etwas zu ändern, und ein weiteres VPN-Gerät anlegen (QR-Code oder Text für einen PC):

```bash
sudo bash einsatz-installation pruefen
sudo bash einsatz-installation geraet
```

Die folgenden Schritte beschreiben dasselbe von Hand, zum Nachvollziehen oder bei Problemen.

## 1. Server vorbereiten

```bash
sudo apt-get update && sudo apt-get install -y git unzip
git clone https://github.com/Elemirus1996/Einsatzueberwachung.Server.git
cd Einsatzueberwachung.Server
git checkout v<version>
```

Aus dem GitHub-Release `v<version>` die Datei `linux-x64-server-<version>.zip` herunterladen und entpacken:

```bash
mkdir -p publish/server
unzip linux-x64-server-<version>.zip -d publish/server
```

## 2. Einstellungen der Staffel

Alle Skripte lesen Domain und VPN-Netz aus `/etc/einsatzueberwachung/staffel.conf`. Anlegen mit:

```bash
sudo bash deploy/scripts/einsatz-staffel setzen domain <domain>
sudo bash deploy/scripts/einsatz-staffel zeigen
```

Nur wenn nötig (siehe Checkliste):

```bash
sudo bash deploy/scripts/einsatz-staffel setzen vpn_netz 10.77.0.0/24      # Heimnetz nutzt schon 10.10.0.x
sudo bash deploy/scripts/einsatz-staffel setzen vpn_port 51821             # 51820 schon belegt
```

`zeigen` gibt auch die abgeleiteten Werte aus: `vpn_server_ip` (VPN-Adresse des Servers, Standard `10.10.0.1`) und
`intern_adresse` (`el.<domain>`). In dieser Anleitung steht `<vpn-ip>` für `vpn_server_ip`.

## 3. Grundinstallation

```bash
sudo bash deploy/setup.sh
```

Installiert nginx, chrony, certbot, WireGuard und die ASP.NET-Core-Laufzeit 9 (bei Bedarf aus der Paketquelle
`dotnet/backports`). Legt den Benutzer `einsatz` und `/opt/einsatzueberwachung` an, kopiert `publish/server` dorthin,
aktiviert Dienst, Backup (täglich 03:30) und Healthcheck, stellt ein vorläufiges Zertifikat für die interne Adresse aus
(wird in Schritt 7 ersetzt), legt `/etc/wireguard/wg0.conf` an und richtet die Firewall ein (22, 80, 443 und den
VPN-Port per UDP). Bricht ab, wenn die Einstellungen aus Schritt 2 fehlen oder ungültig sind.

Prüfen:

```bash
systemctl status einsatzueberwachung-server.service
sudo wg show wg0                      # muss "listening port: 51820" (bzw. vpn_port) zeigen
```

## 4. WireGuard

`setup.sh` hat WireGuard schon eingerichtet. Eine vorhandene `/etc/wireguard/wg0.conf` lässt es unverändert und warnt,
wenn Adresse oder Port nicht zu `staffel.conf` passen. Geräte werden nicht von Hand eingetragen, sondern in Schritt 9
per QR-Code.

## 5. VPN-Geräte und DNS im VPN

```bash
sudo bash deploy/scripts/setup-vpn.sh
```

Installiert das Hilfsprogramm für VPN-Geräte (QR-Code aus der Oberfläche) und `dnsmasq`, das nur im VPN antwortet:
Dort zeigt `<domain>` auf `<vpn-ip>`, Geräte im VPN bekommen so die volle Oberfläche.

## 6. Öffentliche Domain und Team-Zugang

Bei Dynu muss `<domain>` auf die öffentliche IPv4 zeigen (DDNS aus Schritt 0).

Erstes Zertifikat ausstellen. nginx kurz anhalten, weil certbot dafür selbst Port 80 braucht:

```bash
sudo systemctl stop nginx
sudo certbot certonly --standalone -d <domain> --cert-name <domain>
sudo systemctl start nginx
```

nginx-Konfiguration für den Team-Zugang aus der Vorlage übernehmen, `einsatz-staffel` setzt Domain und VPN-Netz ein:

```bash
sudo einsatz-staffel vorlage deploy/nginx/einsatzueberwachung-public.conf \
  | sudo tee /etc/nginx/sites-available/einsatzueberwachung-public >/dev/null
sudo ln -sf /etc/nginx/sites-available/einsatzueberwachung-public /etc/nginx/sites-enabled/einsatzueberwachung-public
sudo nginx -t && sudo systemctl reload nginx
```

Verlängerung auf den Webroot-Weg umstellen und den Knopf „Jetzt erneuern“ freischalten:

```bash
sudo bash deploy/scripts/setup-zertifikat.sh
sudo certbot renew --dry-run          # muss „Congratulations, all simulated renewals succeeded“ melden
```

Prüfen, von außen (Handy ohne WLAN, ohne VPN): `https://<domain>/team` zeigt die Team-Ansicht.

## 7. Gültiges Zertifikat für die interne Adresse

Die EL-App lehnt das vorläufige Zertifikat aus Schritt 3 ab.

1. Bei Dynu (DDNS → Hostname → DNS Records) einen A-Eintrag anlegen: Node Name `el`, IPv4 `<vpn-ip>`.
2. Bei Dynu unter „API Credentials“ den API-Key nachsehen.
3. Auf dem Server:

```bash
sudo bash deploy/scripts/setup-intern-zertifikat.sh                # fragt einmal nach dem API-Key
sudo certbot renew --dry-run --cert-name el.<domain>
```

Das Skript holt das Zertifikat für `el.<domain>` über einen TXT-Eintrag bei Dynu und ersetzt
`/etc/ssl/certs/einsatz.crt`. Der Server muss dafür nicht von außen erreichbar sein.

## 8. Erstes Gerät und Passwort der Einsatzleitung

Die interne Oberfläche ist nur aus dem VPN erreichbar (nginx lässt nur `vpn_netz` zu, Standard `10.10.0.0/24`; die
Beispiele unten nutzen das Standardnetz). QR-Codes für Geräte gibt es
erst nach der Anmeldung, das erste Gerät (Laptop der Einsatzleitung) wird deshalb einmalig von Hand eingetragen.

Auf dem Server:

```bash
wg genkey | tee laptop.key | wg pubkey > laptop.pub
sudo wg set wg0 peer "$(cat laptop.pub)" allowed-ips 10.10.0.2/32
sudo wg-quick save wg0
sudo wg show wg0 public-key           # öffentlicher Schlüssel des Servers
```

In der WireGuard-App des Laptops einen neuen Tunnel anlegen:

```ini
[Interface]
PrivateKey = <Inhalt von laptop.key>
Address = 10.10.0.2/32
DNS = 10.10.0.1

[Peer]
PublicKey = <öffentlicher Schlüssel des Servers>
Endpoint = <domain>:51820
AllowedIPs = 10.10.0.0/24
```

Danach `laptop.key` auf dem Server löschen (`shred -u laptop.key laptop.pub`). Tunnel einschalten, im Browser
`https://el.<domain>` öffnen. Es erscheint „Erstes Einrichten“. Den Einrichtungscode zeigt der Server an:

```bash
sudo cat /opt/einsatzueberwachung/data/LEITUNG-EINRICHTUNGSCODE.txt
```

Code und Passwort (mindestens 10 Zeichen) eingeben. Danach ist der Code verbraucht.

## 9. Geräte anbinden

- **VPN-Geräte:** *Einstellungen → System → VPN-Geräte*: als Adresse des VPN-Servers `<domain>:51820` eintragen,
  Gerätename und Gültigkeit wählen, Passwort bestätigen, QR-Code mit der WireGuard-App scannen. Für jedes Gerät der
  Einsatzleitung wiederholen.
- **Browser der Einsatzleitung** (WireGuard an): `https://el.<domain>`.
- **EL-App** (iOS/Android, WireGuard an): Adresse `el.<domain>`, nicht `<domain>` und nicht `…:51820`.
- **Teams:** `https://<domain>/team`, ohne VPN.

## 10. Abnahme

Abnahme nach [DEPLOYMENT.md, Abschnitt 10](DEPLOYMENT.md#10-abnahme-desktop--mobile-mobile), danach einmal ein Backup
prüfen (Abschnitt 5 dort).

## Bekannte Lücken

Diese Punkte sind noch nicht gelöst und werden in den Skripten nachgezogen:

- Der Installationsmanager braucht ein Release, das ihn enthält (ab der nächsten Version nach 1.13.0).
- Das interne Zertifikat (Schritt 7) verlängert sich nur mit Dynu selbst. Bei anderem DNS-Anbieter
  (`einsatz-staffel setzen dns_anbieter andere`) fragt `setup-intern-zertifikat.sh` nach einem TXT-Eintrag von Hand,
  alle 90 Tage erneut.
- Ein vorhandenes VPN (z. B. im Router) statt WireGuard auf dem Server ist nicht vorgesehen.
- Diese Anleitung ist noch nicht auf einem frischen Server durchgespielt worden.
