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

Echtzeit-Tracking von bis zu 20 GPS-Halsbaendern fuer Rettungshunde. Eine externe Halsband-Software sendet GPS-Positionen per REST-API. Die Positionen werden live auf der Einsatzkarte als Polylines dargestellt.

### Architektur

```
GPS USB-Geraet (Handheld)
        |
        v
  Einsatzueberwachung.LiveTracking (Windows WPF Desktop-App)
        |  liest Halsband-Daten via USB
        |  leitet GPS-Positionen weiter
        v
  POST /api/CollarWebhook/location
        |
        v
  CollarTrackingService (in-memory, thread-safe)
        |
        +---> CollarLocationReceived event
        |         |
        |         v
        |   CollarTrackingRelayService (hosted service)
        |         |
        |         v
        |   SignalR broadcast "collar.location"
        |         |
        |         v
        |   EinsatzKarte.razor --> collar-tracking.js --> Leaflet polyline + marker
        |
        +---> OutOfBoundsDetected event (falls ausserhalb Suchgebiet)
                  |
                  v
            SignalR broadcast "collar.outofbounds"
                  |
                  v
            Roter pulsierender Kreis auf Karte + UI-Warnung
```

### GPS LiveTracking Desktop-App

Eigenstaendige Windows WPF-Anwendung (`Einsatzueberwachung.LiveTracking`), die als Bruecke zwischen dem GPS USB-Empfaenger und dem Einsatzueberwachung-Server dient.

**Funktion:**
- Verbindet sich per USB mit einem GPS-Handheld (z.B. Astro/Alpha)
- Empfaengt Echtzeit-GPS-Daten der Hundehalsbänder ueber das GPS USB-Protokoll
- Leitet die Positionen automatisch per HTTP an den Einsatzueberwachung-Server weiter
- Zeigt Verbindungsstatus, erkannte Hunde und Uebertragungsstatistik an

**Unterstuetzte GPS-Pakete:**
| Paket-ID | Typ | Beschreibung |
|---|---|---|
| `0x0033` | PVT Data (D800) | GPS-Position des Hauptgeraets |
| `0x0C06` | Dog Collar Data | Halsband-Telemetrie (Position, Batterie, Signalstaerke, Hundename) |
| `0x0072` | Multi-Person Data | Mehrere getrackte Personen |

**Konfiguration:** Server-URL wird in den App-Einstellungen gespeichert und bleibt ueber Neustarts erhalten.

**Voraussetzungen:** Windows mit installiertem GPS USB-Treiber, .NET 9 Runtime.

### Ablauf

1. Externe Software sendet GPS-Daten an `POST /api/CollarWebhook/location`
2. Halsband wird automatisch registriert (erstmalig) oder aktualisiert
3. Dispatcher weist Halsband im Team-Editor (EinsatzMonitor) einem Hundeteam zu
4. Live-Pfad wird auf der Karte gezeichnet (Toggle ueber GPS-Button)
5. Bei Verlassen des Suchgebiets wird eine Warnung ausgeloest

### REST-API Endpunkte

| Methode | Route | Beschreibung |
|---|---|---|
| `POST` | `/api/CollarWebhook/location` | GPS-Position empfangen |
| `GET` | `/api/CollarWebhook/collars` | Alle bekannten Halsbaender auflisten |
| `GET` | `/api/CollarWebhook/collars/available` | Nicht zugewiesene Halsbaender |
| `GET` | `/api/CollarWebhook/history/{collarId}` | Positionsverlauf eines Halsbands |

### Payload fuer `POST /api/CollarWebhook/location`

```json
{
  "Id": "collar-001",
  "CollarName": "Rex GPS",
  "Coordinates": {
    "Lat": 49.3188,
    "Lng": 8.4312
  }
}
```

### SignalR-Events

| Event | Beschreibung |
|---|---|
| `collar.location` | Neue GPS-Position (collarId, lat, lng, timestamp) |
| `collar.outofbounds` | Hund hat Suchgebiet verlassen (teamId, collarId, lat, lng) |

### Hinweise

- Halsband-Daten sind In-Memory (kein DB-Schema noetig) und werden bei Einsatz-Ende zurueckgesetzt
- Zuordnung Halsband-zu-Team erfolgt ausschliesslich ueber das Team-Formular im EinsatzMonitor (kein REST-Endpunkt)
- Maximal 20 gleichzeitige Halsbaender, thread-safe via ConcurrentDictionary
- Out-of-Bounds-Pruefung nutzt Ray-Casting Algorithmus gegen das zugewiesene Suchgebiet-Polygon

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
