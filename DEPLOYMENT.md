# Deployment auf Ubuntu (VPN-intern)

## 1. Voraussetzungen

- Ubuntu Server 22.04 oder 24.04
- VPN (z. B. WireGuard) aktiv
- DNS/Hosts-Eintrag fuer `einsatz.vpn.local`
- TLS-Zertifikat vorhanden (`/etc/ssl/certs/einsatz.crt`, `/etc/ssl/private/einsatz.key`)

## 2. Build und Publish

Fuehre lokal oder im CI aus:

```bash
dotnet publish src/Einsatzueberwachung.Server -c Release -o publish/server
dotnet publish src/Einsatzueberwachung.Mobile -c Release -o publish/mobile
```

Kopiere Repository inkl. `publish/` auf den Zielserver.

## 3. Setup ausfuehren

```bash
sudo bash deploy/setup.sh
```

Das Skript installiert Runtime/Nginx, kopiert Artefakte, aktiviert systemd und UFW.

Zusatzfunktionen nach Setup:

- taegliches Backup um 03:30 Uhr via systemd timer
- Healthcheck alle 2 Minuten mit automatischem Neustart fehlerhafter Dienste
- persistente Laufzeitdaten unter `/opt/einsatzueberwachung/data`
- PDF-Berichte unter `/opt/einsatzueberwachung/data/berichte`
- serverseitiger Updater via `deploy/scripts/apply-update.sh`
- aktive Zeit-Synchronisierung via `chrony` (NTP)

## 4. Dienste pruefen

```bash
sudo systemctl status einsatzueberwachung-server.service
sudo systemctl status einsatzueberwachung-mobile.service
sudo systemctl status nginx
sudo systemctl status einsatzueberwachung-backup.timer
sudo systemctl status einsatzueberwachung-healthcheck.timer
```

Zeit-Synchronisierung pruefen:

```bash
timedatectl status
timedatectl show -p NTPSynchronized -p SystemClockSynchronized --value
chronyc tracking
```

Erwartung:

- `NTPSynchronized=yes`
- `SystemClockSynchronized=yes`
- `chronyc tracking` liefert valide Daten

## 5. Backup und Restore

Manuelles Backup ausfuehren:

```bash
sudo /opt/einsatzueberwachung/scripts/backup.sh
```

Backups liegen unter `/opt/einsatzueberwachung/backups/`.

Wichtige Laufzeitordner:

```bash
/opt/einsatzueberwachung/data
/opt/einsatzueberwachung/data/berichte
```

Restore (Beispiel):

```bash
sudo systemctl stop einsatzueberwachung-server.service einsatzueberwachung-mobile.service
sudo mkdir -p /tmp/einsatz-restore
sudo tar -xzf /opt/einsatzueberwachung/backups/einsatz-backup-YYYYMMDD-HHMMSS.tar.gz -C /tmp/einsatz-restore
sudo rsync -a /tmp/einsatz-restore/server/ /opt/einsatzueberwachung/server/
sudo rsync -a /tmp/einsatz-restore/databases/ /opt/einsatzueberwachung/
sudo systemctl start einsatzueberwachung-server.service einsatzueberwachung-mobile.service
```

## 6. WireGuard Beispiel

Beispielkonfiguration in `deploy/wireguard/wg0.conf.example`.

Das VPN-Netz ist `10.10.0.0/24` und muss zu `allow 10.10.0.0/24` in `deploy/nginx/einsatzueberwachung.conf` passen.

## 7. Nginx Anpassungen

Anwendungskonfiguration in `deploy/nginx/einsatzueberwachung.conf`.

### Oeffentlicher Team-Zugang (optional)

`deploy/nginx/einsatzueberwachung-public.conf` macht nur die Team-Mobile-Ansicht (`/team`) aus dem Internet erreichbar.
Ab Version 1.9.3 leitet dieser Block auf einen eigenen Port (5002). Ein reiner Pfadfilter in Nginx reicht bei Blazor
Server nicht aus: Ueber die Blazor-Verbindung (`/_blazor`) kann ein Browser jede Seite aufrufen, ohne dass Nginx den Pfad
sieht. Die App erkennt Anfragen auf Port 5002 und zeigt dort nur die Team-Seiten.

