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
  "team": { "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "Anna", "helfer": ["Ben"], "einsatzart": "Flaeche" },
  "suche": { "status": "laeuft", "seit": "2026-10-28T08:40:00Z", "dauerSec": 2100, "darfSteuern": true },
  "suchgebiet": { "id": "…", "name": "Gebiet A", "farbe": "#2f6fd6", "punkte": [ { "lat": 49.31, "lng": 8.43 } ] },
  "halsband": { "id": "HB-7", "name": "Halsband 7", "lat": 49.312, "lng": 8.431, "zeit": "2026-10-28T09:14:48Z", "akku": 3 },
  "mitteilungen": [ { "id": "…", "text": "Bitte Funkkontakt", "zeit": "2026-10-28T09:10:00Z" } ]
}
```

- `suche.status`: `bereit` (noch nicht gesucht) · `laeuft` · `kurzpause` · `beendet`. `seit` = Beginn der laufenden bzw. letzten Suche, `null` bei `bereit`.
  `dauerSec` = Suchzeit (ohne Kurzpausen) wie im Monitor.
- `suche.darfSteuern`: `true`, wenn das Team die Suche selbst steuern darf (Abschnitt 11, Einstellung auf dem Server).
  Bei `false` blendet die App die Knöpfe aus.
- `suchgebiet` und `halsband` sind `null`, wenn keins zugewiesen ist bzw. noch keine Position vorliegt. `akku` fehlt, wenn unbekannt.
- Garmin-Halsband-`akku` ist eine Stufe, kein Prozentwert: 4 voll, 3 hoch, 2 mittel, 1 niedrig (Warnung). Die Bedeutung von 0 und 1 ist noch nicht am Gerät bestätigt; 0 löst keine Akkuwarnung aus. Unbekannte bzw. ungültige Werte sind `null` oder fehlen. Handy-Akkuwerte sind dagegen Prozentwerte.
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
- `abgeschlossen`: frühere Suchen dieses Teams im Einsatz. `art` ist `hund` oder `mensch`. Ab Server 1.17.0 je Suche `einsatzart` (EL-Rolle Abschnitt 10).
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

**Protokoll der Bestätigungen: bewusst nicht vorgesehen** (Entscheidung 31.10.2026). Es gibt keinen Endpunkt dafür; der Server
erfährt nicht, wer bestätigt hat.

Umgesetzt (#148). Ohne gepflegten Text liefert `GET datenschutz` `404`.

### 13. Navigationsziele und Punkte (Variante C)

Zwei Richtungen: Die Einsatzleitung schickt einem Team einen Kartenpunkt als **Ziel**, das Team startet damit seine Navi-App.
Das Team setzt an seinem Standort einen **Punkt** (Fund, Hinweis), der bei der Einsatzleitung live auf der Karte erscheint.

**EL → Team: Ziel in der Mitteilung.** Ein Ziel kommt als `mitteilung` mit dem zusätzlichen Feld `ziel` (in `/lage` und auf
`/ereignisse`). Mitteilungen ohne Ziel haben `ziel: null` bzw. kein Feld.

```json
{ "id": "…", "text": "Ziel: Wegkreuzung Nord", "zeit": "2026-10-31T09:10:00Z",
  "ziel": { "punktId": "…", "name": "Wegkreuzung Nord", "lat": 49.312345, "lng": 8.431234,
            "links": { "geo": "geo:49.312345,8.431234?q=49.312345,8.431234(Wegkreuzung%20Nord)",
                       "appleKarten": "https://maps.apple.com/?ll=49.312345,8.431234&q=Wegkreuzung%20Nord",
                       "googleMaps": "comgooglemaps://?daddr=49.312345,8.431234&directionsmode=walking",
                       "waze": "waze://?ll=49.312345,8.431234&navigate=yes" } } }
```

- Die App zeigt einen Hinweis „Neues Ziel: <name>“ mit „Navigation starten“.
  **Android** öffnet `links.geo` als Intent mit Auswahl (`Intent.createChooser`). **iOS** bietet die installierten Apps an
  (`canOpenURL` auf `comgooglemaps://` und `waze://`, beide in `LSApplicationQueriesSchemes`); Apple Karten immer.
  Ist nur Apple Karten da, öffnet die App es direkt.
- `lat`/`lng` mit 6 Nachkommastellen, `name` höchstens 60 Zeichen (ohne Klammern). Die Links baut der Server (`NavigationsLink`);
  die App setzt keine eigenen zusammen.
- Das Ziel ist ein Schnappschuss: Verschiebt die Einsatzleitung den Punkt danach, ändert sich das gesendete Ziel nicht; sie schickt es neu.
- „neu“/„gelesen“ wie bei jeder Mitteilung. Ton: eigene Tonart „Navigationsziel“ (Einstellung in der App, siehe Einsatzleitung Abschnitt 7).

