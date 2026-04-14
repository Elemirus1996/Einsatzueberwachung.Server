# Einsatzueberwachung.Server

Linux-faehiger Nachbau der Anwendung Einsatzueberwachung.Web mit separater Mobile-PWA.

## Projektstruktur

- `src/Einsatzueberwachung.Server`: Hauptanwendung (Blazor Server)
- `src/Einsatzueberwachung.Domain`: Domain-Modelle und Services
- `src/Einsatzueberwachung.Mobile`: Mobile PWA mit 4 Kernfunktionen
- `src/Einsatzueberwachung.LiveTracking`: Windows Desktop-App (WPF) fuer GPS USB Halsband-Empfang
- `deploy`: Linux-Deployment (Nginx, systemd, WireGuard, Setup-Skript)

## Deployment-Bausteine

- Nginx Reverse Proxy: `deploy/nginx/einsatzueberwachung.conf`
- systemd Dienste: `deploy/systemd/einsatzueberwachung-*.service`
- systemd Timer: Backup und Healthcheck in `deploy/systemd`
- Skripte: `deploy/scripts/backup.sh`, `deploy/scripts/health-check.sh`, `deploy/scripts/apply-update.sh`

## Laufzeitdaten unter Linux

- Persistente Daten: `/opt/einsatzueberwachung/data`
- Berichte/PDFs: `/opt/einsatzueberwachung/data/berichte`
- Konfiguration ueber Env Vars: `EINSATZUEBERWACHUNG_DATA_DIR`, `EINSATZUEBERWACHUNG_REPORT_DIR`, `EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD`

## Updater

- Startseite zeigt den Update-Status (installiert/verfuegbar, letzter Check, letzte Installation)
- Auto-Check alle 6 Stunden (abschaltbar ueber `AppSettings.AutoCheckUpdates`)
- Manuelle Trigger ueber UI oder API:
	- `POST /api/update/check`
	- `POST /api/update/install`
	- `GET /api/update/status`

## Live GPS-Halsband Tracking

Einfache Bedienung fuer den Einsatzbetrieb.

### Bedienungsanleitung Live-Tracking

#### 1. Vorbereitung

1. GPS-Handheld per USB am Rechner anschliessen, auf dem die **LiveTracking Desktop-App** laeuft.
2. In der Desktop-App die Server-URL eintragen (z.B. `http://10.0.0.1:5000`) und verbinden.
3. Sobald Halsbaender erkannt werden, erscheinen sie automatisch im System.

#### 2. Halsband einem Team zuweisen

1. Im **EinsatzMonitor** ein Hundeteam anlegen oder bearbeiten.
2. Im Team-Formular unter **GPS-Halsband** das gewuenschte Halsband aus der Dropdown-Liste waehlen.  
   Nur nicht-zugewiesene Halsbaender werden angezeigt.
3. Team speichern — das Halsband ist nun dem Team zugeordnet.

#### 3. Suche starten und Live-Pfad verfolgen

1. Im EinsatzMonitor auf **Start** klicken, um den Team-Timer zu starten.  
   Der bisherige GPS-Verlauf des Halsbands wird dabei zurueckgesetzt.
2. Zur **Einsatzkarte** wechseln und den **GPS**-Button in der Kopfzeile aktivieren.
3. Das schwebende Tracking-Panel zeigt alle aktiven Halsbaender mit Farbcodierung an.
4. Der Live-Pfad wird als farbige Linie auf der Karte gezeichnet. Die Farbe entspricht der Farbe des zugewiesenen Suchgebiets.

#### 4. Suchgebiet-Warnung

Verlaesst ein Hund sein zugewiesenes Suchgebiet, erscheint:
- Ein **rot pulsierender Marker** an der aktuellen Position auf der Karte.
- Eine **Warnmeldung** im Tracking-Panel.
- Kehrt der Hund ins Suchgebiet zurueck, wird die Warnung automatisch entfernt.

#### 5. Team stoppen und Track sichern

1. Im EinsatzMonitor auf **Stopp** klicken.
2. Der gesamte aufgezeichnete GPS-Track wird als Snapshot gespeichert.
3. Der Snapshot steht anschliessend fuer den PDF-Bericht zur Verfuegung.

## Einsatzbericht und PDF-Export

### Bedienungsanleitung Berichterstellung

#### 1. Bericht oeffnen

Im Hauptmenue auf **Bericht** klicken oder ueber den EinsatzMonitor den Bericht aufrufen.

#### 2. Berichtsdaten eingeben

- **Ergebnis**: Kurztext zum Einsatzergebnis (z.B. „Person gefunden", „Suche erfolglos").
- **Bemerkungen**: Ausfuehrliche Anmerkungen zum Einsatzverlauf.

#### 3. GPS-Tracks einschliessen (optional)

- Die Checkbox **„GPS-Tracks in PDF einschliessen"** aktivieren.  
  Ein Badge zeigt die Anzahl der verfuegbaren Tracks an.
- Ist die Checkbox aktiv, werden die gesicherten Tracks als Kartenansichten in die PDF aufgenommen.

#### 4. PDF erzeugen

Auf **„PDF erzeugen"** klicken. Die PDF-Datei wird serverseitig generiert und zum Download angeboten.

#### 5. Einsatz archivieren

- **„Einsatz beenden und archivieren"** — speichert den Bericht und verschiebt den Einsatz ins Archiv.
- **„Archivieren und neuen Einsatz vorbereiten"** — archiviert und setzt das System fuer einen neuen Einsatz zurueck.

## Mobile Funktionen

- Einsatz anlegen
- Team-Uebersicht
- Notizen
- Funksprueche

## Lokaler Start (Development)

```bash
# Server + Mobile (Linux/macOS/Windows)
dotnet build Einsatzueberwachung.Server.sln
dotnet run --project src/Einsatzueberwachung.Server
dotnet run --project src/Einsatzueberwachung.Mobile

# LiveTracking Desktop-App (nur Windows)
dotnet run --project src/Einsatzueberwachung.LiveTracking
```

## Deployment

Siehe `DEPLOYMENT.md`.

Fuer die End-to-End-Abnahme (Desktop + Mobile ueber `/mobile`) siehe in `DEPLOYMENT.md` den Abschnitt **10. Abnahme: Desktop + Mobile (`/mobile`)**.

## Release-Automation

- Bei jedem veroeffentlichten GitHub Release (`v*`) wird automatisch ein Linux-Asset gebaut und hochgeladen.
- Workflow: `.github/workflows/release-linux-asset.yml`
- Asset-Name: `linux-x64-server-<version>.zip` (z. B. `linux-x64-server-1.1.3.zip`)
- Manuell nachtriggern ueber `workflow_dispatch` mit Tag-Eingabe ist moeglich.