**Einmalig bei bestehenden Servern** (ein App-Update aendert die systemd-Unit nicht):

```bash
sudo systemctl edit einsatzueberwachung-server.service
```

```ini
[Service]
Environment="ASPNETCORE_URLS=http://127.0.0.1:5000;http://127.0.0.1:5002"
Environment=EINSATZUEBERWACHUNG_PUBLIC_PORT=5002
```

Danach die neue `einsatzueberwachung-public.conf` (Upstream `127.0.0.1:5002`) nach `/etc/nginx/sites-available/` kopieren und:

```bash
sudo systemctl restart einsatzueberwachung-server.service
sudo journalctl -u einsatzueberwachung-server.service | grep "Oeffentlicher Team-Zugang"   # muss Port 5002 melden
sudo nginx -t && sudo systemctl reload nginx
```

Beide Umgebungsvariablen immer gemeinsam setzen: Lauscht Kestrel auf 5002, ohne dass `EINSATZUEBERWACHUNG_PUBLIC_PORT`
gesetzt ist, gilt der Port als intern. Fehlt umgekehrt der Port in `ASPNETCORE_URLS`, antwortet der oeffentliche Block mit
502 – dann ist nichts oeffentlich erreichbar.

### Anmeldung der Einsatzleitung (ab 1.10)

Alle Seiten und Schnittstellen außer der Team-Ansicht verlangen eine Anmeldung mit dem gemeinsamen Passwort der Staffel.
Die Anmeldung ist nur über das VPN erreichbar, nicht über den öffentlichen Team-Zugang.

**Erstes Einrichten** (nach der Installation oder dem Update auf 1.10): Im Browser die Einsatzüberwachung öffnen – es
erscheint „Erstes Einrichten“. Den Einrichtungscode zeigt der Server an:

```bash
sudo journalctl -u einsatzueberwachung-server.service | grep Einrichtungscode
# oder
sudo cat /opt/einsatzueberwachung/data/LEITUNG-EINRICHTUNGSCODE.txt
```

Code und neues Passwort (mindestens 10 Zeichen) eingeben – danach ist der Code verbraucht und die Datei gelöscht.

- **Passwort ändern:** *Einstellungen → System → Passwort der Einsatzleitung*. Danach sind alle Geräte abgemeldet,
  auch offene Seiten auf Tablets (spätestens nach einer Minute).
- **Passwort vergessen:** auf dem Server `data/leitung-auth.json` löschen und den Dienst neu starten – es gibt dann einen
  neuen Einrichtungscode.
- Passwort-Hash und Einrichtungscode sind nicht im Daten-Backup enthalten; nach einer Wiederherstellung auf einem neuen
  Server wird neu eingerichtet.
- Nach 5 falschen Passwörtern ist die Anmeldung von dieser Adresse 5 Minuten gesperrt.

### Weitere Optionen

- `Cors:AllowedOrigins` (z.B. `Cors__AllowedOrigins__0=https://andere-app.example`): fremde Webseiten, die die API im
  Browser aufrufen duerfen. Standard: keine – die eigene Oberflaeche braucht das nicht.
- `ReverseProxy:KnownNetworks` (z.B. `ReverseProxy__KnownNetworks__0=172.16.0.0/12`): weitere Proxys, deren
  `X-Forwarded-For`/`-Proto` uebernommen wird. Standard: nur `127.0.0.1`/`::1`.
- Das Team-Cookie wird nur noch ueber HTTPS gesendet. Die Team-Ansicht deshalb immer ueber Nginx mit HTTPS aufrufen.

## 8. Monitoring und Hinweise

- Health Endpoint serverseitig: `/health`
- Healthcheck-Skript: `deploy/scripts/health-check.sh`
- Journal-Logs:

```bash
sudo journalctl -u einsatzueberwachung-server.service -f
sudo journalctl -u einsatzueberwachung-mobile.service -f
sudo journalctl -t einsatz-health-check -f
```

- Kestrel bleibt intern auf `127.0.0.1`: 5000 (VPN), 5002 (oeffentlicher Team-Zugang); 5001 belegt der alte Dienst `einsatzueberwachung-mobile`
- Zugriff nur ueber VPN freigeben
- Kein oeffentliches Port-Forwarding

