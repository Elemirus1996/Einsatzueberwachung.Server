# Schnittstelle der Tracking-App (v1)

Verbindliche Beschreibung für die iOS- und die Android-App ([Einsatzueberwachung-App](https://github.com/Elemirus1996/Einsatzueberwachung-App)).
Änderungen hier zuerst, mit Tests im Server (`TrackingAppTests`). Neue Felder dürfen dazukommen; Apps ignorieren unbekannte Felder.

**Zeiten:** In der ganzen App-Schnittstelle sind Zeitpunkte UTC – als ISO 8601 **mit `Z`** (z. B. `2026-10-28T09:15:00Z`)
oder als Unix-Millisekunden, wo es angegeben ist. Ortszeit ohne Zone kommt nicht vor.

## Rolle Team

### 1. Anmeldung (wie bisher per QR-Code)

1. Die App scannt den **bestehenden** QR-Code aus dem Monitor (Team-Dialog): `https://<server>/team/login/<Anmelde-Code>`
   (ältere Form `https://<server>/team/login?code=<Anmelde-Code>` ebenfalls annehmen) oder das Mitglied tippt Server und Code ein.
   Die App akzeptiert nur `https://`-Adressen und zeigt vor dem Anmelden den Servernamen zur Bestätigung.
   Den Code gibt es nur, solange ein Einsatz oder eine Übung läuft (Einsatzort eingetragen, nicht beendet).
2. **Native Anmeldung** (Basis `https://<server>/api/team-mobile/app/v1`, ohne Token):

   `POST /login/teams` mit `{ "code": "<Anmelde-Code>" }` → Einsatz und Teamliste:
   ```json
   { "apiVersion": 1, "einsatz": "Vermisste Person · Testwald", "uebung": false, "staffel": "RHS Musterstadt",
     "teams": [ { "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "…" } ] }
   ```
   `POST /login` mit `{ "code": "<Anmelde-Code>", "teamId": "…" }` → Token:
   ```json
   { "apiVersion": 1, "rolle": "team", "token": "a1.…", "teamId": "…", "teamName": "Team Rex", "expiresAt": "2026-10-29T10:00:00Z" }
   ```
   `uebung` ist `true` bei einer Übung – die App zeigt das deutlich an.
   Leerzeichen und Kleinschreibung im Code sind egal. Die Anmeldung ist strenger begrenzt (20 Anfragen/min je IP, wie `/team/login`). Falscher Code oder kein laufender Einsatz → `401`, unbekanntes Team → `404`.
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
  "einsatz": "Vermisste Person · Testwald", "uebung": false, "staffel": "RHS Musterstadt",
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

### 6. `GET /lage` – native Team-Ansicht (#119)

Alles, was die Ansicht beim Öffnen braucht (Grundlage: heutige Team-Seite).

```json
{
  "mode": "track",
  "serverZeit": "2026-10-28T09:15:00Z",
  "einsatz": { "text": "Vermisste Person · Testwald", "uebung": false, "staffel": "RHS Musterstadt" },
  "team": { "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "Anna", "helfer": ["Ben"] },
  "suche": { "status": "laeuft", "seit": "2026-10-28T08:40:00Z", "dauerSec": 2100 },
  "suchgebiet": { "id": "…", "name": "Gebiet A", "farbe": "#2f6fd6", "punkte": [ { "lat": 49.31, "lng": 8.43 } ] },
  "halsband": { "id": "HB-7", "name": "Halsband 7", "lat": 49.312, "lng": 8.431, "zeit": "2026-10-28T09:14:48Z", "akku": 72 },
  "mitteilungen": [ { "id": "…", "text": "Bitte Funkkontakt", "zeit": "2026-10-28T09:10:00Z" } ]
}
```

- `suche.status`: `bereit` (noch nicht gesucht) · `laeuft` · `kurzpause` · `beendet`. `seit` = Beginn der laufenden bzw. letzten Suche, `null` bei `bereit`.
  `dauerSec` = Suchzeit (ohne Kurzpausen) wie im Monitor.
- `suchgebiet` und `halsband` sind `null`, wenn keins zugewiesen ist bzw. noch keine Position vorliegt. `akku` fehlt, wenn unbekannt.
- `helfer` ist eine Liste (ein Team kann mehrere Helfer haben).
- `mitteilungen`: die letzten 20 Mitteilungen der Einsatzleitung an dieses Team, **älteste zuerst**. Sie bleiben für den
  laufenden Einsatz gespeichert (auch über einen Neustart des Servers), damit die App sie nach Funkloch oder Hintergrund nachholt.
  `id` ist stabil – „neu“ und „gelesen“ verwaltet die App auf dem Gerät.

### 7. `GET /tracks` und `GET /tracks?seit=<unix-ms>`

```json
{
  "serverZeit": 1761642900000,
  "laufend": {
    "hund":   [ { "lat": 49.31, "lng": 8.43, "t": 1761641000000 } ],
    "mensch": [ { "lat": 49.30, "lng": 8.42, "t": 1761641005000 } ]
  },
  "abgeschlossen": [
    { "sucheId": "…", "beginn": "…Z", "ende": "…Z",
      "tracks": [ { "art": "hund", "farbe": "#d32f2f", "punkte": [ { "lat": 49.3, "lng": 8.4, "t": 1761630000000 } ] } ] }
  ]
}
```

- `laufend`: Halsband-Track (`hund`) und Handy-Laufweg (`mensch`) der **laufenden** Suche dieses Teams, nach Zeit sortiert. Leer, wenn keine Suche läuft.
- `abgeschlossen`: frühere Suchen dieses Teams im Einsatz. `art` ist `hund` oder `mensch`.
- Mit `seit`: nur Punkte der laufenden Suche mit `t > seit`, `abgeschlossen` entfällt.
  Hinweis: Positionen, die erst nach einem Funkloch mit älterer Zeit eintreffen, liefert `seit` nicht nach.
  Deshalb lädt die App bei jedem `lage`-Ereignis und spätestens alle 5 Minuten einmal **ohne** `seit`.

### 8. `POST /statusmeldung`

```json
{ "id": "6f1c…-uuid-der-app", "code": "im_gebiet" }
```

| `code` | Notiz im Einsatz (wie die Knöpfe der Team-Seite) |
|---|---|
| `im_gebiet` | „im Suchgebiet eingetroffen“ |
| `pause` | „Pause“ |
| `funk` | „Funkkontakt benötigt“ |
| `beendet` | „Auftrag beendet“ |

- Antwort `200`: `{ "zeit": "…Z", "doppelt": false }`. Unbekannter Code → `400`.
- `id` (von der App erzeugt, max. 64 Zeichen) entdoppelt: Die App darf im Funkloch beliebig oft wiederholen, dieselbe `id`
  erzeugt nur **eine** Notiz (Antwort dann `"doppelt": true`).

### 9. `GET /ereignisse` – Live (Server-Sent Events)

`Accept: text/event-stream`, Token **nur** im `Authorization`-Header (nie in der Adresse).
Ungültiges Token beim Verbinden → `410` + `{"mode":"stop"}` wie überall.

```
event: halsband
data: {"lat":49.312,"lng":8.431,"zeit":"2026-10-28T09:14:48Z","akku":72}

event: mitteilung
data: {"id":"…","text":"Bitte Funkkontakt","zeit":"2026-10-28T09:10:00Z"}

event: lage
data: {}

event: stop
data: {}

: ping
```

- `halsband`: neue Position des Halsbands dieses Teams.
- `mitteilung`: neue Mitteilung der Einsatzleitung (zusätzlich in `/lage` gespeichert).
- `lage`: Suche, Gebiet, Halsband-Zuordnung oder Team geändert → App lädt `/lage` (und `/tracks` ohne `seit`) neu.
- `stop`: Einsatz beendet oder Team gelöscht → wie `mode: stop`; der Server schließt danach die Verbindung.
- Alle 20 s ein Kommentar `: ping` (hält Mobilfunk und nginx offen). Bricht die Verbindung ab, verbindet die App nach 5 s neu
  (`retry: 5000` wird mitgesendet) und lädt `/lage` neu.
- Rückfall ohne Live-Verbindung: `/lage` alle 15 s, solange die Ansicht im Vordergrund ist. Im Hintergrund nichts davon – nur die Ortung.

### 10. Heimatstaffel: Server-Kennung und Logo (#121)

`GET /config` und `POST /login/teams` enthalten zusätzlich (das bisherige Textfeld `staffel` bleibt unverändert):

```json
"server": { "kennung": "7f3c9a1e-…", "staffel": "RHS Musterstadt", "logoVersion": "a1b2c3d4e5f60718" }
```

- `kennung`: feste Kennung dieses Servers (UUID), einmal erzeugt und dauerhaft gespeichert. Ändert sich nicht bei neuer Adresse,
  neuem Staffelnamen oder Neustart. Nicht geheim. Daran erkennt die App die **Heimatstaffel** wieder.
- `logoVersion`: ändert sich genau dann, wenn ein anderes Logo hinterlegt wird; `null`, wenn es für die App kein Logo gibt.
  Die App lädt das Logo nur neu, wenn sich `logoVersion` gegenüber ihrer gespeicherten Fassung ändert.

`GET /logo` (Token wie überall, ungültig → `410` + `stop`):

- `200` mit `Content-Type: image/png`, höchstens 512 × 512 px (Seitenverhältnis bleibt), `ETag: "<logoVersion>"`,
  `Cache-Control: private, max-age=86400`. Mit `If-None-Match: "<logoVersion>"` → `304`.
- `404`, wenn kein Logo hinterlegt ist **oder** das Logo ein SVG ist. Der Server rechnet PNG, JPG und WEBP in PNG um; SVG kann er
  nicht umwandeln. Für die App muss die Staffel dann ein PNG oder JPG hochladen (Hinweis in Einstellungen → Staffel).
  Die App zeigt bei `404` nur den Namen.

### Grenzen

- nginx: eigenes Limit für `/api/team-mobile/app/` (300 Anfragen/min je IP, Burst 120), weil sich im Mobilfunk viele Handys eine IP teilen.
  Die Anmeldung (`/login`, `/login/teams`) ist strenger begrenzt (20/min je IP, wie `/team/login`).
- Bei Überschreitung antwortet nginx mit **429**. Die App zeigt dann „Zu viele Versuche, bitte kurz warten“ und versucht es frühestens nach 30 s erneut.
- `/ereignisse` läuft über einen eigenen nginx-Block ohne Pufferung und mit langer Laufzeit.
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

Die Position der Einsatzleitung wird nicht gespeichert und erscheint nicht im Bericht.

### 3. `GET /logo`

Wie in der Team-Rolle (Abschnitt 10), mit EL-Token, nur intern. Kennung und `logoVersion` liefert die native EL-Anmeldung (Phase 1b)
im selben Objekt `server`. Bei Einsatzende verschwindet der Marker.
