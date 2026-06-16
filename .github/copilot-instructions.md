# 🤖 AI Agent Guidelines & Self-Improvement Protocol
## Projekt: Einsatzüberwachung.Server

## 1. Deine Rolle & Der Zweck dieser Datei
Du bist ein KI-Entwicklungsassistent (z.B. Claude Code, GitHub Copilot, Cursor) in diesem Softwareprojekt.
Diese Datei ist deine primäre **"Single Source of Truth"** für unsere Projektstandards, Architektur-Entscheidungen und Best Practices. **Lies diese Datei vor jeder größeren Aufgabe.**

---

## 2. Der "Self-Improvement" Zyklus (HÖCHSTE PRIORITÄT)
Wir möchten, dass du und andere Agenten aus Fehlern lernen. Da du keine sitzungsübergreifende Erinnerung hast, dient diese Datei als dein Langzeitgedächtnis.

**Die Regel:**
Wann immer ein menschlicher Entwickler deinen Code korrigiert, einen Vorschlag ablehnt, dich auf einen Fehler hinweist oder eine Lösung manuell anpasst, bist du verpflichtet, folgendes zu tun:
1. **Analysiere:** Verstehe, *warum* der Mensch deinen Code geändert hat. Was war die zugrundeliegende Design-Entscheidung oder Vorliebe?
2. **Extrahiere:** Formuliere aus dieser Erkenntnis eine kurze, prägnante und allgemeingültige Projekt-Regel.
3. **Aktualisiere:** Wenn du die Fähigkeit hast, Dateien zu bearbeiten (z.B. Claude Code), öffne diese Datei (`.github/copilot-instructions.md`), füge die neue Regel unter Sektion 4 ("Erfahrungen & Dynamische Regeln") hinzu und speichere sie.
4. **Bestätige:** Wenn du die Datei nicht selbst speichern kannst (z.B. in manchen Copilot-Chats), generiere den aktualisierten Text für Sektion 4 und fordere den Entwickler auf, ihn zu kopieren.

---

## 3. Allgemeine Projekt-Grundsätze (Statisch)

### Projekt-Überblick
**Einsatzüberwachung.Server** ist eine Blazor Server (.NET 8/9) Einsatzleit-Anwendung für Suchhundestaffeln.
Sie läuft auf einem **Ubuntu Linux Server** mit **VPN-geschütztem Zugriff** (nicht öffentlich erreichbar) hinter Nginx Reverse Proxy.

Mobile Ansichten sind direkt im Server-Projekt integriert (kein separates Mobile-Projekt).
Der Desktop-GPS-Client `Einsatzueberwachung.LiveTracking` ist eine separate WPF-App (Windows-only).

### Tech-Stack
- **Frontend**: Blazor Server (.NET 8/9) mit Razor Components + interaktiver Server-Render-Modus (`@rendermode InteractiveServer`)
- **Backend**: ASP.NET Core (.NET 8/9) auf Kestrel mit REST-API-Controllern und Minimal-API-Endpoints
- **Datenbank**: Entity Framework Core mit SQLite — `runtime-state.db` für Runtime-Zustand (über `RuntimeDbContext`), JSON-Dateien für Stammdaten und Einstellungen
- **Echtzeit**: SignalR (`/hubs/einsatz`) für alle Live-Updates (Teams, Notizen, GPS, Radio)
- **Karten**: Leaflet.js (interaktive Karte), OSM/Carto-Tiles für statischen PDF-Karten-Export
- **UI**: Bootstrap 5.3+ mit Dark Mode, Bootstrap Icons (`bi-*`)
- **Kompression**: Brotli + Gzip Response Compression für alle HTTP-Antworten
- **Deployment**: systemd Service hinter Nginx Reverse Proxy auf Ubuntu 22.04/24.04 LTS
- **VPN**: Zugriff nur über WireGuard/OpenVPN-Netzwerk
- **Desktop-Client**: `Einsatzueberwachung.LiveTracking` — WPF-App (Windows, .NET 9) für USB-GPS-Halsband-Integration

### Sprache & Stil
- **UI-Sprache**: Deutsch
- **Code**: Variablen, Klassen, Methoden auf Englisch oder Deutsch (dem Original folgend)
- **Commits**: Deutsch oder Englisch, beschreibend
- **Kommentare**: Klären das *Warum*, nicht das *Was*