## 9. Updater aktivieren und pruefen

Der Updater wird ueber die Startseite (`/`) oder API aufgerufen. Das Setup hinterlegt bereits:

- `EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD=/opt/einsatzueberwachung/scripts/apply-update.sh '{package}' '{version}'`
- sudoers-Regel fuer den Dienstbenutzer `einsatz`, um die beiden systemd-Dienste neu zu starten

Pruefen:

```bash
sudo cat /etc/sudoers.d/einsatzueberwachung-update
sudo systemctl show einsatzueberwachung-server.service --property=Environment
```

Updater API manuell testen:

```bash
curl -X POST http://127.0.0.1:5000/api/update/check
curl http://127.0.0.1:5000/api/update/status
curl -X POST http://127.0.0.1:5000/api/update/install
```

## 10. Abnahme: Desktop + Mobile (`/mobile`)

Diese Checkliste verifiziert die Kernanforderungen:

- Mobile unter `https://10.10.0.1/mobile` erreichbar
- gleiche Datenbasis zwischen Hauptapp und Mobile
- Echtzeit-Updates (SignalR)
- Funksprueche als eigene API
- Daten bleiben nach Neustart erhalten

### 10.1 Vorbedingungen

- Beide Dienste laufen:

```bash
sudo systemctl status einsatzueberwachung-server.service
sudo systemctl status einsatzueberwachung-mobile.service
```

- Nginx aktiv:

```bash
sudo systemctl status nginx
```

### 10.2 Mobile Erreichbarkeit

1. Browser auf Handy/Laptop (im VPN): `https://10.10.0.1/mobile`
2. Direktaufrufe pruefen:
	- `https://10.10.0.1/mobile/einsatz`
	- `https://10.10.0.1/mobile/teams`
	- `https://10.10.0.1/mobile/notizen`
	- `https://10.10.0.1/mobile/funk`
3. Browser-Refresh auf Unterseite darf keinen 404 erzeugen.

### 10.3 Gemeinsame Datenbasis pruefen

1. In Mobile unter `/mobile/einsatz` neuen Einsatz starten.
2. In Desktop (`/einsatz-monitor`) kontrollieren:
	- Einsatzdaten sind sichtbar
	- Teams/Notizen/Funk beziehen sich auf denselben Einsatz
3. In Desktop eine Notiz erzeugen und in Mobile unter `/mobile/notizen` kontrollieren.
4. In Mobile eine Notiz erzeugen und in Desktop kontrollieren.

### 10.4 Echtzeit pruefen (SignalR)

1. Desktop und Mobile parallel offen halten.
2. In Desktop Teamstatus aendern (Start/Stopp/Reset).
3. Mobile `/mobile/teams` muss ohne manuelles Reload aktualisieren.
4. In Desktop Notiz erstellen.
5. Mobile `/mobile/notizen` muss live aktualisieren.
6. In Mobile Notiz oder Funk erstellen.
7. Desktop muss die neuen Eintraege live anzeigen.

### 10.5 Funk-API separat pruefen

1. Mobile `/mobile/funk`: neuen Funkspruch senden.
2. API pruefen:

```bash
curl http://127.0.0.1:5000/api/radio
```

3. Antwort auf Funkspruch in Mobile senden.
4. API erneut pruefen und Reply in `radio_messages`/`radio_replies` kontrollieren.

### 10.6 Neustart- und Persistenztest

1. Laufenden Einsatz, Teams, Notizen und Funk-Eintrag erzeugen.
2. Dienste neu starten:

```bash
sudo systemctl restart einsatzueberwachung-server.service
sudo systemctl restart einsatzueberwachung-mobile.service
```

3. Nach Neustart in Desktop und Mobile pruefen:
	- Einsatz ist weiterhin vorhanden
	- Teams mit Status sind vorhanden
	- Notizen sind vorhanden
	- Funksprueche sind vorhanden

### 10.7 Fehlersuche bei Problemen

```bash
sudo journalctl -u einsatzueberwachung-server.service -n 200 --no-pager
sudo journalctl -u einsatzueberwachung-mobile.service -n 200 --no-pager
sudo tail -n 100 /var/log/nginx/error.log
```