**Team → EL: `POST /punkt`.**

```json
{ "id": "6f1c…-uuid-der-app", "text": "Fund: Jacke", "lat": 49.312345, "lng": 8.431234, "genauigkeit": 8, "zeit": "2026-10-31T09:12:03Z" }
```

- `typ` (ab Server 1.17.0, optional): `hinweis` (Standard) oder `fund`. Ein Fund erscheint auf der Karte mit eigenem Symbol und
  wird beim Einsatzende als Fundort vorgeschlagen (EL-Rolle Abschnitt 10). Unbekannter `typ` → `400`; ältere Server ignorieren das Feld.
  Die App bietet beim Punkt „Fund“ und „Hinweis“ zur Auswahl an.
- `text` Pflicht, höchstens 200 Zeichen. `lat`/`lng` Pflicht, gültiger Bereich. `genauigkeit` (Meter, optional) und `zeit`
  (Zeitpunkt der Ortung, UTC mit `Z`, optional; ohne Angabe = Eingang beim Server) aus der Ortung des Handys.
- Die App nimmt die **aktuelle** Position (nicht älter als 60 s, sonst neu orten). Ohne Ortungsfreigabe gibt es den Knopf nicht.
- Antwort `200`: `{ "doppelt": false, "punktId": "…" }`. Entdopplung über `id` wie bei `/statusmeldung` (Wiederholung im Funkloch
  erzeugt nur einen Punkt, Antwort dann `"doppelt": true` mit derselben `punktId`).
- Wirkung: ein Kartenpunkt im Einsatz („Team Rex: Fund: Jacke“, orange `#E67E22`, Beschreibung mit Zeit und Genauigkeit,
  Koordinate auf 6 Nachkommastellen) und eine Notiz mit Quelle „Team-App“ und Koordinate. Im Monitor und in `/el` ein Hinweis mit
  Ton; die Einsatzkarte zeigt den Punkt sofort.
- Höchstens 50 Punkte je Team und Einsatz, danach `409` + `{ "error" }` (nicht `429` – das bedeutet „kurz warten“).
  Fehlende oder ungültige Felder, `zeit` ohne `Z` oder mehr als 5 min in der Zukunft → `400`.
  Kein laufender Einsatz bzw. `mode = stop` → `410` + `{ "mode": "stop" }` wie überall.
- Die Team-App sieht ihre eigenen Punkte nicht über den Server (keine Liste); sie merkt sich gesendete Punkte nur bis zum Abmelden.

**Lebensdauer, Löschfrist, Bericht** (Entscheidung 31.10.2026):

- Punkte leben wie alle Kartenpunkte bis zum Einsatzende; die Einsatzleitung kann sie löschen. Ziele bleiben als Mitteilung beim Team.
- Keine eigene Löschfrist. Die Notiz zum Team-Punkt fällt unter die Frist „Notizen, Statusmeldungen, Funk und Kartenpunkte“.
- Bericht: Beim Einsatzende wählt die Einsatzleitung „Kartenpunkte im Bericht“ (`/el/beenden` und EL-App `pdf.punkte`).
  Nur dann übernimmt der Server die Kartenpunkte ins Archiv; sie erscheinen im Bericht auf der Karte und als Liste und werden mit
  der Notizen-Frist gelöscht. Ohne Auswahl wird kein Punkt archiviert.

### Grenzen

- nginx: eigenes Limit für `/api/team-mobile/app/` (300 Anfragen/min je IP, Burst 120), weil sich im Mobilfunk viele Handys eine IP teilen.
  Die Anmeldung (`/login`, `/login/teams`) ist strenger begrenzt (20/min je IP, wie `/team/login`).
- Bei Überschreitung antwortet nginx mit **429**. Die App zeigt dann „Zu viele Versuche, bitte kurz warten“ und versucht es frühestens nach 30 s erneut.
- `/ereignisse` läuft über einen eigenen nginx-Block ohne Pufferung und mit langer Laufzeit.
- Die App sendet nur an den `server` aus der Übergabe und nur über HTTPS.

### 14. Packliste der Staffel (ab Server 1.16.0)

Eine Checkliste „Was brauche ich für den Einsatz?“. Die Staffel gibt eine Packliste vor, jedes Mitglied hakt sie **nur auf
seinem Gerät** ab und kann eigene Punkte dazunehmen. Die App erinnert in einstellbarem Abstand (Standard 14 Tage) mit einer
lokalen Mitteilung daran, die Liste durchzugehen.

**Nur die Staffel bearbeitet ihre Vorlage** (Einstellungen → Tracking-App, eine Zeile je Punkt, eine Zeile mit `:` am Ende
beginnt eine Gruppe). In der App sind Punkte der Staffel nicht änderbar und nicht löschbar, nur abhakbar. Eigene Punkte legt
das Mitglied in der App an, sie bleiben auf dem Gerät.

