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

## 4. Dienste pruefen

```bash
sudo systemctl status einsatzueberwachung-server.service
sudo systemctl status einsatzueberwachung-mobile.service
sudo systemctl status nginx
sudo systemctl status einsatzueberwachung-backup.timer
sudo systemctl status einsatzueberwachung-healthcheck.timer
```

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

## 7. Nginx Anpassungen

Anwendungskonfiguration in `deploy/nginx/einsatzueberwachung.conf`.

## 8. Monitoring und Hinweise

- Health Endpoint serverseitig: `/health`
- Healthcheck-Skript: `deploy/scripts/health-check.sh`
- Journal-Logs:

```bash
sudo journalctl -u einsatzueberwachung-server.service -f
sudo journalctl -u einsatzueberwachung-mobile.service -f
sudo journalctl -t einsatz-health-check -f
```

- Kestrel bleibt intern auf `127.0.0.1` (Ports 5000/5001)
- Zugriff nur ueber VPN freigeben
- Kein oeffentliches Port-Forwarding

## 9. Updater aktivieren und pruefen

Der Updater wird ueber die Startseite (`/`) oder API aufgerufen. Das Setup hinterlegt bereits:

- `EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD=/opt/einsatzueberwachung/scripts/apply-update.sh "{package}" "{version}"`
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
