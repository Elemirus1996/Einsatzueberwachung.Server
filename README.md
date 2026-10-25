# 🐕 Einsatzüberwachung.Server

**Einsatzleitsystem für Suchhundestaffeln** — entwickelt für den VPN-internen Betrieb auf einem Linux-Server.

Die Anwendung unterstützt Suchhundestaffeln bei der Koordination von Einsätzen: Teamverwaltung mit Timern, Live-GPS-Tracking von Suchhunden, Drohnen-Livestream, GPX-Import, Funksprüche, Notizen, Einsatzberichte als PDF und eine direkte Schnittstelle zu Divera 24/7.

Entwickelt von **Elemirus1996** und **Freakwave**. Lizenz: [MIT](LICENSE).

---

## ✨ Funktionen im Überblick

| Bereich | Beschreibung |
|---|---|
| 🗂️ **Einsatzsteuerung** | Einsatz starten/beenden, Teams anlegen, Suchgebiete auf der Karte definieren |
| 🎯 **Einsatz-Szenarien** | Mantrailer / Flächensuche / Trümmer / Sonstige als Szenario am Einsatz; UI passt sich an (Multi-Vermisste, Trümmer-Karte) |
| 🧑‍🤝‍🧑 **Mehrere Vermisste** | Bei Fläche/Trümmer-Szenario beliebig viele Vermisstenprofile pro Einsatz; Mantrailer auf eine Person beschränkt |
| 📋 **Szenarioabhängige Checklisten** | Editierbare Default-Checklisten je Szenario in den Stammdaten, automatisch an jedes Vermisstenprofil angehängt |
| 🧱 **Trümmer-Lagekarte** | Drohnenfoto vom Trümmerfeld hochladen und Suchgebiete pixel-basiert (ohne GPS) einzeichnen — eigene Seite, NavMenu-Eintrag erscheint nur im Trümmer-Szenario |
| ⏱️ **Team-Timer** | Zeiterfassung pro Team mit farbcodierter Warnstufe (Grün → Orange → Rot → Blinken) |
| 👥 **Teams-Detailansicht** | Detailkarte pro Team mit Status, zugewiesenen Hunden und Timer-Übersicht |
| 📡 **GPS Live-Tracking** | Bis zu 20 Halsbänder gleichzeitig, Live-Laufwege auf der Karte, automatische Suchgebiet-Warnung |
| 🚶 **Mensch-Laufweg (Handy-GPS)** | Laufweg des Hundeführers via Team-Mobile (Smartphone-GPS) live erfassen und als `HumanTrack` zur Suche speichern |
| 🚁 **Drohnen-Livestream** | Drohnen in Stammdaten verwalten und per Live-Stream-Link direkt in der Karte anzeigen |
| 📥 **GPX-Import** | GPS-Tracks als GPX-Datei importieren (z. B. aus Garmin Basecamp) zur Dokumentation von Suchteams |
| 📻 **Funksprüche** | Chronologische Funkspruch-Liste mit Antwort-Threads, Echtzeit-Sync via SignalR |
| 📝 **Notizen** | Globale und teamspezifische Notizen mit Zeitstempel, Antwort-Threads und @-Erwähnungen |
| 🗺️ **Interaktive Karte** | Leaflet.js-Karte mit Suchgebieten, Polygonen, ELW-Position und GPS-Tracks |
| 📄 **PDF-Bericht** | Einsatzbericht mit GPS-Track-Karten und Teamdaten als PDF-Download |
| 🏛️ **Archiv** | Abgeschlossene Einsätze durchsuchen und als PDF exportieren |
| 👤 **Stammdaten** | Personal, Hunde und Drohnen verwalten (inkl. Excel-Import/-Export) |
| 🔔 **Divera 24/7** | Alarme und Verfügbarkeitsstatus direkt in der Anwendung |
| 🎓 **Trainer-Modul** | Passwortgeschützter Bereich für Übungsverwaltung und Szenario-Vorschläge |
| 📱 **Mobile Ansicht** | Optimierte Ansicht für Smartphones (integriert, keine separate App) |
| 🌤️ **Wetter** | Aktuelle Wetterdaten für den Einsatzort via DWD/BrightSky |
| 🔄 **Auto-Update** | GitHub-Release-Check und Update-Einspielen direkt aus der Anwendung |