### Architektur
```
src/
├── Einsatzueberwachung.Server/        ← ASP.NET Core Hauptanwendung
│   ├── Components/
│   │   ├── Pages/                      ← Razor Pages (alle Seiten)
│   │   └── Layout/                     ← MainLayout, NavMenu, TrainerLayout
│   ├── Controllers/                    ← REST-API Controller
│   │   ├── EinsatzController           ← Einsatz-API
│   │   ├── CollarWebhookController     ← GPS-Halsband Webhook-Empfang
│   │   ├── TeamMobileController         ← Team-Mobile Login, State & Handy-GPS
│   │   ├── RadioController             ← Funksprüche-API
│   │   ├── ThreadsController           ← Antwort-Threads
│   │   ├── DiveraController            ← Divera 24/7 Integration
│   │   ├── TrainingController          ← Training-API (ext. Trainings-App)
│   │   ├── TrainerAuthController       ← Trainer-Login/Logout
│   │   └── UpdateController            ← GitHub-Update-Verwaltung
│   ├── Data/
│   │   └── RuntimeDbContext.cs         ← SQLite (Runtime-State, Radio, Replies)
│   ├── Hubs/
│   │   └── EinsatzHub.cs               ← SignalR Hub
│   ├── Security/
│   │   └── TrainerAuthOptions.cs       ← Cookie-Auth Konfiguration
│   ├── Services/
│   │   ├── Radio/                      ← RadioService (Funksprüche)
│   │   ├── BrowserPreferencesService   ← Theme-Präferenzen (Scoped)
│   │   ├── CollarTrackingRelayService  ← GPS → SignalR Relay (Hosted)
│   │   ├── EinsatzHubRelayService      ← Domain-Events → SignalR (Hosted)
│   │   ├── RuntimeStatePersistenceService ← SQLite-Persistenz (Hosted)
│   │   ├── TeamTimerTickService        ← Timer-Ticks (Hosted)
│   │   ├── UpdateAutoCheckService      ← GitHub-Update-Check (Hosted)
│   │   └── OsmStaticMapRenderer        ← Karten-Tiles für PDF-Export
│   ├── Training/                       ← Trainer-Modul
│   │   ├── TrainingContracts.cs        ← DTOs & Records
│   │   ├── TrainingApiOptions.cs       ← Konfiguration
│   │   ├── TrainingExerciseService     ← Übungsverwaltung
│   │   ├── TrainingScenarioSuggestion  ← KI-gestützte Szenario-Vorschläge
│   │   └── TrainerNotificationService  ← Trainer-Benachrichtigungen
│   ├── wwwroot/
│   │   ├── js/                         ← collar-tracking.js, einsatz-map.js,
│   │   │                               │   clock.js, keyboard-shortcuts.js,
│   │   │                               │   layout-tools.js, theme-sync.js
│   │   ├── audio/warnings/             ← funk.wav, glocke.wav, kritisch.wav
│   │   ├── app.css, leaflet-custom.css, print-map.css
│   │   └── leaflet-interop.js
│   └── Program.cs                      ← DI + Middleware-Pipeline
├── Einsatzueberwachung.Domain/        ← Business-Logik
│   ├── Interfaces/                     ← IEinsatzService, ICollarTrackingService,
│   │                                   │   IDiveraService, IWeatherService, etc.
│   ├── Models/                         ← EinsatzData, Team, PersonalEntry, Note,
│   │   │                               │   Collar, CollarLocation, SearchArea, etc.
│   │   └── Divera/                     ← DiveraAlarm, DiveraMember, etc.
│   └── Services/                       ← EinsatzService, CollarTrackingService,
│                                       │   DiveraService, DwdWeatherService,
│                                       │   GitHubUpdateService, UtmConverter, etc.
├── Einsatzueberwachung.LiveTracking/  ← WPF Desktop-App (Windows-only!)
│   │                                   ← Liest GPS-USB-Daten & sendet an Server-API
│   └── Services/
│       ├── GpsSimulationService        ← GPS-Simulation für Tests
│       └── ServerApiClient             ← HTTP-Client zum Server
└── Einsatzueberwachung.Tests/         ← Unit-Tests
deploy/
├── nginx/                              ← Nginx Reverse Proxy Config
├── systemd/                            ← systemd Service Units
├── wireguard/                          ← WireGuard VPN Beispiel-Config
└── scripts/
    ├── apply-update.sh                 ← Update einspielen
    ├── backup.sh                       ← Datensicherung
    └── health-check.sh                 ← Systemzustand prüfen
```

