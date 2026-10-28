# Schnittstelle der Tracking-App (v1)

Verbindliche Beschreibung für die iOS- und die Android-App ([Einsatzueberwachung.App](https://github.com/Elemirus1996/Einsatzueberwachung.App)).
Änderungen hier zuerst, mit Tests im Server (`TrackingAppTests`). Neue Felder dürfen dazukommen; Apps ignorieren unbekannte Felder.

## Rolle Team

### 1. Anmeldung (wie bisher per QR-Code)

1. Die App scannt den **bestehenden** QR-Code aus dem Monitor (Team-Dialog): `https://<server>/team/login?code=<Anmelde-Code>`.
   Sie akzeptiert nur `https://`-Adressen und zeigt vor dem Öffnen den Servernamen zur Bestätigung.
2. Die App öffnet die Adresse in ihrer WebView. Das Mitglied meldet sich wie gewohnt an und wählt sein Team.
3. Die Team-Seite erkennt die App und übergibt die Zugangsdaten an die native Ortung:
   - iOS: `window.webkit.messageHandlers.einsatzApp.postMessage(json)` → `WKScriptMessageHandler` mit dem Namen **`einsatzApp`**
   - Android: `window.EinsatzAppAndroid.ortungUebergeben(json)` → `@JavascriptInterface` mit dem Namen **`EinsatzAppAndroid`**

   ```json
   { "apiVersion": 1, "server": "https://einsatz.example.org", "token": "a1.…", "teamName": "Team Rex", "expiresAt": "2026-10-29T10:00:00Z" }
   ```
   Die App übernimmt nur Übergaben, deren `server` mit der Adresse aus dem QR-Code übereinstimmt.
4. Das Token ist an Team und Einsatz gebunden und läuft spätestens nach 24 h ab. Es gehört in Keychain bzw. Keystore.

Alle Anfragen: `Authorization: Bearer <token>`, Basis `https://<server>/api/team-mobile/app/v1`.

### 2. `GET /config`

Nach der Übergabe und bei jedem App-Start.

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

Folgt (Phase 1, zweiter Teil): `api/el/app/v1/` nur aus dem VPN, Position der Einsatzleitung für die Karte im ELW.