---

## 🏗️ Projektstruktur

```
src/
├── Einsatzueberwachung.Server/     ← ASP.NET Core Blazor Server (Hauptanwendung + REST-API)
├── Einsatzueberwachung.Domain/     ← Domain-Modelle, Interfaces und Business-Logik
├── Einsatzueberwachung.LiveTracking/ ← WPF Desktop-App (Windows) für USB-GPS-Empfänger
└── Einsatzueberwachung.Tests/      ← xUnit Unit-Tests

deploy/
├── nginx/                          ← Nginx Reverse-Proxy-Konfiguration
├── systemd/                        ← systemd Service Units, Backup- und Healthcheck-Timer
├── wireguard/                      ← WireGuard VPN Beispielkonfiguration
└── scripts/                        ← backup.sh, health-check.sh, apply-update.sh
```

---

## 🛠️ Tech-Stack

- **Frontend**: Blazor Server (.NET 9) mit Bootstrap 5.3+, Dark Mode, Bootstrap Icons
- **Karten**: Leaflet.js mit OpenStreetMap-Tiles
- **Echtzeit**: SignalR (`/hubs/einsatz`) für alle Live-Updates
- **Datenbank**: Entity Framework Core mit SQLite (`runtime-state.db`)
- **Stammdaten**: JSON-Dateien (menschenlesbar, im Datenverzeichnis)
- **PDF-Export**: QuestPDF + SkiaSharp
- **Excel**: ClosedXML
- **Deployment**: systemd-Dienst hinter Nginx auf Ubuntu 22.04/24.04 LTS
- **Zugriff**: ausschließlich VPN-intern (WireGuard/OpenVPN), Anmeldung der Einsatzleitung mit Staffel-Passwort (ab 1.10)

---

## 📡 GPS Live-Tracking

Für Halsband-Tracking wird die beiliegende Windows-App **Einsatzueberwachung.LiveTracking** verwendet:

1. In der Einsatzüberwachung *Einstellungen → Integration → GPS-Livetracking* öffnen. Dort stehen die Download-Adresse und der Verbindungscode für die App, jeweils mit Kopier-Knopf.
2. App herunterladen, entpacken und auf dem Windows-Rechner mit angeschlossenem GPS-Empfänger (z. B. Garmin Alpha) starten. Der Rechner muss im VPN sein.
3. Den Verbindungscode in der App einfügen und verbinden. Er enthält Server-Adresse, API-Schlüssel und den Fingerabdruck des Server-Zertifikats – so klappt die Verbindung auch mit einem selbst ausgestellten Zertifikat. Bei Netzausfall puffert die App die Positionen und sendet sie mit Originalzeit nach.
4. Erkannte Halsbänder erscheinen automatisch im EinsatzMonitor.
5. Im EinsatzMonitor Team starten → Halsband zuweisen → Live-Pfad auf der Karte verfolgen.
6. Team stoppen → GPS-Track wird als Snapshot gespeichert und steht im PDF-Bericht zur Verfügung.

Verlässt ein Hund sein Suchgebiet: rot pulsierender Marker auf der Karte + Warnung im Tracking-Panel.

### 🚶 Mensch-Laufweg per Handy-GPS (Team-Mobile)

Zusätzlich kann der Laufweg des Menschen (Hundeführer) über die mobile Team-Ansicht aufgezeichnet werden:

1. Team im Team-Login auswählen und `TeamMobile` (`/team`) öffnen.
2. Standortfreigabe im Browser zulassen.
3. Während das Team läuft, sendet das Handy Positionen an `/api/team-mobile/location`.
4. Beim Team-Stopp wird der Verlauf als `HumanTrack` zur abgeschlossenen Suche gespeichert.
5. Der Verlauf erscheint in Karte und PDF-Bericht zusammen mit dem Halsband-Track.

Details: [`docs/GPS_TRACKING_WORKFLOW.md`](docs/GPS_TRACKING_WORKFLOW.md)

---

## 🚀 Lokaler Start (Development)