### Seiten der Hauptanwendung (implementiert)
1. **Home.razor** — Startseite/Dashboard
2. **EinsatzStart.razor** — Neuen Einsatz starten
3. **EinsatzMonitor.razor** — Hauptüberwachungsseite (Teams, Timer, Notizen, Funksprüche)
4. **EinsatzKarte.razor** — Interaktive Karte (Leaflet.js, GPS-Halsbänder, Polygone)
5. **EinsatzBericht.razor** — Einsatzbericht / PDF-Export
6. **EinsatzArchiv.razor** — Archiv vergangener Einsätze
7. **Stammdaten.razor** — Personal-, Hunde- und Drohnen-Verwaltung
8. **Einstellungen.razor** — QR-Code, Theme, Konfiguration, DB-Verwaltung, GitHub-Update, Divera-API-Key, Trainer-Zugang
9. **Weather.razor** — Wetteranzeige (DWD/BrightSky API)
10. **DiveraStatus.razor** — Divera 24/7: aktive Alarme & Verfügbarkeitsstatus (`/divera`)
11. **Trainer.razor** — Trainer-Modul mit eigenem Layout (`/trainer`, passwortgeschützt)
12. **PopoutNotes.razor** — Popout-Fenster für Funksprüche & Notizen
13. **PopoutTeams.razor** — Popout-Fenster für Team-Übersicht
14. **MobileDashboard.razor** — Mobiles Dashboard
15. **MobileKarte.razor** — Mobile Kartenansicht
16. **TeamLogin.razor** — Team-Mobile Login (Token-gestützt)
17. **TeamMobile.razor** — Team-Mobile Einsatzansicht mit Handy-GPS-Übertragung
18. **Error.razor** — Fehlerseite

### REST-API Endpoints
Alle REST-Endpoints sind über `/api/` erreichbar. Swagger-UI ist im Development-Modus unter `/swagger` verfügbar.

| Prefix | Controller | Zweck |
|---|---|---|
| `/api/einsatz` | `EinsatzController` | Einsatz starten/beenden, aktuelle Daten |
| `/api/collar` | `CollarWebhookController` | GPS-Halsband-Positionen empfangen (Webhook) |
| `/api/team-mobile` | `TeamMobileController` | Team-Mobile Login/Status und Handy-GPS-Positionsupdates |
| `/api/radio` | `RadioController` | Funksprüche lesen/erstellen |
| `/api/threads` | `ThreadsController` | Antwort-Threads auf Funksprüche |
| `/api/divera` | `DiveraController` | Divera 24/7 Status & Alarme |
| `/api/training` | `TrainingController` | Training-API für externe Trainings-App |
| `/api/trainer` | `TrainerAuthController` | Trainer-Login/Logout (Cookie-Auth) |
| `/api/update` | `UpdateController` | GitHub-Release-Check & Update-Anwendung |
| `/downloads/*` | Minimal API | PDF, Excel, JSON, ZIP Downloads |
| `/health` | Built-in | Health-Check Endpoint |
| `/hubs/einsatz` | `EinsatzHub` | SignalR WebSocket Hub |

### Sicherheit
- Niemals echte API-Keys, Passwörter oder sensible Daten ausgeben — Platzhalter oder `appsettings.Production.json` / Umgebungsvariablen verwenden
- **Divera-API-Key** wird ausschließlich über `ISettingsService` aus `StaffelSettings.json` gelesen — NICHT aus `appsettings.json`
- **Trainer-Passwort** steht in `appsettings.json` unter `TrainerAuth:Password` — in Produktion über Umgebungsvariable `TrainerAuth__Password` überschreiben
- Keine öffentlichen Endpunkte — ausschließlich VPN-interner Zugriff
- Keine Windows-Registry-Zugriffe, keine IIS-Konfiguration
- Die Training-API kann schreibend gesperrt werden (`TrainingApi:AllowWriteOperations=false`); Ursprünge per `TrainingApi:AllowedOrigins` einschränken
- Cookie-Authentifizierung für den Trainerbereich: `HttpOnly`, `SameSite=Strict`, 12h Session

