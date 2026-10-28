# Schnittstelle der Tracking-App (v1)

Verbindliche Beschreibung für die iOS- und die Android-App ([Einsatzueberwachung.App](https://github.com/Elemirus1996/Einsatzueberwachung.App)).
Änderungen hier zuerst, mit Tests im Server (`TrackingAppTests`). Neue Felder dürfen dazukommen; Apps ignorieren unbekannte Felder.

## Rolle Team

### 1. Anmeldung (wie bisher per QR-Code)

1. Die App scannt den **bestehenden** QR-Code aus dem Monitor (Team-Dialog): `https://<server>/team/login/<Anmelde-Code>`
   (ältere Form `https://<server>/team/login?code=<Anmelde-Code>` ebenfalls annehmen) oder das Mitglied tippt Server und Code ein.
   Die App akzeptiert nur `https://`-Adressen und zeigt vor dem Anmelden den Servernamen zur Bestätigung.
   Den Code gibt es nur während eines laufenden **Einsatzes** (nicht bei Übungen), wie auf der Team-Seite.
2. **Native Anmeldung** (Basis `https://<server>/api/team-mobile/app/v1`, ohne Token):

   `POST /login/teams` mit `{ "code": "<Anmelde-Code>" }` → Einsatz und Teamliste:
   ```json
   { "apiVersion": 1, "einsatz": "Vermisste Person · Testwald", "staffel": "RHS Musterstadt",
     "teams": [ { "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "…" } ] }
   ```
   `POST /login` mit `{ "code": "<Anmelde-Code>", "teamId": "…" }` → Token:
   ```json
   { "apiVersion": 1, "rolle": "team", "token": "a1.…", "teamId": "…", "teamName": "Team Rex", "expiresAt": "2026-10-29T10:00:00Z" }
   ```
   Leerzeichen und Kleinschreibung im Code sind egal. Falscher Code oder kein laufender Einsatz → `401`, unbekanntes Team → `404`.
   Der Code steht im Body, nicht in der Adresse, damit er nicht in Zugriffsprotokollen landet.
3. Alternativ (WebView, bisher): Die App öffnet die Adresse aus dem QR-Code, das Mitglied meldet sich auf der Team-Seite an,
   und die Seite übergibt die Zugangsdaten an die App:
   - iOS: `window.webkit.messageHandlers.einsatzApp.postMessage(json)` → `WKScriptMessageHandler` mit dem Namen **`einsatzApp`**
   - Android: `window.EinsatzAppAndroid.ortungUebergeben(json)` → `@JavascriptInterface` mit dem Namen **`EinsatzAppAndroid`**

   ```json
   { "apiVersion": 1, "rolle": "team", "server": "https://einsatz.example.org", "token": "a1.…", "teamName": "Team Rex", "expiresAt": "2026-10-29T10:00:00Z" }
   ```
   Die App übernimmt nur Übergaben, deren `server` mit der Adresse aus dem QR-Code übereinstimmt.
4. Das Token ist an Team und Einsatz gebunden und läuft spätestens nach 24 h ab. Es gehört in Keychain bzw. Keystore.

Alle Anfragen: `Authorization: Bearer <token>`, Basis `https://<server>/api/team-mobile/app/v1`.

### 2. `GET /config`

Nach der Anmeldung und bei jedem App-Start.

```json
{
  "apiVersion": 1,
  "teamId": "…", "teamName": "Team Rex", "dogName": "Rex",
  "einsatz": "Vermisste Person · Testwald", "staffel": "RHS Musterstadt",
  "positionUrl": "https://…/api/team-mobile/app/v1/positions",
  "statusUrl": "https://…/api/team-mobile/app/v1/status",
  "expiresAt": "2026-10-29T10:00:00Z",
  "mode": "track",
  "intervalSec": 20, "uploadEverySec": 60, "pauseCheckSec": 120
}
```

### 3. `POST /positions`

Gesammelte Positionen aus dem lokalen Puffer, höchstens 500 je Anfrage, höchstens 256 KB.

```json
{ "positions": [ { "lat": 49.31, "lng": 8.43, "accuracy": 8.5, "timestamp": 1761645600000, "battery": 72 } ] }
```

- `timestamp`: Aufnahmezeit am Handy, Unix-Millisekunden (UTC). Positionen aus Funklöchern kommen so mit Originalzeit an.
- `accuracy` in Metern, `battery` 0–100, beide optional.
- Unsinnige Koordinaten werden übergangen. Positionen nach Suchende verwirft der Server.

Antwort:
```json
{ "mode": "track", "intervalSec": 20, "uploadEverySec": 60, "pauseCheckSec": 120, "expiresAt": "…", "accepted": 2 }
```

Nach erfolgreicher Antwort (200) löscht die App die gesendeten Positionen aus dem Puffer. Bei Netzfehler oder 5xx bleiben sie und werden beim nächsten Takt erneut gesendet.

### 4. `GET /status`

Leichte Abfrage ohne GPS, im Modus `pause` alle `pauseCheckSec` Sekunden und beim App-Start. Antwort wie bei `/positions` (ohne `accepted`).

### 5. `mode` – was die App tun muss

| `mode` | App |
|---|---|
| `track` | Ortung an (`intervalSec`), puffern, alle `uploadEverySec` hochladen |
| `pause` | **Ortungsdienst beenden** (nicht nur das Senden), Puffer noch hochladen, dann alle `pauseCheckSec` `/status` abfragen |
| `stop` | Ortungsdienst beenden, Token und Puffer löschen, Rolle Team beenden |

Server-Logik: Suche läuft → `track`; Kurzpause, Suche nicht gestartet oder beendet → `pause`; Einsatz beendet oder Team gelöscht → `stop`.

**Ungültiges, abgelaufenes oder fremdes Token, Einsatz vorbei, Team gelöscht:** HTTP **410 Gone** mit `{ "mode": "stop" }`. Die App behandelt 410 immer als `stop` und wiederholt nichts.

**Sicherheitsnetz:** Erreicht die Uhr `expiresAt`, beendet die App die Ortung auch ohne Netz.

### Grenzen

- nginx: eigenes Limit für `/api/team-mobile/app/` (300 Anfragen/min je IP, Burst 120), weil sich im Mobilfunk viele Handys eine IP teilen.
- Die App sendet nur an den `server` aus der Übergabe und nur über HTTPS.

## Rolle Einsatzleitung

Zweck: Der Führungsassistent sieht auf der Einsatzkarte, wo die Einsatzleitung ist (roter Marker „EL“ mit Gerätename).

### 1. Anmeldung (wie bisher)

1. WireGuard-VPN ist aktiv. Die App öffnet die **interne** Adresse des Servers (vom Nutzer einmal eingegeben, z. B. aus dem VPN-QR-Code) und zeigt `/el`.
   Ohne VPN ist die Adresse nicht erreichbar → Hinweis „WireGuard einschalten“.
2. Anmeldung in der WebView mit dem Staffel-Passwort wie im Browser.
3. Die EL-Ansicht übergibt über dieselbe Brücke wie die Team-Seite:
   ```json
   { "apiVersion": 1, "rolle": "el", "server": "https://einsatz.intern", "token": "e1.…", "expiresAt": "…" }
   ```
   Die Team-Übergabe trägt `"rolle": "team"`. Die App übernimmt nur Übergaben, deren `server` der geöffneten Adresse entspricht.
4. Das Token gilt 24 h und nur, solange das Staffel-Passwort nicht geändert wird. Es erlaubt ausschließlich die zwei Endpunkte unten.

Basis `https://<interne Adresse>/api/el/app/v1`, Anmeldung `Authorization: Bearer <token>`.
Am öffentlichen Zugang sind diese Pfade gesperrt (404).

### 2. `POST /positions` und `GET /status`

Format wie bei der Team-Rolle (`positions`-Liste, Antwort mit `mode`, `intervalSec` = 30, `uploadEverySec` = 60, `pauseCheckSec`, `expiresAt`).
Der Server zeigt die jeweils neueste Position je Gerät; der Gerätename kommt aus den VPN-Geräten (Einstellungen → VPN-Geräte).

| Zustand | `mode` |
|---|---|
| Einsatz oder Übung läuft | `track` |
| kein Einsatz | `pause` – GPS aus, angemeldet bleiben; beim nächsten Einsatz ohne Eingabe wieder `track` |
| Token ungültig, abgelaufen, Passwort geändert | 410 `stop` – Token löschen, EL-Rolle neu anmelden |

Die Position der Einsatzleitung wird nicht gespeichert und erscheint nicht im Bericht. Bei Einsatzende verschwindet der Marker.