**Keine Rückmeldung an die Staffel** (Entscheidung 16.11.2026): Es gibt keinen Endpunkt, der Abhak-Stand, eigene Punkte oder
Erinnerungen entgegennimmt. Der Server erfährt nicht, wer die Liste geladen oder abgehakt hat.

`server` (Abschnitt 10, in `GET /config` und `POST /login/teams`; bei der Einsatzleitung in `POST /login` und `POST /verlaengern`)
enthält zusätzlich:

```json
"server": { "kennung": "…", "staffel": "RHS Musterstadt", "logoVersion": "…", "packlisteVersion": "9c1e2f7a0b3d4c55" }
```

- `packlisteVersion`: ändert sich genau dann, wenn die Staffel die Packliste ändert (Hash, 16 Zeichen); `null`, wenn keine
  hinterlegt ist. Die App lädt die Liste nur neu, wenn sich die Version gegenüber ihrer gespeicherten Fassung ändert.

`GET /packliste` (Team-Token) bzw. `GET /api/el/app/v1/packliste` (EL-Token), ungültiges Token → `410` + `stop`:

```json
{ "version": "9c1e2f7a0b3d4c55", "stand": "2026-11-16T20:00:00Z",
  "punkte": [ { "id": "3fa1c2d4", "text": "Stirnlampe mit Ersatzakku", "gruppe": "Persönlich" },
              { "id": "b07e9a11", "text": "Wasser und Napf", "gruppe": "Hund" } ] }
```

- `punkte` in der Reihenfolge der Staffel. `gruppe` ist `null` für Punkte vor der ersten Gruppe.
- `id`: aus Gruppe und Text abgeleitet (8 Zeichen). Bleibt gleich, solange der Punkt gleich lautet; die App merkt sich den
  Abhak-Stand je `id`. Ändert die Staffel den Text, gilt der Punkt als neu (nicht abgehakt).
- Höchstens 200 Punkte, Text höchstens 200 Zeichen, Gruppe höchstens 60 Zeichen. Längeres kürzt der Server.
- `404`, wenn keine Packliste hinterlegt ist.

**Nur die Heimatstaffel:** Die App übernimmt die Vorlage nur von dem Server, dessen `kennung` sie als Heimatstaffel gespeichert
hat (Abschnitt 10). Bei einem gemeinsamen Einsatz mit einer anderen Staffel bleibt die eigene Packliste. Die gespeicherte
Vorlage bleibt nach dem Abmelden auf dem Gerät, damit Liste und Erinnerung auch ohne Einsatz funktionieren.

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

### 4. `GET /packliste` (ab Server 1.16.0)

Wie in der Team-Rolle (Abschnitt 14), mit EL-Token unter `/api/el/app/v1/packliste`, nur intern. `packlisteVersion` steht im
Objekt `server` von `POST /login` und `POST /verlaengern`. Auch hier keine Rückmeldung an die Staffel.

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

**Anmeldung per QR-Code** (Idee 6.3): Der Server zeigt unter Einstellungen → System → „EL-App anmelden“ einen QR-Code mit
der internen Adresse, unter der die Einstellungen geöffnet sind: `https://<interne Adresse>/el` – **ohne Passwort und ohne Token**
(mit der Handy-Kamera geöffnet ist es einfach die EL-Seite). Nur bei HTTPS, sonst zeigt der Server statt des Codes einen Hinweis.
**Keine IP-Adresse** (ab Server 1.16.0): Sind die Einstellungen über eine IP-Adresse geöffnet (z. B. `https://10.10.0.1`), zeigt der
Server keinen Code, sondern den Hinweis, die Seite über den Namen zu öffnen – das Zertifikat gilt nur für den Namen, die App bekäme
sonst einen Zertifikatsfehler. Die App lehnt eine IP-Adresse im QR-Code und im Adressfeld der EL-Anmeldung mit einer klaren Meldung ab
(„Bitte den Namen der internen Adresse eintragen, nicht die IP-Adresse“), auch bei Codes älterer Server.
Die App erkennt im selben Scanner wie beim Team-Code eine `https://`-Adresse mit dem Pfad `/el` (Groß-/Kleinschreibung und
`/` am Ende egal, Abfrageteil wird ignoriert), übernimmt daraus nur den Server, zeigt den Servernamen zur Bestätigung und fragt das
Staffel-Passwort ab (`POST /login`). Ein Team-Code (`/team/login/…`) bleibt eine Team-Anmeldung. Keine neue Schnittstelle.

### 4a. Wer bin ich: Person und Rolle (ab Server 1.17.0)