### Service-Registrierung Konventionen
- **Singleton**: Alle Domain-Services (`IEinsatzService`, `IMasterDataService`, `ISettingsService`, `ICollarTrackingService`, `IDiveraService`, `IArchivService`, `ToastService`, `GitHubUpdateService`, etc.) — sie halten den globalen Anwendungszustand
- **Scoped**: `BrowserPreferencesService` (pro HTTP-Request / Blazor-Circuit)
- **Transient** via `IRadioService`: Scoped mit `AddScoped<IRadioService, RadioService>()` — hat Zugriff auf `DbContext`
- **Hosted Services**: `EinsatzHubRelayService`, `CollarTrackingRelayService`, `TeamTimerTickService`, `UpdateAutoCheckService`, `RuntimeStatePersistenceService` — laufen als `BackgroundService`
- **DbContext**: `IDbContextFactory<RuntimeDbContext>` (nicht direkt `DbContext`) — für Thread-Safety in Singleton-Kontext

### Datenpersistenz
- **Runtime-Zustand** (aktiver Einsatz, Teams, Notizen): SQLite via `RuntimeDbContext` → `runtime-state.db`
- **Funksprüche & Replies**: SQLite via `RuntimeDbContext` (Tabellen: `radio_messages`, `radio_replies`)
- **Stammdaten** (Personal, Hunde, Drohnen): JSON-Dateien im Daten-Verzeichnis
- **Einstellungen**: `StaffelSettings.json` und `AppSettings.json` im Daten-Verzeichnis
- **Archiv**: JSON-Dateien für archivierte Einsätze
- Alle Dateipfade immer über `AppPathResolver.GetDataDirectory()` auflösen — niemals absolute Pfade hardcoden

### Inkrementelle Architektur-Verbesserung (Boy Scout Rule) 🏕️
**Hinterlasse jede Datei, die du anfasst, ein kleines bisschen besser als du sie vorgefunden hast.**

Das bedeutet konkret: Wenn du eine Datei ohnehin bearbeitest, darfst und sollst du gleichzeitig *kleine* Verbesserungen vornehmen — aber **niemals** ein großes Refactoring starten, nur weil du die Datei schon offen hast.

**Erlaubte Mini-Refactorings beim Anfassen einer Datei:**
- Eine übergroße Methode (>50 Zeilen) in private Hilfsmethoden aufteilen
- Duplizierte Logik in eine eigene Methode extrahieren
- Magic Strings/Numbers durch benannte Konstanten ersetzen
- Fehlende `private`/`readonly`-Modifier ergänzen
- Veraltete Kommentare korrigieren oder entfernen
- Eine Klasse mit zu vielen Verantwortlichkeiten aufteilen, *wenn* das schnell und risikoarm geht
- Unnötige `using`-Direktiven entfernen
- Einen neuen Dienst in eine eigene Datei auslagern statt ihn an eine bereits große Datei anzuhängen

**Verboten (immer Rückfrage beim Menschen):**
- Umbenennung von öffentlichen API-Klassen, Interfaces oder Methoden (bricht ggf. Clients)
- Verschieben ganzer Namespaces oder Projekte
- Änderung der Datenbankstruktur (`RuntimeDbContext`, Migrations)
- Komplettes Umschreiben einer Seite oder eines Services
- Umbau der Middleware-Pipeline in `Program.cs`

### Zerstörerische Aktionen
Bevor du Dateien löschst oder massive Refactorings durchführst, die das ganze System betreffen, **frage immer den Menschen um Erlaubnis**.

### DO ✅
- **Linux-kompatible Pfade** — immer `AppPathResolver.GetDataDirectory()` und `Path.Combine()` verwenden
- Systemd-fähige Konfiguration (kein interaktiver Modus, kein Autostart via Windows-Mechanismen)
- Nginx-kompatibel: `UseForwardedHeaders()` muss als erstes in der Middleware-Pipeline stehen
- Neue REST-API-Endpunkte als Controller in `Controllers/` anlegen, nicht als Minimal-API (außer Download-Endpoints die bereits als Minimal-API existieren)
- Trainer-Modul mit `[Authorize(Policy = "TrainerOnly")]` absichern und `TrainerLayout` verwenden
- Training-API-Endpoints: immer `TrainingApi:Enabled` prüfen (→ 404 wenn deaktiviert), `AllowWriteOperations` für Schreibzugriffe prüfen
- `IDbContextFactory<RuntimeDbContext>` verwenden (nicht direkt injizieren) wenn in Singleton-Services auf die DB zugegriffen wird
- Neue Audio-Warnungen als `.wav` in `wwwroot/audio/warnings/` ablegen
- Neue JS-Module als eigene Dateien in `wwwroot/js/` anlegen (kein eingebettetes JS in Razor-Dateien)
- UI auf Deutsch, Bootstrap 5.3+ mit Dark Mode und Bootstrap Icons (`bi-*`)

