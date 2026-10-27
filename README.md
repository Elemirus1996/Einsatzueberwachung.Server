# 🐕 Einsatzüberwachung.Server

**Einsatzleitsystem für Rettungshundestaffeln.** Läuft auf einem eigenen Linux-Server und gibt der Staffel ein gemeinsames, aktuelles Lagebild: Teams und Suchzeiten, Hunde und Hundeführer live auf der Karte, Abdeckung der Suchgebiete, Funk, Notizen und am Ende ein vollständiger PDF-Bericht.

Die Einsatzleitung arbeitet ausschließlich im VPN. Nach außen ist nur das freigegeben, was die Handys der Teams brauchen.

[![.NET Tests](https://github.com/Elemirus1996/Einsatzueberwachung.Server/actions/workflows/dotnet-tests.yml/badge.svg)](https://github.com/Elemirus1996/Einsatzueberwachung.Server/actions/workflows/dotnet-tests.yml)
[![Neueste Version](https://img.shields.io/github/v/release/Elemirus1996/Einsatzueberwachung.Server?label=Version)](https://github.com/Elemirus1996/Einsatzueberwachung.Server/releases/latest)
[![Lizenz: MIT](https://img.shields.io/badge/Lizenz-MIT-blue.svg)](LICENSE)

Entwickelt von **Elemirus1996** und **Freakwave** · Projektseite: <https://elemirus1996.github.io/Einsatzueberwachung.Server/>

---

## ✨ Was die Anwendung kann

### Im Einsatz

| Bereich | Beschreibung |
|---|---|
| 🗂️ **Einsatzmonitor** | Einsatz anlegen und beenden, Teams mit Hund und Hundeführer, Suchzeiten mit Warnstufen (Grün → Orange → Rot), Suchgebiete auf der Karte |
| 📱 **EL-Ansicht `/el`** | Für das Tablet der Einsatzleitung, als App installierbar: Teams starten, pausieren und beenden, Funk, Notizen, Karte und Einsatz beenden. Hält auch in schwachem Mobilfunk die Verbindung |
| 📶 **Funkloch** | Funk und Notizen werden auf dem Gerät zwischengespeichert und mit der ursprünglichen Uhrzeit nachgesendet. Kartenkacheln lassen sich vorab „für Funkloch sichern“ |
| 🎯 **Szenarien** | Mantrailer, Fläche, Trümmer und Sonstige. Die Oberfläche passt sich an, z. B. mehrere Vermisste bei Fläche und Trümmer sowie Checklisten je Szenario |
| 🧱 **Trümmer-Lagekarte** | Drohnenfoto des Trümmerfelds hochladen und Suchbereiche direkt auf dem Bild einzeichnen |
| 📻 **Funk & Notizen** | Chronologisch, mit Antworten und @-Erwähnungen, auf allen Geräten sofort aktuell. Einträge der Einsatzleitung zeigen das Gerät, z. B. „Einsatzleitung · Tablet EL“ |

### Ortung

| Bereich | Beschreibung |
|---|---|
| 🐕 **GPS-Halsbänder** | Bis zu 20 Halsbänder gleichzeitig (z. B. Garmin Alpha über die beiliegende Windows-App). Jede Suche bekommt einen eigenen Track in eigener Farbe. Eine Pause unterbricht den Track, danach läuft er weiter |
| 🚶 **Team-Handy** | Laufweg des Hundeführers über den Browser oder die kostenlose App **Traccar Client**. Die App sendet auch bei gesperrtem Handy weiter und puffert im Funkloch. Eingerichtet wird sie per Link mit einem Zugang, der nur für diesen Einsatz gilt |
| ▦ **Abdeckung live** | Die Karte zeigt je Suchgebiet „x % abgesucht“ und die bereits abgesuchte Fläche, während die Hunde laufen |
| ⚠️ **Warnungen** | Hund verlässt sein Suchgebiet, Team-Handy sendet nicht mehr, Akku unter 20 %, Suchzeit überschritten. Alle Warnungen laufen im Warnzentrum zusammen |
| 🚁 **Drohnen & GPX** | Drohnen-Livestream in der Karte, GPX-Import (z. B. aus Garmin Basecamp) |

### Nachbereitung

| Bereich | Beschreibung |
|---|---|
| 📄 **PDF-Bericht** | Teams, Funk, Notizen, Tracks und Abdeckung auf echten Karten. Kartenart (Straße, Topografisch, Satellit) und Teams sind wählbar |
| 🏛️ **Archiv** | Abgeschlossene Einsätze mit allen Suchen, Vermissten und Tracks. Den Bericht jederzeit neu erzeugen |
| 🧾 **Audit-Log** | Wer hat wann was getan, inklusive Anmeldungen und Geräten |

### Betrieb

| Bereich | Beschreibung |
|---|---|
| 🔒 **Zugriff** | Einsatzleitung nur im WireGuard-VPN, mit Staffel-Passwort. VPN-Geräte werden in den Einstellungen per QR-Code angelegt und laufen automatisch ab |
| 🩺 **Systemzustand** | Zertifikat, letztes Backup, freier Speicher, Divera und Uhrzeit auf einen Blick. Ein ablaufendes Zertifikat lässt sich direkt in der App erneuern |
| 👤 **Stammdaten** | Personal, Hunde und Drohnen, mit Excel-Import und -Export |
| 🔔 **Divera 24/7** | Alarme und Verfügbarkeiten in der Anwendung, Einsatz aus dem Alarm übernehmen |
| 🌤️ **Wetter** | Wetter am Einsatzort (DWD über BrightSky) |
| 🎓 **Trainer-Modul** | Passwortgeschützter Bereich für Übungen und Szenario-Vorschläge |
| 🔄 **Updates** | Neue Versionen werden aus GitHub-Releases erkannt und im Browser eingespielt |

---

## 🧭 Aufbau im Einsatz

```
  Einsatzleitung (Laptop, Tablet /el)          Team-Handys (Browser, Traccar Client)
              │  WireGuard-VPN                          │  HTTPS, Let's Encrypt
              ▼                                         ▼
  ┌───────────────────────── nginx ──────────────────────────────┐
  │  intern: gesamte Anwendung      öffentlich: nur /team und    │
  │                                 /api/team-mobile/…           │
  └───────────────────────────┬──────────────────────────────────┘
                              ▼
                  Einsatzueberwachung.Server  ◄── LiveTracking-App (Windows, GPS-Halsbänder)
```

Für die Handys der Teams ist eine öffentliche Adresse mit Zertifikat nötig, z. B. eine kostenlose DynDNS-Domain. Ohne gültiges Zertifikat sendet Traccar Client nichts. Die Anwendung warnt deshalb rechtzeitig vor dem Ablauf.

---

## 🚀 Installation auf Ubuntu

Vollständige Anleitung: [`DEPLOYMENT.md`](DEPLOYMENT.md)

```bash
sudo bash deploy/setup.sh                                          # .NET-Laufzeit, nginx, systemd, Firewall, Backups
sudo bash deploy/scripts/setup-vpn.sh dein-einsatz.mywire.org      # VPN-Geräte per QR-Code (ab 1.10)
sudo bash deploy/scripts/setup-zertifikat.sh dein-einsatz.mywire.org   # Zertifikat + Knopf „Jetzt erneuern“ (ab 1.11.6)
```

Das Setup richtet ein:

- den systemd-Dienst `einsatzueberwachung-server.service` hinter nginx
- ein tägliches Backup um 03:30 Uhr und einen Healthcheck alle 2 Minuten mit automatischem Neustart
- Daten unter `/opt/einsatzueberwachung/data` und die Zeitsynchronisierung mit `chrony`
- den Updater `deploy/scripts/apply-update.sh` für Updates aus dem Browser

| Umgebungsvariable | Standard | Bedeutung |
|---|---|---|
| `EINSATZUEBERWACHUNG_DATA_DIR` | `/opt/einsatzueberwachung/data` | Stammdaten, Einstellungen, Archiv, Trümmer-Bilder |
| `EINSATZUEBERWACHUNG_REPORT_DIR` | `…/data/berichte` | Erzeugte PDF-Berichte |
| `EINSATZUEBERWACHUNG_PUBLIC_PORT` | `5002` | Interner Port für die öffentliche Team-Adresse |
| `EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD` | `…/apply-update.sh '{package}' '{version}'` | Befehl zum Einspielen von Updates |

---

## 📡 GPS-Halsbänder und Team-Handys

**Halsbänder:** In *Einstellungen → Integration → GPS-Livetracking* stehen Download und Verbindungscode der Windows-App **Einsatzueberwachung.LiveTracking**. Der Code enthält Server-Adresse, Schlüssel und den Fingerabdruck des Zertifikats. Damit klappt die Verbindung auch mit einem selbst ausgestellten Zertifikat. Erkannte Halsbänder erscheinen im Monitor. Beim Start eines Teams wird das Halsband zugewiesen, beim Beenden der Suche wird der Track gespeichert.

**Team-Handy:** Das Team öffnet `/team` und gibt den Standort frei. Für die Ortung bei gesperrtem Handy richtet ein Knopf **Traccar Client** ein. Nach dem Einsatz nimmt der Server den Zugang nicht mehr an. Ob alles funktioniert, lässt sich ohne Einsatz prüfen: *Einstellungen → Integration → Traccar testen*.

Details: [`docs/GPS_TRACKING_WORKFLOW.md`](docs/GPS_TRACKING_WORKFLOW.md)

---

## 🛠️ Technik

- **Anwendung:** Blazor Server (.NET 9), SignalR für alle Live-Updates, Bootstrap 5 mit Hell- und Dunkelmodus
- **Karten:** Leaflet mit OpenStreetMap, OpenTopoMap und Esri-Satellit. PDF-Karten mit SkiaSharp
- **Daten:** SQLite (EF Core) für den laufenden Einsatz, JSON-Dateien für Stammdaten und Einstellungen
- **PDF / Excel:** QuestPDF, ClosedXML
- **Betrieb:** Ubuntu 22.04/24.04 LTS, systemd, nginx, WireGuard, Let's Encrypt

```
src/
├── Einsatzueberwachung.Server/        ← Blazor-Anwendung und REST-API
├── Einsatzueberwachung.Domain/        ← Modelle, Dienste und Fachlogik
├── Einsatzueberwachung.LiveTracking/  ← Windows-App für GPS-Empfänger (Halsbänder)
└── Einsatzueberwachung.Tests/         ← xUnit-Tests
deploy/
├── nginx/  systemd/  wireguard/       ← Vorlagen für den Server
└── scripts/                           ← Setup, Backup, Healthcheck, Update, VPN, Zertifikat
```

---

## 💻 Entwicklung

```bash
dotnet build Einsatzueberwachung.Server.sln
dotnet run --project src/Einsatzueberwachung.Server      # http://localhost:5000, Swagger unter /swagger
dotnet test src/Einsatzueberwachung.Tests
```

Die REST-API ist im Development-Modus unter `/swagger` dokumentiert. Die Schnittstelle für die Trainings-App beschreibt [`TRAINING_API.md`](TRAINING_API.md).

---

## 📦 Releases

Jedes veröffentlichte GitHub-Release (`v*`) baut automatisch das Paket `linux-x64-server-<version>.zip` und die Windows-App für die Halsbänder. Danach aktualisiert es die Versionshistorie auf der [Projektseite](https://elemirus1996.github.io/Einsatzueberwachung.Server/). Eingespielt wird das Update in der Anwendung unter *Einstellungen → System*.

Lizenz: [MIT](LICENSE) · Verwendete Bibliotheken: [`THIRD_PARTY_LICENSES.md`](THIRD_PARTY_LICENSES.md)