```bash
# Abhängigkeiten wiederherstellen und bauen
dotnet build Einsatzueberwachung.Server.sln

# Server starten
dotnet run --project src/Einsatzueberwachung.Server

# Tests ausführen
dotnet test src/Einsatzueberwachung.Tests

# LiveTracking Desktop-App (nur Windows)
dotnet run --project src/Einsatzueberwachung.LiveTracking
```

Die Anwendung ist dann unter `http://localhost:5000` erreichbar. Swagger-UI: `http://localhost:5000/swagger`.

---

## 🐧 Deployment auf Ubuntu (VPN-intern)

Vollständige Anleitung: [`DEPLOYMENT.md`](DEPLOYMENT.md)

**Kurzübersicht:**

```bash
# Auf dem Server: Setup-Skript ausführen (installiert .NET Runtime, Nginx, systemd, UFW)
sudo bash deploy/setup.sh
```

**Was das Setup einrichtet:**

- systemd-Dienst (`einsatzueberwachung-server.service`) hinter Nginx Reverse Proxy
- Tägliches automatisches Backup um 03:30 Uhr
- Healthcheck alle 2 Minuten mit automatischem Neustart
- Persistente Laufzeitdaten unter `/opt/einsatzueberwachung/data`
- Serverseitiger GitHub-Updater via `deploy/scripts/apply-update.sh`
- NTP-Zeitsynchronisierung via `chrony`

**Relevante Umgebungsvariablen:**

| Variable | Standard | Beschreibung |
|---|---|---|
| `EINSATZ_DATA_DIR` | `/opt/einsatzueberwachung/data` | Stammdaten, Einstellungen, Archiv, Trümmer-Bilder unter `truemmer/{einsatzId}/` |
| `EINSATZ_REPORTS_DIR` | `/opt/einsatzueberwachung/data/berichte` | Generierte PDF-Berichte |

---

## 🔌 REST-API

Alle Endpunkte sind über `/api/` erreichbar. Im Development-Modus steht Swagger unter `/swagger` zur Verfügung.

| Pfad | Zweck |
|---|---|
| `POST /api/einsatz/start` | Neuen Einsatz starten |
| `GET /api/einsatz` | Aktuellen Einsatzzustand abrufen |
| `POST /api/truemmer/karten` | Trümmer-Lagekarte (Drohnenfoto) hochladen — Multipart, max 20 MB, JPG/PNG/WEBP |
| `GET /api/truemmer/karten/{id}/image` | Trümmer-Bild ausliefern |
| `DELETE /api/truemmer/karten/{id}` | Trümmer-Karte löschen |
| `POST /api/CollarWebhook/location` | GPS-Halsband-Position empfangen (Webhook) |
| `GET /api/team-mobile/state` | Mobilen Team-Zustand inkl. Halsband- und Handy-Track abrufen (authentifiziert) |
| `POST /api/team-mobile/location` | Handy-GPS-Position eines Team-Users speichern (authentifiziert) |
| `GET /api/radio` | Funksprüche abrufen |
| `POST /api/radio` | Neuen Funkspruch erstellen |
| `GET /api/divera/status` | Divera 24/7 Status abrufen |
| `GET /api/training/resources` | Stammdaten-Snapshot für Trainings-App |
| `POST /api/update/check` | GitHub-Release-Check auslösen |
| `POST /api/update/install` | Update einspielen |
| `GET /health` | Health-Check Endpoint |
| `GET /hubs/einsatz` | SignalR WebSocket Hub |

Training-API Details: [`TRAINING_API.md`](TRAINING_API.md)

---

## 📦 Release-Automation

Bei jedem veröffentlichten GitHub Release (`v*`) wird automatisch ein Linux-Asset gebaut und hochgeladen:

- Workflow: `.github/workflows/release-linux-asset.yml`
- Asset-Name: `linux-x64-server-<version>.zip` (z. B. `linux-x64-server-1.2.0.zip`)
- Manuelles Nachtriggern über `workflow_dispatch` mit Tag-Eingabe ist möglich.

Das Update kann direkt aus der Anwendung heraus eingespielt werden (Einstellungsseite oder `POST /api/update/install`).