### DON'T ❌
- Keine Windows-spezifischen Pfade (`C:\`, Backslashes) im Server-Projekt
- Keine PowerShell/Batch-Scripts
- Keine Windows-Registry-Zugriffe im Server-Projekt
- Keine IIS-Konfiguration
- Keine Desktop-Verknüpfungen (.lnk)
- Keine Inno Setup Installer
- Kein direktes Einbetten von JS-Code in `.razor`-Dateien — stattdessen JS-Interop-Module in `wwwroot/js/`
- `EinsatzHub` nicht direkt aus Domain-Services aufrufen — immer über Relay-Services (`EinsatzHubRelayService`)
- Keine öffentlichen Endpunkte — VPN-interner Zugriff vorausgesetzt
- **`Einsatzueberwachung.LiveTracking`** ist eine Windows-WPF-App — dort KEINE Linux-spezifischen APIs verwenden; nur über HTTP-API mit dem Server kommunizieren
- Divera-API-Key niemals in `appsettings.json` — nur über `ISettingsService` / `StaffelSettings.json`

### Feature-Überblick (implementiert)
- **🌓 Dark Mode**: Persistente Einstellungen, Cross-Tab Sync via `theme-sync.js`
- **🗺️ Interaktive Karten (Leaflet.js)**: Suchgebiete, Marker, Polygone, GPS-Halsband-Live-Tracking auf Karte
- **📡 GPS-Halsband-Tracking**: Bis zu 20 Halsbänder gleichzeitig, Live-Position via Webhook-API, Bereichserkennung (im/außerhalb Suchgebiet), Relay via SignalR
- **🚶 Mensch-Laufweg (Handy-GPS)**: Team-Mobile (`/team`) sendet Smartphone-Positionen; wird als `HumanTrack` in Suche/Karte/PDF geführt
- **🐕 LiveTracking Desktop-App**: WPF-App liest USB-GPS-Gerät (Garmin Alpha) und sendet Positionen an Server-API; als ZIP-Download unter `/downloads/livetracking.zip`
- **👥 Team-Management**: Teams anlegen/bearbeiten/löschen, Timer mit Farbcodierung (Grün→Orange→Rot), Blink-Animation, Pause-Funktion für Hunde
- **📻 Funksprüche**: Chronologisch, mit Antwort-Threads, persistent in SQLite, Echtzeit-SignalR
- **📝 Notizen**: Globale & team-spezifische Notizen, Zeitstempel, Antwort-Threads
- **🔔 Audio-Warnungen**: `funk.wav` (Funkspruch), `glocke.wav` (Benachrichtigung), `kritisch.wav` (Kritischer Alarm)
- **🔗 Divera 24/7**: Alarme und Verfügbarkeitsstatus via REST-API, konfigurierbarer API-Key
- **🎓 Trainer-Modul**: Passwortgeschützter Bereich (`/trainer`), Übungsverwaltung, Szenario-Vorschläge (KI-gestützt), Trainer-Einträge, zeitgesteuerte Events
- **📡 Training-API**: REST-API für externe Trainings-Apps (`/api/training/*`), read/write steuerbar
- **🔄 GitHub Auto-Update**: Automatische Prüfung auf neue Releases, Update-Download und -Anwendung via `apply-update.sh`
- **🗺️ Statische Karten für PDF**: OSM-Tiles via `OsmStaticMapRenderer` für PDF-Einsatzberichte
- **📐 UTM-Koordinaten**: `UtmConverter` für UTM ↔ WGS84 Umrechnung
- **🌤️ Wetter (DWD/BrightSky)**: Wetteranzeige für Einsatzort
- **📊 Runtime-Persistenz**: Einsatzzustand überlebt Server-Neustarts (SQLite, alle 3 Sekunden gespeichert)
- **💾 Persistierte Live-Tracks**: Halsband-History und Handy-GPS-History (`PhoneTrackHistory`) werden im Runtime-Snapshot mitgespeichert
- **⌨️ Keyboard Shortcuts**: `keyboard-shortcuts.js`
- **📥 Downloads**: PDF-Berichte, Excel-Stammdaten, JSON-Export, ZIP-Backup, LiveTracking-App

---

## 4. Erfahrungen & Dynamische Regeln (Wird von KI gepflegt)
*(Liebe KI, füge hier neue Regeln im Format `[Datum] - [Regel]` hinzu, wenn du von Menschen korrigiert wurdest.)*

* [2026-04-28] - Initialer Start des Agenten-Gedächtnisses. Projektstandards, Architektur und Self-Improvement-Protokoll in `.github/copilot-instructions.md` als einziger Wahrheitsquelle zusammengeführt.
* [2026-05-05] - **RuntimeStatePersistenceService.Subscribe/Unsubscribe**: Jedes neue Event auf `IEinsatzService`, das persistenten Zustand mutiert, MUSS in `Subscribe()` UND `Unsubscribe()` in `RuntimeStatePersistenceService` eingetragen werden — sonst setzt kein Code `_isDirty = true`, der 3-Sekunden-Timer schreibt nichts in SQLite, und die Daten gehen beim nächsten Server-Neustart verloren. Beispiel: `TrackSnapshotAdded` und `CompletedSearchUpdated` fehlten, was dazu führte, dass importierte GPX-Tracks nicht persistiert wurden.
* [2026-05-09] - **JS-Interop `setOptions()` vor `loadHistory()`**: Wenn JS-Module (CollarTracking, PhoneTracking, teamMobileMap) konfigurierbare Darstellung (z.B. Icons) haben, muss `setOptions()` IMMER unmittelbar nach `initialize()` und VOR allen datenladeenden Aufrufen wie `loadHistory()` oder `setDogPosition()` aufgerufen werden — sonst werden bereits platzierte Marker mit dem Default-Icon erstellt.
* [2026-05-09] - **try-catch in `OnAfterRenderAsync` sauber aufteilen**: In `TeamMobile.razor` (und ähnlichen Seiten) darf der try-catch für `init` NICHT mit `setOptions`/Settings-Aufrufen vermischt werden. Wenn `init` fehlschlägt und `return` aufgerufen wird, ist das richtig; wenn aber `setOptions` fehlschlägt, war die Karte bereits initialisiert — die Fehlermeldung "map init failed" ist dann falsch und der `return` erzeugt eine leere, funktionslose Seite. Lösung: separater (toleranter) try-catch für `setOptions`.

---

## 5. Design-Kontext (Impeccable)
Für **alle UI-/Frontend-Arbeiten** sind zwei Dateien im Projekt-Root die Quelle der Wahrheit. Vor UI-Änderungen lesen:

- [`PRODUCT.md`](../PRODUCT.md) — **Strategie** (Register, Nutzer, Marken-Persönlichkeit, Anti-Referenzen, Designprinzipien). Register: **product** (Werkzeug, kein Marketing).
- [`DESIGN.md`](../DESIGN.md) — **visuelles System** im Google-Stitch-Format (Tokens, Farben, Typografie, Komponenten). Ergänzt durch den Sidecar `.impeccable/design.json`.

**Kernregeln, die jede UI-Änderung beachten muss:**
1. **Re-Derivation-Rule**: Keine Hex-Werte in Komponenten hardcoden — immer über die Token-Ebenen (`--theme-*`, `--ui-*`, `--signal-*`). Neue Screens unter **NRW + Ruhr + Dark Mode + jeder Intensität** (dezent/ausgewogen/lebhaft) testen.
2. **Status nie nur über Farbe**: Jeder Team-/Hunde-/Timer-Zustand braucht einen zweiten Kanal (Icon/Form/Label/Position). Der Timer-Verlauf Grün→Orange→Rot ist der Testfall (Farbsehschwäche + Sonnenlicht).
3. **Leise per Default**: Gesättigte Markenfarbe, Bewegung und Audio sind knappe Eskalation — nur für echte Aufmerksamkeitszustände, nie Dekoration. Überschriften sind blau, nicht Markenrot.
4. **Feldtauglich**: Touch-Ziele ≥44px, Fließtext/Muted-Text ≥4.5:1, besonders in den mobilen Ansichten.

Impeccable-Befehle (z. B. `/impeccable audit EinsatzMonitor`, `/impeccable critique`, `/impeccable polish`) lesen diese Dateien automatisch. Live-Mode ist für dieses Blazor-Server-Projekt nicht verfügbar (kein Vite/HMR oder statischer HTML-Entry).
