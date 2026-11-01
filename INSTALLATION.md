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
      `51820/udp` (WireGuard). Bei einem Mietserver entfällt das, die Firewall richtet `setup.sh` ein.
- [ ] **Domain bei Dynu:** Ein kostenloser Hostname bei [Dynu](https://www.dynu.com) (z. B. `….mywire.org`) mit
      API-Key. Bei wechselnder IP die DDNS-Aktualisierung im Router oder mit dem Dynu-Client einrichten. Andere
      DNS-Anbieter sind für die interne Adresse (Schritt 7) noch nicht vorgesehen, siehe „Bekannte Lücken“.
- [ ] **VPN-Netz frei:** Das VPN nutzt fest `10.10.0.0/24`. Das Heimnetz am Server darf diesen Bereich nicht nutzen.
- [ ] **Passwort der Einsatzleitung** festlegen: gemeinsam für die Staffel, mindestens 10 Zeichen.
- [ ] **Geräte:** WireGuard-App auf allen Geräten der Einsatzleitung (Laptop, Tablet, Handy).

## 1. Server vorbereiten

```bash
sudo apt-get update && sudo apt-get install -y git certbot python3 wireguard
git clone https://github.com/Elemirus1996/Einsatzueberwachung.Server.git
cd Einsatzueberwachung.Server
git checkout v<version>
```

Aus dem GitHub-Release `v<version>` die Datei `linux-x64-server-<version>.zip` herunterladen und entpacken:

```bash
mkdir -p publish/server
unzip linux-x64-server-<version>.zip -d publish/server
```

## 2. Vorläufiges Zertifikat für die interne Adresse

`setup.sh` erwartet ein Zertifikat unter `/etc/ssl/certs/einsatz.crt`, sonst bricht `nginx -t` ab. Für den Anfang
reicht ein selbst ausgestelltes. Es wird in Schritt 7 durch ein gültiges ersetzt.

```bash
sudo openssl req -x509 -nodes -newkey rsa:2048 -days 30 -subj "/CN=einsatz" \
  -keyout /etc/ssl/private/einsatz.key -out /etc/ssl/certs/einsatz.crt
```

## 3. Grundinstallation

```bash
sudo bash deploy/setup.sh
```

Installiert nginx, chrony und die .NET-Laufzeit. Legt den Benutzer `einsatz` und `/opt/einsatzueberwachung`
an, kopiert `publish/server` dorthin, aktiviert Dienst, Backup (täglich 03:30) und Healthcheck und richtet die
Firewall ein (22, 443, 51820/udp).

Der Server braucht die **ASP.NET Core Runtime 9** (`aspnetcore-runtime-9.0`). `setup.sh` installiert nur
`dotnet-runtime-9.0`. Startet der Dienst nicht (`systemctl status einsatzueberwachung-server.service`), zusätzlich
installieren:

```bash
sudo apt-get install -y aspnetcore-runtime-9.0
sudo systemctl restart einsatzueberwachung-server.service
```

Findet apt das Paket nicht, die Paketquelle von Microsoft für Ubuntu einrichten (Microsoft-Doku „.NET unter Ubuntu
installieren“).

## 4. WireGuard einrichten

```bash
wg genkey | sudo tee /etc/wireguard/server.key | wg pubkey | sudo tee /etc/wireguard/server.pub
sudo chmod 600 /etc/wireguard/server.key
```

`/etc/wireguard/wg0.conf` anlegen, nur der `[Interface]`-Teil aus `deploy/wireguard/wg0.conf.example`:

```ini
[Interface]
Address = 10.10.0.1/24
ListenPort = 51820
PrivateKey = <Inhalt von /etc/wireguard/server.key>
```

```bash
sudo chmod 600 /etc/wireguard/wg0.conf
sudo systemctl enable --now wg-quick@wg0
sudo wg show wg0                      # muss "listening port: 51820" zeigen
```

Geräte werden nicht von Hand eingetragen, sondern in Schritt 9 per QR-Code.

## 5. VPN-Geräte und DNS im VPN

```bash
sudo bash deploy/scripts/setup-vpn.sh <domain>
```

Installiert das Hilfsprogramm für VPN-Geräte (QR-Code aus der Oberfläche) und `dnsmasq`, das nur im VPN antwortet:
Dort zeigt `<domain>` auf `10.10.0.1`, Geräte im VPN bekommen so die volle Oberfläche.

## 6. Öffentliche Domain und Team-Zugang

Bei Dynu muss `<domain>` auf die öffentliche IPv4 zeigen (DDNS aus Schritt 0).

Erstes Zertifikat ausstellen. nginx kurz anhalten, weil certbot dafür selbst Port 80 braucht:

```bash
sudo ufw allow 80/tcp
sudo systemctl stop nginx
sudo certbot certonly --standalone -d <domain> --cert-name <domain>
sudo systemctl start nginx
```

nginx-Konfiguration für den Team-Zugang übernehmen. Die Vorlage enthält die Beispiel-Domain `dein-einsatz.mywire.org`,
die hier ersetzt wird:

```bash
sed "s/dein-einsatz\.mywire\.org/<domain>/g" deploy/nginx/einsatzueberwachung-public.conf \
  | sudo tee /etc/nginx/sites-available/einsatzueberwachung-public >/dev/null
sudo ln -sf /etc/nginx/sites-available/einsatzueberwachung-public /etc/nginx/sites-enabled/einsatzueberwachung-public
sudo nginx -t && sudo systemctl reload nginx
```

Verlängerung auf den Webroot-Weg umstellen und den Knopf „Jetzt erneuern“ freischalten:

```bash
sudo bash deploy/scripts/setup-zertifikat.sh <domain>
sudo certbot renew --dry-run          # muss „Congratulations, all simulated renewals succeeded“ melden
```

Prüfen, von außen (Handy ohne WLAN, ohne VPN): `https://<domain>/team` zeigt die Team-Ansicht.

## 7. Gültiges Zertifikat für die interne Adresse

Die EL-App lehnt das vorläufige Zertifikat aus Schritt 2 ab.

1. Bei Dynu (DDNS → Hostname → DNS Records) einen A-Eintrag anlegen: Node Name `el`, IPv4 `10.10.0.1`.
2. Bei Dynu unter „API Credentials“ den API-Key nachsehen.
3. Auf dem Server:

```bash
sudo bash deploy/scripts/setup-intern-zertifikat.sh el.<domain>    # fragt einmal nach dem API-Key
sudo certbot renew --dry-run --cert-name el.<domain>
```

Das Skript holt das Zertifikat über einen TXT-Eintrag bei Dynu und ersetzt `/etc/ssl/certs/einsatz.crt`. Der Server
muss dafür nicht von außen erreichbar sein.

## 8. Erstes Gerät und Passwort der Einsatzleitung

Die interne Oberfläche ist nur aus dem VPN erreichbar (nginx lässt nur `10.10.0.0/24` zu). QR-Codes für Geräte gibt es
erst nach der Anmeldung, das erste Gerät (Laptop der Einsatzleitung) wird deshalb einmalig von Hand eingetragen.

Auf dem Server:

```bash
wg genkey | tee laptop.key | wg pubkey > laptop.pub
sudo wg set wg0 peer "$(cat laptop.pub)" allowed-ips 10.10.0.2/32
sudo wg-quick save wg0
cat /etc/wireguard/server.pub
```

In der WireGuard-App des Laptops einen neuen Tunnel anlegen:

```ini
[Interface]
PrivateKey = <Inhalt von laptop.key>
Address = 10.10.0.2/32
DNS = 10.10.0.1

[Peer]
PublicKey = <Inhalt von server.pub>
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

- Die Schritte 2, 4, 6 (erstes Zertifikat, nginx-Vorlage) und die zusätzliche ASP.NET-Laufzeit macht `setup.sh` noch
  nicht selbst.
- Das interne Zertifikat (Schritt 7) geht nur mit Dynu. Bei anderem DNS-Anbieter bleibt nur die Ausstellung von Hand
  (`certbot certonly --manual --preferred-challenges dns -d el.<domain>`), die alle 90 Tage wiederholt werden muss.
- Das VPN-Netz `10.10.0.0/24` steht fest in `wg0.conf.example`, beiden nginx-Dateien und `setup-vpn.sh`.
- Ein vorhandenes VPN (z. B. im Router) statt WireGuard auf dem Server ist nicht vorgesehen.
- Diese Anleitung ist noch nicht auf einem frischen Server durchgespielt worden.
