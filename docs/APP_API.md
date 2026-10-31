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

Server-Logik: Suche läuft **oder Kurzpause** → `track`; Suche noch nicht gestartet oder beendet → `pause`; Einsatz beendet oder Team gelöscht → `stop`.

In der Kurzpause bleibt die Ortung also an (#133): Das Team ist weiter im Gelände. Diese Positionen zeigt der Server nur als
**aktuelle Position**, in den Laufweg der Suche (`/tracks`, Bericht) kommen sie nicht.

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
  "suche": { "status": "laeuft", "seit": "2026-10-28T08:40:00Z", "dauerSec": 2100, "darfSteuern": true },
  "suchgebiet": { "id": "…", "name": "Gebiet A", "farbe": "#2f6fd6", "punkte": [ { "lat": 49.31, "lng": 8.43 } ] },
  "halsband": { "id": "HB-7", "name": "Halsband 7", "lat": 49.312, "lng": 8.431, "zeit": "2026-10-28T09:14:48Z", "akku": 72 },
  "mitteilungen": [ { "id": "…", "text": "Bitte Funkkontakt", "zeit": "2026-10-28T09:10:00Z" } ]
}
```

- `suche.status`: `bereit` (noch nicht gesucht) · `laeuft` · `kurzpause` · `beendet`. `seit` = Beginn der laufenden bzw. letzten Suche, `null` bei `bereit`.
  `dauerSec` = Suchzeit (ohne Kurzpausen) wie im Monitor.
- `suche.darfSteuern`: `true`, wenn das Team die Suche selbst steuern darf (Abschnitt 11, Einstellung auf dem Server).
  Bei `false` blendet die App die Knöpfe aus.
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
| `pause` | „Pause“ (veraltet, siehe unten) |
| `funk` | „Funkkontakt benötigt“ |
| `beendet` | „Auftrag beendet“ (veraltet, siehe unten) |

- Antwort `200`: `{ "zeit": "…Z", "doppelt": false }`. Unbekannter Code → `400`.
- `id` (von der App erzeugt, max. 64 Zeichen) entdoppelt: Die App darf im Funkloch beliebig oft wiederholen, dieselbe `id`
  erzeugt nur **eine** Notiz (Antwort dann `"doppelt": true`).
- `pause` und `beendet` ersetzt `POST /suche` (Abschnitt 11). Der Server nimmt sie von älteren Apps weiter an, schreibt aber nur
  die Notiz und ändert die Suche nicht. Neue Apps schicken nur noch `im_gebiet` und `funk`.

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
- `stop`: **echtes Ende** – Einsatz beendet, Team gelöscht oder Token abgelaufen → wie `mode: stop`; der Server schließt danach die Verbindung.
- `verdraengt` `{"wiederholenNachSec":60}`: Je Team sind höchstens **10** Live-Verbindungen offen (Schutz vor Missbrauch; nach einem
  Funkloch können hängende Verbindungen kurz mitzählen); bei einer weiteren wird die älteste
  mit diesem Ereignis geschlossen. Das Gerät bleibt angemeldet, Ortung und Token laufen weiter. Es darf **nicht sofort** neu
  verbinden, sondern frühestens nach `wiederholenNachSec` Sekunden (sonst werfen sich mehrere Geräte im Kreis hinaus) und fragt bis
  dahin bei Bedarf `/lage` ab. Gezählt werden nur Verbindungen auf `/ereignisse`; die Team-Webseite im Browser nutzt sie nicht.
- Alle 20 s ein Kommentar `: ping` (hält Mobilfunk und nginx offen). Bricht die Verbindung ab, verbindet die App nach 5 s neu
  (`retry: 5000` wird mitgesendet) und lädt `/lage` neu – außer nach `verdraengt` (siehe oben).
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
- PNG, JPG und WEBP rechnet der Server in PNG um, **SVG rastert er** (Svg.Skia). Vorher entfernt er aus dem SVG Skripte,
  Ereignis-Attribute, DTD/Entitäten und alle Verweise nach außen; nachgeladen oder ausgeführt wird nichts. Das Ergebnis wird
  zwischengespeichert und nur bei einem neuen Logo neu erzeugt.
- `404`, wenn kein Logo hinterlegt ist oder die Datei nicht lesbar ist. Die App zeigt dann nur den Namen.

### 11. `POST /suche` – Team steuert die Suche (#133)

Ablauf im Einsatz: anmelden → zum Suchgebiet → „Suche starten“ → „Pause“ (Kurzpause, Ortung läuft weiter) → „Weiter“ →
„Suche beendet“ (Ortung aus).

```json
{ "id": "6f1c…-uuid-der-app", "aktion": "starten" }
```

| `aktion` | erlaubt bei `suche.status` | Wirkung wie im Monitor bzw. in `/el` |
|---|---|---|
| `starten` | `bereit`, `beendet` | Suche starten, mit dem Halsband, das dem Team schon zugewiesen ist (sonst ohne). Keine Halsband-Auswahl in der App. |
| `pause` | `laeuft` | Kurzpause, Suchzeit hält an, `mode` bleibt `track` |
| `weiter` | `kurzpause` | Suche fortsetzen |
| `beenden` | `laeuft`, `kurzpause` | Suche beenden (Track wird gespeichert), danach `mode = pause` |

Antwort `200`:

```json
{ "doppelt": false, "mode": "track", "suche": { "status": "laeuft", "seit": "…Z", "dauerSec": 0, "darfSteuern": true } }
```

- `id` (UUID der App, höchstens 64 Zeichen) entdoppelt wie bei `/statusmeldung`: Dieselbe `id` wird nur **einmal** ausgeführt,
  eine Wiederholung antwortet `200` mit `"doppelt": true` und dem aktuellen Stand. Abgelehnte Anfragen (`400`, `403`, `409`)
  merkt sich der Server nicht.
- Nicht erlaubter Übergang, z. B. `pause`, obwohl keine Suche läuft: `409` + `{ "error": "<kurzer Text>", "suche": { … } }`
  mit dem aktuellen Stand. Die App zeigt den Text und übernimmt den Stand (die Einsatzleitung kann inzwischen umgeschaltet haben).
- Einstellung „Teams dürfen die Suche selbst steuern“ aus: `403` + `{ "error": "…" }`. Standard ist **ein**.
- Fehlende `id` oder unbekannte `aktion`: `400`. Ungültiges Token: `410` + `{ "mode": "stop" }` wie überall.
- Danach kommt auf `/ereignisse` wie gewohnt `lage`.
- Im Monitor und in `/el` erscheint ein deutlicher Hinweis mit Ton („Team Rex hat die Suche gestartet (App)“), dazu eine Notiz
  mit Quelle „Team-App“ und ein Eintrag im Audit-Log. Die Einsatzleitung kann jederzeit korrigieren.

### 12. Datenschutzerklärung der Staffel (#148)

Verantwortlich für die Daten ist die Staffel, die den Server betreibt. Sie pflegt ihre Datenschutzerklärung unter
Einstellungen → Tracking-App (Vorlage per Knopf einfügbar, Platzhalter selbst ausfüllen). Der Server liefert sie an die App:

```json
"datenschutz": { "version": "3f9c0a7b12de4e55", "titel": "Datenschutzerklärung RHS Musterstadt", "text": "## 1. Wer ist verantwortlich?
…", "stand": "2026-10-31T09:00:00Z" }
```

| Wo | Inhalt |
|---|---|
| `POST login/teams` (Abschnitt 1, **vor** dem Token) | ganzes Objekt `datenschutz` |
| `GET config`, `GET lage` | nur `{ "version" }` |
| Einsatzleitung `POST /login` und `POST /verlaengern` | ganzes Objekt |
| Einsatzleitung `GET /lage` | nur `{ "version" }` |
| `GET datenschutz` (Team-Token) bzw. `GET /api/el/app/v1/datenschutz` (EL-Token) | ganzes Objekt, zum Nachlesen in den Einstellungen der App |

- `version`: ändert sich genau dann, wenn Titel oder Text geändert werden (Hash, 16 Zeichen). Die App speichert die Bestätigung
  **je Server-`kennung` und `version`** und fragt bei einer neuen `version` erneut, sobald sie sie in `config`/`lage` bemerkt.
- `text`: Markdown in einfacher Form (Überschriften `#`/`##`, Absätze, Listen mit `-`, **fett**, Links). Zeigt die App kein
  Markdown an, ist der Text trotzdem lesbar. Höchstens 50 000 Zeichen.
- `stand`: Zeitpunkt der letzten Änderung (UTC).
- Hat die Staffel nichts hinterlegt: `datenschutz: null` überall. Die App zeigt dann „Diese Staffel hat noch keine
  Datenschutzerklärung hinterlegt“ und lässt die Anmeldung trotzdem zu.
- Die Bestätigung („Zur Kenntnis genommen“) bleibt auf dem Gerät; der Server prüft sie nicht und lehnt keine Anmeldung ab.

**Vorschlag, nicht umgesetzt (Entscheidung Paul):** Protokoll der Bestätigungen per `POST datenschutz/bestaetigt { "version" }`
(Team-Token bzw. EL-Token) → `204`. Der Server schriebe Team bzw. Gerätename, `version` und Zeit ins Audit-Log oder in eine eigene,
dauerhafte Liste, die die Staffel unter Einstellungen einsehen kann. Bis zur Entscheidung gibt es den Endpunkt nicht (`404`).

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
   { "apiVersion": 1, "rolle": "el", "server": "https://einsatz.intern", "token": "e2.…", "expiresAt": "…" }
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

## Rolle Einsatzleitung – native App (#125)

Die App baut `/el`, `/el/start` und `/el/beenden` **nativ** nach. Dafür gelten zusätzlich die folgenden Endpunkte unter
`https://<interne Adresse>/api/el/app/v1`. Grundsätze:

- **Nur intern** (VPN). Am öffentlichen Zugang antwortet jeder Endpunkt `404`.
- Token wie oben (24 h, ungültig nach Änderung des Staffel-Passworts oder nach `/logout`), sonst `410` + `{"mode":"stop"}`.
- Zeiten UTC mit `Z` bzw. Unix-ms. Nicht in der App (bleibt im Monitor): Teams anlegen, Stammdaten, Einstellungen, Archiv.
- Jede Aktion steht mit dem Gerätenamen (VPN-Gerät, sonst „Einsatzleitung“) im Audit-Log. Token und Positionen nie.
- Fehler, die die App anzeigen soll (z. B. „Team läuft schon“, „Halsband vergeben“): `409` + `{ "error": "<kurzer Text>" }`.
  Falsche Eingaben: `400` + `{ "error": "…" }`.

### 4. `POST /login`, `POST /logout` und `POST /verlaengern`

```json
{ "passwort": "…", "geraet": "iPhone Anna" }
```

Antwort `200`:

```json
{ "apiVersion": 1, "rolle": "el", "token": "e3.…", "expiresAt": "…Z",
  "server": { "kennung": "…", "staffel": "RHS Musterstadt", "logoVersion": "a1b2…" } }
```

- Gleiche Sperre wie die Anmeldeseite im Browser (gemeinsamer Zähler je Absender-IP): nach 5 Fehlversuchen 5 Minuten gesperrt,
  dann `429` + `{ "error": "…", "wiederholenNachSec": 300 }`. Falsches Passwort `401`. Noch kein Staffel-Passwort eingerichtet `409`.
- `geraet` (optional, höchstens 40 Zeichen) dient nur der Anzeige im Audit-Log, falls das VPN-Gerät keinen Namen hat.
- nginx: strenges Limit wie `/team/login` (20/min je IP).
- `POST /logout` (mit Token) → `204`. Dieses Token ist danach ungültig, auch nach einem Neustart des Servers.

**`POST /verlaengern`** (#141, mit gültigem Token, Körper optional `{ "geraet": "iPhone Anna" }`): neues Token für einen langen Einsatz,
ohne das Staffel-Passwort erneut einzugeben. Antwort `200` wie `/login`, zusätzlich mit der ursprünglichen Anmeldung:

```json
{ "apiVersion": 1, "rolle": "el", "token": "e3.…", "expiresAt": "…Z",
  "anmeldungAt": "2026-10-31T06:00:00Z", "verlaengerbarBis": "2026-11-03T06:00:00Z",
  "server": { "kennung": "…", "staffel": "RHS Musterstadt", "logoVersion": "a1b2…" } }
```

- Nur während eines laufenden Einsatzes oder einer Übung, sonst `409` + `{ "error" }`. Die App meldet sich dann nach Ablauf normal neu an.
- Höchstens **72 h** ab der Anmeldung mit Passwort (`anmeldungAt`, steht signiert im Token und wandert beim Verlängern mit).
  Das neue Token läuft 24 h, aber nie über `verlaengerbarBis` hinaus. Ist dieser Zeitpunkt erreicht (das neue Token würde nicht länger
  gelten als das alte), → `409`: Anmeldung mit Passwort nötig.
- Das alte Token ist danach gesperrt wie nach `/logout` (`410`); es gilt immer nur eins. Laufende `/ereignisse`-Verbindungen mit dem
  alten Token enden beim nächsten Ping mit `stop` – die App verbindet sich mit dem neuen Token neu.
- Passwortänderung und `/logout` machen das neue Token wie bisher ungültig. Eintrag im Audit-Log mit Gerätename.
- Ungültiges Token → `410` + `{"mode":"stop"}`. Die App ruft `/verlaengern` auf, wenn weniger als 6 h Laufzeit übrig sind und ein Einsatz läuft.
- Token-Format ab jetzt `e3.…` (mit Anmeldezeitpunkt); `e2`-Tokens gelten bis zu ihrem Ablauf weiter, beim Verlängern zählt bei ihnen
  der Ausstellungszeitpunkt (Ablauf − 24 h) als Anmeldung.

### 5. `GET /lage`

```json
{
  "mode": "track", "serverZeit": "…Z",
  "einsatz": { "aktiv": true, "text": "Vermisste Person · Testwald", "stichwort": "…", "ort": "…", "szenario": "Flaeche",
               "uebung": false, "beginn": "…Z", "einsatzleiter": "…", "fuehrungsassistent": "…" },
  "teams": [ {
      "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "Anna", "helfer": ["Ben"],
      "suche": { "status": "laeuft", "seit": "…Z", "dauerSec": 2100 },
      "warnstufe": 0, "warnung1Min": 45, "warnung2Min": 60,
      "suchgebiet": { "id": "…", "name": "Gebiet A", "farbe": "#2f6fd6" },
      "halsband": { "id": "HB-7", "name": "Halsband 7", "zeit": "…Z", "akku": 72 },
      "handy": { "zeit": "…Z", "akku": 64 },
      "pause": { "erforderlichBis": "…Z" }
  } ],
  "wetter": { "temperatur": 12.5, "wind": 14, "beschreibung": "Bewölkt", "zeit": "…Z" }
}
```

- `einsatz.aktiv = false`: kein Einsatz, `teams` leer, `mode = pause`. `szenario`: `Unbestimmt` · `Mantrailer` · `Flaeche` · `Truemmer` · `Sonstige`.
- `suche.status` wie in der Team-Rolle (`bereit` · `laeuft` · `kurzpause` · `beendet`). `warnstufe`: 0 normal, 1 erste, 2 zweite Warnung.
- `halsband`, `handy`, `suchgebiet`, `pause` und `wetter` sind `null`, wenn nicht vorhanden. Die Positionen selbst liefert `/karte`.

### 6. Weitere Lese-Endpunkte

| Endpunkt | Inhalt |
|---|---|
| `GET /vermisste` | `{ "szenario", "mehrereErlaubt", "profile": […] }`, je Profil `profilId`, `version`, `vollerName`, `zuletztAktualisiert` und alle Felder wie in `/el` (siehe unten), dazu `checkliste` (`null` oder `{ "name", "punkte": [ { "id", "text", "typ", "pflicht", "auswahl", "wert", "erledigt" } ] }`) |
| `GET /karte` | `suchgebiete` (id, name, farbe, punkte, teamId, `abgeschlossen`, `abdeckungProzent` – `null`, solange nichts berechnet ist), `positionen` (`art`: `halsband` · `handy` · `el` · `elw`, `id`, `name`, `teamId`, `lat`, `lng`, `zeit`, `akku`; beim ELW sind `zeit` und `akku` `null`). Ohne laufenden Einsatz beide Listen leer |
| `GET /tracks?seit=<unix-ms>` | wie Abschnitt 7 der Team-Rolle, aber `laufend` als Liste je Team: `[ { "teamId", "hund": […], "mensch": […] } ]`, `abgeschlossen` mit `teamId` |
| `GET /notizen?vor=<id>&anzahl=50` | `{ "notizen": […], "weitere": true }` – Notizen und Funk wie in `/el`, **neueste zuerst**: `id`, `art` (`notiz` · `funk` · `status` · `system`), `text`, `zeit`, `von`, `teamId`, `loeschbar`, `antworten` `[ { "id", "text", "zeit", "von" } ]`. Blättern mit `vor` = älteste geladene `id`; `anzahl` höchstens 200 |
| `GET /halsbaender` | `{ "frei": [ { "id", "name", "zeit", "akku" } ], "zuletzt": [ { "teamId", "halsbandId" } ] }` – freie Halsbänder für die Rückfrage beim Teamstart, dazu je Team das zuletzt benutzte (nur Teams, die schon eins hatten) |
| `GET /einsatz/ergebnisse` | erlaubte Ergebnisse für das Einsatzende: `[ { "wert": "Uebung abgeschlossen", "anzeige": "Übung abgeschlossen" } ]` |
| `GET /einsatz/vorlage` | Auswahllisten wie in `/el/start`: `{ "aktiv", "szenarien": ["Mantrailer", …], "divera": [ { "id", "titel", "ort", "lat", "lng", "zeit" } ], "einsatzleiter": ["Anna Berg"], "fuehrungsassistenten": […], "ergebnisse": […], "kartenarten": [ { "wert": "Streets", "anzeige": "Straßenkarte" } ] }` – `divera` leer, wenn Divera nicht eingerichtet oder nicht erreichbar ist |

### 7. Aktionen

Alle Aktionen sind `POST` mit JSON und tragen eine `id` (UUID der App, höchstens 64 Zeichen). Dieselbe `id` wird nur **einmal**
ausgeführt, eine Wiederholung antwortet `200` mit `{ "doppelt": true }` (wie `/statusmeldung`). So darf die App im Funkloch puffern
und wiederholen.

| Endpunkt | Körper (zusätzlich zu `id`) | Wirkung wie in `/el` |
|---|---|---|
| `/teams/{teamId}/start` | `halsbandId` (optional, `null` = ohne Halsband) | Suche starten; Halsband vergeben → `409` |
| `/teams/{teamId}/pause`, `/weiter`, `/stopp`, `/zuruecksetzen` | – | Kurzpause, fortsetzen, Suche beenden (Track wird gespeichert), Suchzeit zurücksetzen |
| `/teams/{teamId}/mitteilung` | `text` | Mitteilung an das Team (in der Team-App als `mitteilung`) |
| `/notizen` | `text`, `teamId` (optional) | Notiz der Einsatzleitung |
| `/notizen/{notizId}/loeschen` | – | Notiz löschen |
| `/funk` | `text`, `teamId` (optional), `antwortAuf` (optional) | Funkspruch oder Antwort |
| `/vermisste` | Profil wie bei `GET`, ohne `profilId` = neu; beim Ändern mit `version` | anlegen oder ändern (siehe unten) |
| `/vermisste/{profilId}/entfernen` | – | entfernen |
| `/vermisste/{profilId}/checkliste` | `punktId`, `erledigt` (bei `typ = jaNein`) bzw. `wert` (bei `auswahl` und `text`) | Checklistenpunkt setzen, ohne `version` |
| `/szenario` | `szenario` | Szenario des Einsatzes |
| `/einsatz/start` | `stichwort`, `ort`, `lat`/`lng` (optional), `szenario`, `uebung`, `einsatzleiter`, `fuehrungsassistent`, `diveraAlarmId` (optional) | wie `/el/start`; läuft schon ein Einsatz → `409` |
| `/einsatz/beenden` | `ergebnis` (`wert` aus `/einsatz/ergebnisse`), `ressourcenGeprueft` (muss `true` sein), `pdf`: `{ "tracks": true, "kartenart": "Streets", "teamIds": null }` – `kartenart`: `Streets` · `Topographic` · `Satellite`, sonst `400` | wie `/el/beenden` |

Antwort jeder Aktion: `200` + `{ "doppelt": false, … }`, beim Anlegen mit der neuen Kennung des Objekts (z. B. `notizId`).

Umgesetzt (Stufe 3), im Einzelnen:

- Team-Aktionen antworten mit `suche` wie in `/lage`. Nicht erlaubte Übergänge → `409` (z. B. `pause`, obwohl keine Suche läuft;
  `zuruecksetzen` während die Suche läuft; Hund läuft schon in einem anderen Team). Unbekanntes Team → `404` + `{ "error" }`.
- `start` mit `halsbandId`: nur ein freies oder das dem Team schon zugewiesene Halsband, sonst `409`. Ohne `halsbandId` startet
  die Suche mit dem bereits zugewiesenen Halsband (falls vorhanden). `stopp` gibt das Halsband frei.
- `mitteilung` → `{ "mitteilungId" }`. `notizen` → `{ "notizId" }`; mit `teamId` steht der Teamname vor dem Text.
- `notizen/{id}/loeschen`: nur Notizen mit `loeschbar = true` (die der Einsatzleitung), sonst `404`. Funk und Team-Meldungen bleiben.
- `funk` → `{ "notizId" }` (Kennung in `/notizen`); mit `teamId` wie der Empfänger-Knopf in `/el` („@Team Rex …“).
  `antwortAuf` = `id` aus `/notizen` → `{ "notizId", "antwortId" }`, unbekannt → `404`.
- `text` ist Pflicht (höchstens 1000 Zeichen), sonst `400`. Ohne laufenden Einsatz → `409`.
- Nur erfolgreiche Aktionen werden für die Entdopplung gemerkt; eine abgelehnte `id` darf die App erneut senden.

Einsatz starten und beenden (Stufe 5 umgesetzt):

- `/einsatz/start`: `ort` und `szenario` (nicht `Unbestimmt`) sind Pflicht, sonst `400` – außer der Ort kommt aus dem Divera-Alarm.
  Mit `diveraAlarmId` übernimmt der Server Ort, Koordinate, Stichwort, Melder, Nummer, Lagetext und Alarmzeit wie `/el/start`;
  mitgeschickte Angaben der App gehen vor. Unbekannter Alarm → `404`. `lat`/`lng` nur zusammen, setzen Kartenadresse und ELW-Position.
  `uebung` (Standard `false`). Läuft schon ein Einsatz → `409`. Antwort `{ "einsatz": { "aktiv": true, "beginn": "…Z" } }`.
- `/einsatz/beenden`: `ergebnis` muss ein `wert` aus `/einsatz/ergebnisse` sein, `ressourcenGeprueft` muss `true` sein, sonst `400`.
  Optional `bemerkungen` und `endzeit` als Zeitpunkt in **UTC** (z. B. `"2026-10-31T17:45:00Z"`, auch mit Versatz wie `+01:00`;
  ohne Angabe = jetzt). Ohne Zeitzone, in der Zukunft oder vor dem Einsatzbeginn → `400`. Für den Bericht rechnet der Server in
  Ortszeit um. `pdf.tracks` Standard `true`, `pdf.teamIds` `null` = alle Teams. Die Vor-Ort-Liste übernimmt
  der Server aus den eingeteilten Teams (wie die Vorauswahl in `/el/beenden`). Antwort `{ "archivId", "einsatzNummer",
  "pdf": { "erstellt": true, "fehler": null } }`; den Bericht gibt es danach im Archiv. Schon an einem anderen Leitungsplatz beendet
  oder kein laufender Einsatz → `409`. Danach kommt auf `/ereignisse` `einsatz` mit `{ "aktiv": false }`.

**Beispiel `GET /vermisste`** (Stufe 4a, so liefert es der Server):

```json
{
  "szenario": "Mantrailer",
  "mehrereErlaubt": false,
  "profile": [ {
    "profilId": "3f2b8c1e-…", "version": "a1b2c3d4e5f60718", "vollerName": "Erna Muster", "zuletztAktualisiert": "2026-10-29T08:12:00Z",
    "vorname": "Erna", "nachname": "Muster", "alter": "82", "geburtsdatum": "03.04.1944",
    "beschreibung": "165 cm, schlank, graue kurze Haare", "kleidung": "rote Jacke, Hausschuhe", "besonderheiten": "Hörgerät",
    "zuletztGesehenOrt": "Seniorenheim Lindenhof, Haupteingang", "zuletztGesehenZeit": "gestern ca. 18:30", "zuletztGesehenVon": "Pflegekraft",
    "vorerkrankungen": "Demenz", "medikamente": "Blutverdünner",
    "orientierung": "Eingeschraenkt", "mobilitaet": "Rollator", "suizidrisiko": "Nein", "bewaffnet": "Unbekannt",
    "polizeiKontaktName": "PHK Meier", "polizeiDienstnummer": "", "polizeiTelefon": "0621 …",
    "polizeiVermisstenmeldungAufgenommen": true, "polizeiKoordinationBesprochen": false, "polizeiSuchabschnittAbgestimmt": false,
    "polizeiRueckmeldepflichtVereinbart": false, "polizeiDatenschutzGeklaert": false,
    "bosEinheit": "", "bosZugfuehrer": "", "bosFunkrufname": "", "bosAufgabenteilung": "",
    "bosAbschnittAbgestimmt": false, "bosRessourcenBesprochen": false,
    "checkliste": { "name": "Mantrailer", "punkte": [
      { "id": "9c…", "text": "Geruchsstück gesichert", "typ": "jaNein", "pflicht": true, "auswahl": [], "wert": "true", "erledigt": true },
      { "id": "7d…", "text": "Art des Geruchsstücks", "typ": "auswahl", "pflicht": false, "auswahl": ["Kleidung", "Bettwäsche"], "wert": null, "erledigt": false },
      { "id": "5e…", "text": "Bemerkung", "typ": "text", "pflicht": false, "auswahl": [], "wert": "", "erledigt": false } ] }
  } ]
}
```

- **Typen:** alle Textfelder sind `string` (nie `null`, leer = `""`), die Ja/Nein-Felder `bool`, Auswahlen `string` aus der Liste unten.
  Ein Feld „Geschlecht“ gibt es im Server nicht.
- **Pflichtfelder:** keine – auch ein leeres Profil ist erlaubt (Anzeige dann „Unbekannt“ in `vollerName`).
- **Zuletzt gesehen:** alles Freitext wie in `/el`. `zuletztGesehenOrt` ist ein Text **ohne Koordinate**, `zuletztGesehenZeit` ist ein
  **Freitext** (z. B. „gestern ca. 18:30“), keine UTC-Zeit. Nur `zuletztAktualisiert` ist eine echte Zeit (UTC mit `Z`).
- **Szenario:** Die Felder sind in allen Szenarien gleich. Vom Szenario hängen nur ab: `mehrereErlaubt` (`true` bei `Flaeche` und
  `Truemmer`, sonst nur ein Profil) und die Checkliste – ein neues Profil bekommt die Vorlage des aktuellen Szenarios aus den Stammdaten
  (bei `Unbestimmt` keine, dann `checkliste: null`). Die Checkliste ändert sich nicht, wenn später das Szenario wechselt.
- **`POST /vermisste`:** `{ "id": "<uuid>", "profilId": "…", "version": "…", <Felder> }`.
  Neu: ohne `profilId` und ohne `version`, beliebige Felder. Ändern: `profilId` und `version` Pflicht.
  **Weggelassene Felder bleiben unverändert.** Ein mitgeschicktes `""` (oder `null`) bei einem Textfeld **leert** es. Bei Ja/Nein und
  Auswahlen ist `null` ein Fehler (`400`). `checkliste`, `vollerName` und `zuletztAktualisiert` werden beim Speichern nicht angenommen
  (`400` als unbekanntes Feld) – die Checkliste ändert die App über `/vermisste/{profilId}/checkliste`.

**Felder eines Vermisstenprofils** (Stufe 4a umgesetzt), Namen wie im Server-Modell: Texte `vorname`, `nachname`, `alter`,
`geburtsdatum`, `beschreibung`, `kleidung`, `besonderheiten`, `zuletztGesehenOrt`, `zuletztGesehenZeit`, `zuletztGesehenVon`,
`vorerkrankungen`, `medikamente`, `polizeiKontaktName`, `polizeiDienstnummer`, `polizeiTelefon`, `bosEinheit`, `bosZugfuehrer`,
`bosFunkrufname`, `bosAufgabenteilung` (höchstens 2000 Zeichen); Ja/Nein `polizeiVermisstenmeldungAufgenommen`,
`polizeiKoordinationBesprochen`, `polizeiSuchabschnittAbgestimmt`, `polizeiRueckmeldepflichtVereinbart`, `polizeiDatenschutzGeklaert`,
`bosAbschnittAbgestimmt`, `bosRessourcenBesprochen`; Auswahlen `orientierung` (`Unbekannt` · `Gut` · `Eingeschraenkt`),
`mobilitaet` (`Unbekannt` · `ZuFuss` · `Rollator` · `Rollstuhl` · `Fahrzeug`), `suizidrisiko` und `bewaffnet` (`Unbekannt` · `Nein` · `Ja`).
Beim Speichern übernimmt der Server **nur die mitgeschickten Felder**; unbekannte Felder oder Werte → `400`. Ein zweites Profil in
einem Szenario, das nur eine Person erlaubt (`mehrereErlaubt = false`) → `409`. Antwort `{ "profilId", "version" }`.
`/szenario`: `Unbestimmt` · `Mantrailer` · `Flaeche` · `Truemmer` · `Sonstige`, sonst `400`.

**Gleichzeitiges Bearbeiten von Vermissten:** `GET /vermisste` liefert je Profil eine `version` (Text, ändert sich bei jeder
Speicherung, auch durch `/el` im Browser mit Auto-Save). Beim Ändern schickt die App die `version` mit, auf der ihre Bearbeitung
beruht. Weicht sie ab, speichert der Server nicht und antwortet `409` + `{ "error": "Das Profil wurde inzwischen geändert.",
"profil": { …aktueller Stand mit neuer version… } }`; die App zeigt den neuen Stand und lässt neu bearbeiten. Ohne `version`
→ `400`. Die Antwort auf erfolgreiches Speichern enthält die neue `version`.

### 8. `GET /ereignisse` – Live

Wie Abschnitt 9 der Team-Rolle (Server-Sent Events, Token nur im Header, `retry: 5000`, `: ping` alle 20 s, `verdraengt`),
aber mit diesen Ereignissen:

| Ereignis | Daten | App |
|---|---|---|
| `team` | `{ "teamId" }` | Team geändert (Suche, Status, Gebiet, Halsband), `/lage` neu laden |
| `position` | `{ "art", "id", "teamId", "lat", "lng", "zeit", "akku" }` | Marker auf der Karte bewegen |
| `notiz` / `funk` | `{ "id" }` | `/notizen` neu laden |
| `vermisste` | `{}` | `/vermisste` neu laden |
| `einsatz` | `{ "aktiv": true }` | gestartet oder beendet, `/lage` neu laden |
| `warnung` | `{ "teamId", "stufe": 1, "text" }` | Warnton bzw. Vibration (`stufe` 1 oder 2) |

`position`: bei `art = halsband` ist `id` die Halsband-Kennung (`teamId` des Teams, das es trägt, sonst `null`), bei `handy`
die `teamId`, bei `el` der Gerätename (`teamId = null`). `notiz`/`funk` kommen bei neuen Einträgen und Antworten,
`notiz` auch beim Löschen über die App (`id` = Eintrag in `/notizen`; bei einer neuen EL-Notiz deren `id`). Ein Token, das abgemeldet wurde oder abgelaufen ist, beendet die Verbindung spätestens beim nächsten Ping mit `stop`.

Die Grenze von 10 Verbindungen gilt **je Token**, nicht je Team. Bei Einsatzende kommt `einsatz` mit `{ "aktiv": false }`,
**nicht** `stop`: Die Einsatzleitung bleibt angemeldet. `stop` nur bei ungültigem Token.

### 9. Reihenfolge der Umsetzung

1. diese Beschreibung, 2. `/login`, `/logout`, `/lage`, `/ereignisse`, 3. Team-Aktionen, Notizen, Funk, Mitteilung,
4. Vermisste, Karte, Tracks, Halsbänder, 5. Einsatz starten und beenden. Bis ein Teil umgesetzt ist, antwortet sein Endpunkt `404`.