Nach dem Passwort wählt die App aus den Stammdaten, wer sie bedient. Daraus folgt je Einsatz die Rolle (Entscheidung Paul 17.11.2026).
Es geht nicht um Schutz vor Missbrauch (wer das Staffel-Passwort hat, könnte sich jeden Namen geben), sondern darum, dass mehrere
EL-Handys im ELW sich nicht in die Quere kommen: nur die Einsatzleitung steuert und sendet ihren Standort.

**`GET /personen`** (EL-Token): aktive Einträge der Stammdaten, nach Vorname:
`{ "personen": [ { "personId": "…", "name": "Anna Berg", "einsatzleiter": true, "fuehrungsassistent": false } ] }`
(`einsatzleiter`/`fuehrungsassistent` = Qualifikation laut Stammdaten, nur als Hinweis bei der Auswahl).

**Feste Person am VPN-Gerät** (Einstellungen → System → VPN-Geräte, Spalte „Person (EL-App)“): Ist dem Gerät eine Person
zugeordnet, gilt sie immer – `POST /login` stellt das Token gleich mit dieser Person aus und antwortet zusätzlich mit
`"ich": { "personId", "name", "rolle", "fest": true }`; die App fragt dann nicht. Die Person des Geräts geht auch vor einer Person
im Token (z. B. Token einer älteren App).

**`POST /ich`** `{ "personId": "…" }` (nur ohne feste Person am Gerät): Person **einmal** wählen. Neues Token mit dieser Person
(gleicher Ablauf, gleiche Anmeldung, also keine Verlängerung); das alte ist danach gesperrt wie bei `/verlaengern`. Antwort
`{ "anmeldung": <wie /verlaengern>, "ich": { "personId", "name", "rolle" } }`. Unbekannte oder inaktive Person → `400`.
**Kein Wechsel** (Entscheidung Paul 17.11.2026): Steht schon eine Person im Token oder am Gerät → `409`. Eine andere Person geht
nur über Abmelden und neue Anmeldung mit dem Staffel-Passwort. Das Audit-Log nennt bei jeder Aktion Gerät und Person.
`/verlaengern` behält die Person.

**Rolle** (`ich.rolle`), bei jeder Anfrage neu aus dem laufenden Einsatz bestimmt (Vergleich mit dem Namen in den Einsatzdaten):

| Rolle | wann | darf |
|---|---|---|
| `einsatzleiter` | Person = Einsatzleiter des laufenden Einsatzes | alles, sendet ihren Standort |
| `fuehrungsassistent` | Person = Führungsassistent | alles, **sendet keinen Standort** |
| `lesen` | sonst, und immer ohne laufenden Einsatz | nur lesen (Lage, Karte, Funk, Vermisste, Tracks, Bericht) |

- Aktionen (Abschnitt 7) mit Rolle `lesen` → `403` + `{ "error" }`. Ausnahme `/einsatz/start`: ohne laufenden Einsatz darf jeder mit
  EL-Zugang anlegen; wer sich dabei als Einsatzleiter einträgt, hat danach die Rolle `einsatzleiter`.
- Standort: `GET /status` und `POST /positions` antworten bei `fuehrungsassistent` und `lesen` mit `mode = pause`; `POST /positions`
  verwirft die Punkte dann (`accepted = 0`). Die App sendet in diesen Rollen nichts.
- `GET /lage` enthält `"ich": { "personId": "…", "name": "Anna Berg", "rolle": "einsatzleiter" }`. Ändert sich der Einsatzleiter im
  Monitor, gilt die neue Rolle ab der nächsten Anfrage; die App lädt `/lage` bei jedem Ereignis ohnehin neu.
- **Ohne gewählte Person** (Apps vor 1.17.0, Token ohne Person, Gerät ohne feste Person): Rolle `einsatzleiter` wie bisher,
  `ich.personId = null`. Die App ab 1.17.0 fragt nach der Anmeldung nach der Person, bevor sie die Lage zeigt – außer `/login`
  brachte schon `ich` mit.
- Mit Rolle `lesen` blendet die App Steuerknöpfe aus (Start, Aktionen, Beenden, Funk schreiben, Vermisste ändern), statt sie nur zu sperren.
- Token-Format `e4.…` (mit Person); `e3` gilt weiter (ohne Person).

**Ein Handy, zwei Rollen:** Ist auf dem Handy zusätzlich ein Team angemeldet, sendet es seinen Standort nur als Team (die Ortung der
EL ruht), damit der Suchtrack nicht abreißt. Die App zeigt auf der Startseite beide Anmeldungen und oben immer, in welcher Rolle man ist.

### 5. `GET /lage`

```json
{
  "mode": "track", "serverZeit": "…Z",
  "einsatz": { "aktiv": true, "text": "Vermisste Person · Testwald", "stichwort": "…", "ort": "…", "szenario": "Flaeche",
               "uebung": false, "beginn": "…Z", "einsatzleiter": "…", "fuehrungsassistent": "…" },
  "teams": [ {
      "teamId": "…", "teamName": "Team Rex", "dogName": "Rex", "hundefuehrerName": "Anna", "helfer": ["Ben"], "einsatzart": "Flaeche",
      "suche": { "status": "laeuft", "seit": "…Z", "dauerSec": 2100 },
      "warnstufe": 0, "warnung1Min": 45, "warnung2Min": 60,
      "suchgebiet": { "id": "…", "name": "Gebiet A", "farbe": "#2f6fd6" },
      "halsband": { "id": "HB-7", "name": "Halsband 7", "zeit": "…Z", "akku": 3 },
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
| `GET /einsatz/ergebnisse` | erlaubte Ergebnisse für das Einsatzende: `[ { "wert": "Uebung abgeschlossen", "anzeige": "Übung abgeschlossen", "fund": false } ]` – `fund` ab Server 1.17.0 (Abschnitt 10) |
| `GET /einsatz/vorort` | Anwesenheit für das Einsatzende wie in `/el/beenden`: `{ "personal": [ { "id", "name", "vorausgewaehlt" } ], "hunde": [ { "id", "name", "vorausgewaehlt" } ] }` – nur aktive Einträge der Stammdaten, Personal nach Vorname, Hunde nach Name; `vorausgewaehlt` = in einem Team eingeteilt. Ab Server 1.14.1, ältere Server antworten `404` |
| `GET /einsatz/bericht/{archivId}` | Einsatzbericht eines beendeten Einsatzes als PDF (`application/pdf`, Dateiname `einsatzbericht-<Einsatznummer>.pdf`), Inhalt wie der Download im Archiv. Optional `?karte=Streets` (`Streets` · `Topographic` · `Satellite`, Unbekanntes = `Streets`), `teams=<teamId>,<teamId>` (ohne = alle), `tracks=0` (ohne Tracks), ab Server 1.17.0 `abschnitte=` (Abschnitt 10). `archivId` aus der Antwort von `/einsatz/beenden`. Unbekannt → `404` + `{ "error" }`, Fehler beim Erstellen `500` + `{ "error" }`. Abruf steht im Audit-Log. Ab Server 1.15.0, ältere Server antworten `404` |
| `GET /einsatz/vorlage` | Auswahllisten wie in `/el/start`: `{ "aktiv", "szenarien": ["Mantrailer", …], "divera": [ { "id", "titel", "ort", "lat", "lng", "zeit" } ], "einsatzleiter": ["Anna Berg"], "fuehrungsassistenten": […], "ergebnisse": […], "kartenarten": [ { "wert": "Streets", "anzeige": "Straßenkarte" } ], "alarmiertDurch": [ { "wert": "Polizei", "anzeige": "Polizei" } ] }` – `alarmiertDurch` ab Server 1.17.0 – `divera` leer, wenn Divera nicht eingerichtet oder nicht erreichbar ist |

### 7. Aktionen

Alle Aktionen sind `POST` mit JSON und tragen eine `id` (UUID der App, höchstens 64 Zeichen). Dieselbe `id` wird nur **einmal**
ausgeführt, eine Wiederholung antwortet `200` mit `{ "doppelt": true }` (wie `/statusmeldung`). So darf die App im Funkloch puffern
und wiederholen.

| Endpunkt | Körper (zusätzlich zu `id`) | Wirkung wie in `/el` |
|---|---|---|
| `/teams/{teamId}/start` | `halsbandId` (optional, `null` = ohne Halsband) | Suche starten; Halsband vergeben → `409` |
| `/teams/{teamId}/pause`, `/weiter`, `/stopp`, `/zuruecksetzen` | – | Kurzpause, fortsetzen, Suche beenden (Track wird gespeichert), Suchzeit zurücksetzen |
| `/teams/{teamId}/mitteilung` | `text` | Mitteilung an das Team (in der Team-App als `mitteilung`) |
| `/teams/{teamId}/suchgebiet` | `suchgebietId` (`id` aus `/karte` → `suchgebiete`; fehlt oder `null` = Zuweisung aufheben) | Suchgebiet dem Team zuweisen wie im Team-Dialog bzw. auf der Karte (Idee 6.4) |
| `/notizen` | `text`, `teamId` (optional) | Notiz der Einsatzleitung |
| `/notizen/{notizId}/loeschen` | – | Notiz löschen |
| `/funk` | `text`, `teamId` (optional), `antwortAuf` (optional) | Funkspruch oder Antwort |
| `/vermisste` | Profil wie bei `GET`, ohne `profilId` = neu; beim Ändern mit `version` | anlegen oder ändern (siehe unten) |
| `/vermisste/{profilId}/entfernen` | – | entfernen |
| `/vermisste/{profilId}/checkliste` | `punktId`, `erledigt` (bei `typ = jaNein`) bzw. `wert` (bei `auswahl` und `text`) | Checklistenpunkt setzen, ohne `version` |
| `/szenario` | `szenario` | Szenario des Einsatzes |
| `/einsatz/start` | `stichwort`, `ort`, `lat`/`lng` (optional), `szenario`, `uebung`, `einsatzleiter`, `fuehrungsassistent`, `diveraAlarmId` (optional); ab Server 1.17.0 `alarmiertDurch` und `melder` (Abschnitt 10) | wie `/el/start`; läuft schon ein Einsatz → `409` |
| `/einsatz/beenden` | `ergebnis` (`wert` aus `/einsatz/ergebnisse`), `ressourcenGeprueft` (muss `true` sein), `pdf`: `{ "tracks": true, "kartenart": "Streets", "teamIds": null, "punkte": false }` – `kartenart`: `Streets` · `Topographic` · `Satellite`, sonst `400`; optional `personalIds`, `hundeIds` (IDs aus `/einsatz/vorort`); ab Server 1.17.0 `abschluss`, `unvollstaendigBestaetigt`, `pdf.abschnitte` (Abschnitt 10) | wie `/el/beenden` |

Antwort jeder Aktion: `200` + `{ "doppelt": false, … }`, beim Anlegen mit der neuen Kennung des Objekts (z. B. `notizId`).

Umgesetzt (Stufe 3), im Einzelnen:

- Team-Aktionen antworten mit `suche` wie in `/lage`. Nicht erlaubte Übergänge → `409` (z. B. `pause`, obwohl keine Suche läuft;
  `zuruecksetzen` während die Suche läuft; Hund läuft schon in einem anderen Team). Unbekanntes Team → `404` + `{ "error" }`.
- `start` mit `halsbandId`: nur ein freies oder das dem Team schon zugewiesene Halsband, sonst `409`. Ohne `halsbandId` startet
  die Suche mit dem bereits zugewiesenen Halsband (falls vorhanden). `stopp` gibt das Halsband frei.
- `suchgebiet` (ab Server 1.15.0, ältere `404`) → `{ "doppelt": false, "suchgebiet": { "id", "name", "farbe" } }` bzw. `"suchgebiet": null`
  nach dem Aufheben. Ein Team hat höchstens ein Gebiet: Das bisherige wird frei. Gehört das Gebiet einem anderen Team, wechselt es wie
  im Monitor zu diesem Team (das andere Team hat dann keins) – die App fragt vorher nach. Unbekanntes Team oder Gebiet → `404` + `{ "error" }`.
  Danach kommt `team` für die betroffenen Teams; `/karte` (`suchgebiete[].teamId`) lädt die App nach der Antwort selbst neu.
  Die Team-App des Teams bekommt das neue Gebiet wie bisher über ihre `lage` (Team-Rolle Abschnitt 6).
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
  Ortszeit um. `pdf.tracks` Standard `true`, `pdf.punkte` (Kartenpunkte ins Archiv und in den Bericht, Team-Rolle Abschnitt 13) Standard `false`, `pdf.teamIds` `null` = alle Teams. Vor Ort waren die in
  `personalIds` und `hundeIds` gewählten Einträge (IDs aus `/einsatz/vorort`; unbekannte oder inaktive IDs fallen weg, `[]` = niemand).
  Fehlt ein Feld oder ist es `null`, übernimmt der Server dafür die Vorauswahl aus den eingeteilten Teams (wie in `/el/beenden`). Antwort `{ "archivId", "einsatzNummer",
  "pdf": { "erstellt": true, "fehler": null } }`; den Bericht gibt es danach im Archiv und als PDF über
  `GET /einsatz/bericht/{archivId}` (Abschnitt 6) – die App bietet nach dem Beenden „Bericht öffnen“ und „Teilen“ an und schickt dabei
  dieselbe `kartenart`, `teamIds` und `tracks` wie beim Beenden mit. Schon an einem anderen Leitungsplatz beendet
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

**Navigationsziele und Punkte (Variante C):**

- `GET /karte` liefert zusätzlich `punkte`: `[ { "id", "name", "beschreibung", "farbe", "lat", "lng", "zeit", "teamId" } ]` –
  alle Kartenpunkte des Einsatzes; `teamId` ist gesetzt, wenn ein Team den Punkt per App gesetzt hat, sonst `null`.
- Aktion `/teams/{teamId}/ziel` mit `punktId` (aus `punkte`): schickt den Punkt als Ziel an das Team (Team-Rolle Abschnitt 13).
  Antwort `{ "doppelt": false, "mitteilungId" }`. Unbekanntes Team oder unbekannter Punkt → `404`.
- Ereignis `punkt` `{ "id", "teamId", "aenderung", "text" }`: Kartenpunkt neu, geändert oder gelöscht → `/karte` neu laden.
  `aenderung`: `neu` · `geaendert` · `geloescht`. `text` nur bei `neu` von einem Team („Team Rex: Fund: Jacke“), sonst `null`.
  Setzt ein Team einen Punkt, kommt zusätzlich `notiz`. **Kein** `warnung` – der Ton gehört zur Tonart „Punkt vom Team“ (unten).

**Töne in den Apps (Einstellung auf dem Gerät):** Der Benutzer legt in den Einstellungen der App fest, welcher Ton zu welcher
Art von Meldung gehört (Auswahl aus den Tönen der App, „Nur Vibration“ oder „Aus“, mit Probehören). Der Server schickt dafür nur
eindeutige Ereignisse, keine Töne. Arten:

| Rolle | Tonart | ausgelöst durch |
|---|---|---|
| Team | Mitteilung | `mitteilung` ohne `ziel` |
| Team | Navigationsziel | `mitteilung` mit `ziel` |
| EL | Warnung Stufe 1 / Stufe 2 | `warnung` mit `stufe` 1 bzw. 2 |
| EL | Punkt vom Team | `punkt` mit `aenderung = neu` und `teamId` |
| EL | Funk | `funk` (neuer Eintrag eines Teams) |

Voreinstellung: jede Art ein eigener, unterscheidbarer Ton; Warnung Stufe 2 lässt sich nicht auf „Aus“
stellen (Entscheidung 31.10.2026). Die Einstellung gilt je Gerät, nicht je Server.

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

### 10. Einsatzbericht: Einsatzart und Abschlussangaben (ab Server 1.17.0)

Grundlage für den neuen Einsatzbericht. Ältere Server liefern die Felder nicht bzw. antworten `404`; die Apps blenden dann alles
dazu aus und beenden wie bisher.

**Alarmiert durch.** Pflichtfrage beim Anlegen (Monitor, `/el/start`, EL-App): `alarmiertDurch` = `Polizei` · `Feuerwehr` ·
`Rettungsdienst` · `AndereStaffel`, dazu optional `melder` als Freitext (Stelle oder Name, höchstens 200 Zeichen).

- `POST /einsatz/start` nimmt beide Felder. Die App fragt die Art immer ab, auch beim Start aus Divera: Der Divera-Melder wird
  nur als Freitext übernommen, die Art wählt die Einsatzleitung. Unbekannter Wert → `400`. Ohne Feld (Apps vor 1.17.0) startet
  der Server trotzdem; die Art fehlt dann und wird beim Einsatzende abgefragt.
- `/lage` (EL-Rolle) zeigt `einsatz.alarmiertDurch` (`null` = noch nicht gewählt), `/einsatz/vorlage` die Liste `alarmiertDurch`.

**Polizeiangaben** (Vorgangsnummer, Polizeikontakt) sind nur gefragt, wenn der Einsatz von der Polizei alarmiert wurde – bei
Übungen nie. Im Beenden-Dialog gibt es dazu „Keine Polizei beteiligt“ (`keinePolizei`): Dann gelten die Polizeiangaben als
erledigt, und der Bericht zeigt „keine Polizei beteiligt“ statt leerer Felder.

**Einsatzart.** `einsatzart` steht am Team in `/lage` (beide Rollen) und je Suche in `/tracks` → `abgeschlossen[]`:
`Flaeche` · `Truemmer` · `Mantrailer` · `Wasser` · `Lawine` oder `null` (nicht festgelegt).

- Festgelegt wird sie im Monitor-Teamdialog („Eingesetzt als“), Auswahl aus den Spezialisierungen des Hundes laut Stammdaten.
  Hat der Hund genau eine davon, ist sie vorausgewählt. In den Apps nur Anzeige auf der Teamkarte; ändern nur im Abschluss (unten).
- Beim Start jeder Suche speichert der Server die dann gültige Einsatzart an der Suche. Spätere Änderungen am Team ändern
  abgeschlossene Suchen nicht – außer im Abschluss: dort füllt die Einsatzart des Teams alle Suchen des Teams ohne Einsatzart.
- Änderung → `team` (EL-Rolle) bzw. `lage` (Team-Rolle).

**Trail** = Suche mit `einsatzart = Mantrailer`. Für Trails kennt der Bericht Start, Ende der Spur (letzter Punkt), Länge und Dauer
aus dem Track; Abbruchgrund und Geruchsartikel kommen aus dem Abschluss.

**`GET /einsatz/abschluss`** (EL-Rolle) – alles, was der Beenden-Dialog abfragt, mit den aktuellen Werten:

```json
{
  "uebung": false,
  "alarmiertDurch": "Polizei",
  "keinePolizei": false,
  "polizeiGefragt": true,
  "vorgangsnummer": "",
  "melder": "PI Dortmund-Nord",
  "polizei": { "name": "PHK Meier", "dienstnummer": "", "telefon": "0621 …" },
  "fund": { "zeit": null, "lat": null, "lng": null, "ort": "", "uebergabeAn": "", "teamId": null,
            "vorschlag": { "lat": 49.312, "lng": 8.431, "zeit": "…Z", "teamId": "…" } },
  "teams": [ { "teamId": "…", "teamName": "Team Rex", "einsatzart": null, "erlaubt": ["Flaeche", "Mantrailer"] } ],
  "trails": [ { "sucheId": "…", "teamId": "…", "teamName": "Team Rex", "beginn": "…Z", "ende": "…Z",
                "abbruchgrund": "", "geruchsartikel": "" } ],
  "abbruchgruende": ["Person gefunden", "Spur verloren", "Hund erschöpft", "Abbruch durch Einsatzleitung", "Gefahr im Gelände"],
  "fehlend": ["vorgangsnummer", "teams", "trails"]
}
```

- `melder` = Stelle oder Name zu „Alarmiert durch“ (Freitext, optional), `polizei` = Polizeikontakt wie in `/vermisste`.
- `polizeiGefragt`: `true`, wenn Vorgangsnummer und Polizeikontakt abgefragt werden (Einsatz, `alarmiertDurch = Polizei`,
  nicht `keinePolizei`). Die App blendet die Polizeifelder sonst aus oder zeigt sie als freiwillig.
- `fund.teamId`: Team, das gefunden hat („Gefunden durch“ im Bericht); der Vorschlag bringt es mit.
- `fund.vorschlag`: jüngster Kartenpunkt mit `typ = fund` (Team-Rolle Abschnitt 13, im Monitor ebenso setzbar), sonst `null`. Die App übernimmt ihn nur auf Wunsch.
- `abbruchgruende`: feste Liste für `trails[].abbruchgrund`, in den Server-Einstellungen anpassbar (oben die Vorgabe).
  Ein gespeicherter Grund bleibt, auch wenn er später aus der Liste entfernt wird.
- `teams[].erlaubt`: Spezialisierungen des Hundes, die als Einsatzart in Frage kommen; leer = alle fünf.
- `fehlend`: Angaben, die noch leer sind – `alarmiertDurch` (nur Einsätze), `vorgangsnummer` und `polizei` (Name leer; beide nur
  bei `polizeiGefragt`), `teams` (ein Team ohne Einsatzart), `trails` (ein Trail ohne Abbruchgrund oder Geruchsartikel). `fund` steht nie darin, weil es vom Ergebnis abhängt: Die App fragt
  den Fund ab, wenn das gewählte Ergebnis `fund: true` hat (neu in `/einsatz/ergebnisse`).
- Kein laufender Einsatz → `409`.

**`POST /einsatz/beenden`** bekommt zusätzlich, alles optional:

- `abschluss`: `{ "alarmiertDurch", "keinePolizei", "vorgangsnummer", "melder", "polizei": { "name", "dienstnummer", "telefon" }, "fund": { "zeit", "lat", "lng", "ort", "uebergabeAn", "teamId" },
  "teams": [ { "teamId", "einsatzart" } ], "trails": [ { "sucheId", "abbruchgrund", "geruchsartikel" } ] }`.
  Fehlende Felder bleiben, wie sie sind; Texte höchstens 200 Zeichen. `abbruchgrund` muss aus `abbruchgruende` sein. `fund.zeit` in UTC wie `endzeit`. Unbekannte `teamId`/`sucheId`
  oder eine `einsatzart` außerhalb der Liste → `400`. Der Server speichert die Angaben vor dem Beenden.
- `unvollstaendigBestaetigt`: Fehlt danach noch etwas (wie `fehlend`, dazu `fund` bei einem Ergebnis mit `fund: true`), antwortet
  der Server bei `false` mit `409` + `{ "error", "fehlend": [ … ] }` und beendet nicht. So bestätigt die Einsatzleitung ausdrücklich.
  Ab 1.17.0 schickt die App das Feld immer (erst `false`, nach der Bestätigung `true`); ohne das Feld (ältere Apps) beendet der Server wie bisher.
- `pdf.abschnitte`: Abschnitte des Berichts, `null` = alle: `vermisste` · `zeitleiste` · `teams` · `karten` · `tracks` · `funk` · `vorort`.
  Seite 1 (Kopf, Ergebnis, Polizei) ist immer dabei. `pdf.tracks = false` entspricht `tracks` nicht in der Liste (bleibt für ältere Apps).
  `GET /einsatz/bericht/{archivId}` nimmt dieselbe Liste als `?abschnitte=vermisste,teams,…`.

Nach dem Beenden ist der Einsatz nicht mehr änderbar, auch nicht diese Angaben. Flächenabdeckung steht in keinem Bericht.
