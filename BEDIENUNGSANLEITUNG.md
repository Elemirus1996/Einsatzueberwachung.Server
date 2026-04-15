# Bedienungsanleitung — Einsatzüberwachung
### Für den Einsatz im ELW — auch ohne Computerkenntnisse

---

## 💻 Schritt 1: Programm öffnen

1. ELW-PC einschalten und warten, bis der Desktop erscheint.
2. Das Symbol **„Einsatzüberwachung"** auf dem Desktop **doppelklicken**.
   → Der Browser öffnet sich und zeigt automatisch `http://10.10.0.1`.
3. Die **Startseite** erscheint — das System ist sofort einsatzbereit.

> **Kein Symbol auf dem Desktop?**
> Browser öffnen (z. B. Chrome oder Edge) und in die Adresszeile oben eintippen:
> **`http://10.10.0.1`** → Enter drücken.

> **Seite lädt nicht?** Kurz warten (ca. 30 Sekunden), dann **F5** drücken.

---

## 🖥️ Desktop-Verknüpfung erstellen (einmalig, falls noch nicht vorhanden)

1. Rechtsklick auf eine freie Stelle des Desktops
2. **„Neu" → „Verknüpfung"** wählen
3. Als Adresse eingeben: `http://10.10.0.1`
4. **„Weiter"** → Name eingeben: **Einsatzüberwachung**
5. **„Fertig stellen"**

Das Symbol erscheint nun auf dem Desktop und öffnet das Programm mit einem Doppelklick.

---

## 🏠 Die Startseite — was bedeutet was?

Nach dem Öffnen sieht man das **Dashboard** mit einer Übersicht:

| Anzeige | Bedeutung |
|---|---|
| **Teams gesamt** | Alle angelegten Teams in dieser Sitzung |
| **Teams im Einsatz** | Teams, bei denen der Timer gerade läuft |
| **Kritische Teams** | Teams, die zu lange unterwegs sind (wird rot angezeigt) |
| **Einträge gesamt** | Alle Notizen und Funksprüche zusammen |
| **Aktueller Einsatz** | Wo, wann und wer — auf einen Blick |
| **Letzte Notizen** | Die zuletzt eingetragenen Notizen |
| **Letzte Funksprüche** | Die zuletzt eingetragenen Funksprüche |

Oben rechts steht:
- 🟢 **„Einsatz aktiv"** — läuft gerade ein Einsatz
- ⚫ **„Kein Einsatz"** — System wartet auf neuen Einsatz

### 🔴 Roter Banner ganz oben — was ist das?
Wenn **Divera 24/7** verbunden ist und ein Alarm eingeht, erscheint oben ein roter Alarm-Banner mit Titel, Ort und Uhrzeit.
- **„Einsatz anlegen"** klicken → Formular öffnet sich und ist bereits vorausgefüllt!
- **„Divera-Details"** klicken → öffnet die vollständige Divera-Ansicht

---

## 🚨 Neuen Einsatz anlegen

Im linken Menü auf **„Neuer Einsatz"** klicken (oder „Einsatz anlegen" auf der Startseite).

> ⚠️ **Läuft bereits ein Einsatz?** Eine gelbe Warnung erscheint. Erst den laufenden Einsatz beenden, dann einen neuen anlegen.

### Was ausfüllen?

**Linke Seite — Einsatzinformationen:**

| Feld | Pflicht? | Was eintragen |
|---|---|---|
| **Typ** | ✅ Ja | „Einsatz" oder „Übung" wählen |
| **Timer-Start** | — | Uhrzeit der Alarmierung (z. B. 14:32) |
| **Einsatznummer** | — | Nummer von der Leitstelle |
| **Einsatzort** | ✅ Ja | Straße und Ort (z. B. „Waldweg 5, Musterstadt") |
| **Stichwort** | — | Kurze Beschreibung (z. B. „Vermisstensuche") |
| **Karten-Adresse** | — | Ort für die Karte (ermöglicht automatische Zentrierung) |
| **Alarmiert durch** | — | Wer hat alarmiert (z. B. Polizei, Leitstelle) |
| **Bemerkungen** | — | Sonstige Hinweise (Zufahrt, Lage, etc.) |

**Rechte Seite — Führungspersonal:**

| Feld | Beschreibung |
|---|---|
| **Einsatzleiter** | Name aus der Liste wählen oder selbst eintippen |
| **Führungsassistent** | Optional, falls besetzt |

### Divera-Alarm direkt übernehmen
Falls Divera aktiv ist, erscheint unterhalb des Formulars ein Kasten mit aktuellen Alarmen.
→ Alarm **anklicken** → alle Felder werden automatisch ausgefüllt (Adresse, Uhrzeit, Stichwort).

### Einsatz starten
Auf den blauen Button **„Einsatz starten"** klicken.
→ Das Programm wechselt automatisch zum **Einsatz-Monitor**.

---

## 📊 Einsatz-Monitor — die Hauptseite im Einsatz

Im linken Menü: **„Monitor"**

Oben im Monitor sieht man: Einsatztyp, Ort, Startzeit, Gesamtdauer und (wenn ELW-Marker auf der Karte gesetzt) das aktuelle Wetter.

---

### Teams anlegen

1. Auf **„+ Team anlegen"** klicken (oben bei der Teams-Liste)
2. Felder ausfüllen:

| Feld | Was eintragen |
|---|---|
| **Teamname** | Pflichtfeld — z. B. „Team Maier" oder „Alpha-1" |
| **Hundeführer** | Name aus der Liste oder eintippen |
| **Hund** | Name des Einsatzhundes |
| **Helfer** | Begleitperson |
| **Suchgebiet** | Welches Gebiet sucht das Team |
| **Drohnenteam** | Häkchen setzen, wenn das Team eine Drohne hat |
| **Supportteam** | Häkchen setzen für Versorger / ELW-Besatzung (kein Timer) |

3. **„Speichern"** klicken

---

### Timer-Farben — was bedeuten sie?

| Farbe / Anzeige | Bedeutung | Was tun? |
|---|---|---|
| 🟢 **Grün** | Alles normal, Team ist unterwegs | Nichts, weiter beobachten |
| 🟡 **Orange / „Warnung"** | Team ist schon länger draußen | Aufmerksamkeit, ggf. Funkkontakt |
| 🔴 **Rot / „KRITISCH"** | Team überschreitet Zeitlimit | Sofort Funkkontakt herstellen! |
| **„IN PAUSE"** | Team macht Pflichtpause, Countdown läuft | Warten bis Pause endet |
| **„PAUSE OK"** | Pflichtpause ist abgeleistet | Team kann neu starten |

> Drohnenteams und Supportteams haben keinen Timer — dort steht „Kein Timer erforderlich".

---

### Team-Buttons im Monitor

| Button | Was passiert |
|---|---|
| **Start** (grün) | Timer startet — Team rückt aus |
| **Stopp** (gelb) | Timer hält an — Team kehrt zurück |
| **Reset** (grau) | Timer zurück auf 0 — für neue Runde oder neues Gebiet |
| **Bearbeiten** (blau) | Namen, Gebiet oder Personal des Teams ändern |

---

### Notizen und Funksprüche erfassen

Rechts im Monitor befindet sich der Bereich **„Notizen & Funksprüche"**.

So geht's:
1. **Team/Quelle** auswählen ← **Pflichtfeld!** (z. B. „Team Maier" oder „Einsatzleitung")
2. **Typ** wählen: „Notiz" (interner Vermerk) oder „Funk" (Funkspruch)
3. Text eingeben
4. **„Hinzufügen"** klicken → erscheint sofort mit Uhrzeit

**Schnell-Notizen:** Falls vordefinierte Buttons erscheinen (z. B. „Anmarsch", „Im Gebiet", „Rückkehr") → einmal klicken = automatischer Eintrag mit Uhrzeit.

**Antworten auf einen Eintrag:** Unterhalb jeden Eintrags gibt es ein kleines Antwortfeld — Team wählen, Text schreiben, „Antworten" klicken.

---

### Zweiter Monitor — Popout-Fenster

Beim Teams-Bereich und beim Notizen-Bereich gibt es oben je ein kleines **↗-Symbol**.
Darauf klicken → öffnet den Bereich in einem **separaten Fenster** (ideal für einen zweiten Bildschirm).

---

### Einsatz beenden

Roter Button **„Beenden"** oben rechts im Monitor klicken.

Ein Formular öffnet sich:

| Feld | Was eintragen |
|---|---|
| **Ergebnis** | z. B. „Person gefunden", „Person gefunden – verletzt", „Einsatz abgebrochen" |
| **Ende-Zeit** | Uhrzeit des Einsatzendes |
| **Bemerkungen** | Abschlussbemerkungen |

Eine Zusammenfassung (Teams, Personal, Notizen) wird nochmals angezeigt.

Nach Bestätigung:
- Einsatz wird automatisch **archiviert**
- **PDF-Bericht** wird erstellt und ist im Archiv abrufbar
- System ist sofort wieder bereit

---

## 🗺️ Karte

Im linken Menü: **„Karte"** (oder im Monitor auf den **„Karte"**-Button klicken)

| Was | Wie |
|---|---|
| **Adresse suchen** | Oben in der Suchzeile Adresse eintippen → Lupe klicken |
| **Kartentyp wechseln** | Oben rechts: Straße / Satellit / Hybrid (Karten-Icons) |
| **Suchgebiet zeichnen** | Links auf **„+"** klicken → Fläche auf der Karte einzeichnen → benennen → Farbe wählen |
| **Team einem Gebiet zuweisen** | Gebiet in der linken Liste anklicken → Team aus Dropdown wählen |
| **ELW-Position setzen** | Oben rechts **„ELW setzen"** klicken → auf der Karte den Standort anklicken |
| **Zusätzliche Marker** | Auf der Karte klicken und Marker-Typ wählen (Fundstelle, Treffpunkt, etc.) |
| **Karte drucken** | Oben rechts **Drucker-Symbol** klicken |

> **Wichtig:** ELW-Position setzen → aktiviert automatisch die **Wetteranzeige** im Monitor!

> **Tipp:** Wenn beim Einsatz anlegen unter „Karten-Adresse" eine Adresse eingegeben wurde, springt die Karte automatisch dorthin.

---

## ⚡ Divera 24/7

Im linken Menü: **„Divera 24/7"**

> Diese Seite funktioniert nur, wenn in den **Einstellungen** ein Divera API-Key eingetragen ist.

### Was zeigt die Seite?

**Linke Seite — Aktive Alarme:**
- Alle aktuell offenen Divera-Alarme mit Titel, Text, Adresse und Uhrzeit
- Bei Prioritäts-Alarm: ⚠️-Symbol
- **Rückmeldungen pro Alarm:**

| Anzeige | Bedeutung |
|---|---|
| 🟢 X – 30 Min | X Personen sind in 30 Min vor Ort |
| 🟡 X – 1 Std | X Personen sind in 1 Stunde vor Ort |
| 🔴 X nicht einsatzbereit | X Personen sind verhindert |
| ⚫ X keine Antwort | X Personen haben noch nicht geantwortet |

**Rechte Seite — Personal-Ampel:**
- Listet alle Mitglieder mit ihrem aktuellen Status: verfügbar / bedingt / nicht verfügbar

### Automatische Aktualisierung
Die Seite lädt selbständig im Hintergrund:
- **Kein Alarm aktiv:** alle 10 Minuten
- **Alarm aktiv:** jede Minute

Mit dem Button **„Aktualisieren"** kann jederzeit manuell neu geladen werden.

### Verbindungsanzeige (oben links)
- 🟢 **Verbunden** → Divera antwortet, alles ok
- 🔴 **Verbindungsfehler** → Fehlermeldung erscheint darunter; API-Key in Einstellungen prüfen

---

## ⛅ Wetter

Im linken Menü: **„Wetter"**

Zeigt aktuelle Wetterdaten vom **Deutschen Wetterdienst (DWD)**:
- Temperatur, Wind, Luftfeuchtigkeit, Luftdruck, Niederschlag
- **Flugwetter** (für Drohneneinsätze): wird als farbiges Badge angezeigt
- **DWD-Warnung:** wenn eine offizielle Unwetterwarnung vorliegt, erscheint ein farbiges Warn-Badge

**So Wetter abrufen:**
1. Ort in das Textfeld eingeben (z. B. „Musterstadt")
2. **„Wetter abrufen"** klicken

> **Wetter im Monitor automatisch:** Wenn auf der Karte ein **ELW-Marker gesetzt** ist, wird das Wetter für diesen Standort automatisch im Monitor-Header angezeigt — ohne manuelles Eingeben.

---

## 📂 Archiv

Im linken Menü: **„Archiv"**

Zeigt alle abgeschlossenen Einsätze als Liste:
- Einsatzort, Typ, Datum und Uhrzeit
- Einsatzdauer, Anzahl Teams, Personal, Notizen
- Button zum Öffnen des **PDF-Berichts** des jeweiligen Einsatzes

---

## 👥 Stammdaten (Personal, Hunde, Drohnen)

Im linken Menü: **„Stammdaten"**

Hier werden alle Einsatzkräfte, Hunde und Drohnen der Staffel hinterlegt.

**Tabs:**
- **Personal** — alle Personen
- **Hunde** — alle Suchhunde mit zugeordnetem Hundeführer
- **Drohnen** — alle Drohnen

**Wichtiges Feld beim Personal:**
„**Divera Benutzer-ID**" — die Nummer aus Divera (z. B. `681743`).
Wenn eingetragen, werden in der Divera-Ansicht die echten Namen statt internen IDs angezeigt.
→ Zu finden in der Divera-App unter „Profil" oder aus einer Alarm-Rückmeldung (dort als `#681743`).

**Daten exportieren/importieren:**
- **„Exportieren"** → lädt alle Stammdaten als Excel-Datei herunter
- **„Vorlage"** → leere Excel-Vorlage herunterladen
- **Datei hochladen** → ausgefüllte Excel-Datei importieren

---

## ⚙️ Einstellungen

Im linken Menü: **„Einstellungen"**

Hier werden einmalig die Grunddaten der Staffel und Divera eingerichtet. Diese Seite wird normalerweise nur vom Administrator bedient.

**Staffel-Informationen:** Name, Adresse, Telefon, E-Mail, Logo für PDF-Berichte.

**Darstellung:** Hell- oder Dunkelmodus (manuell oder automatisch nach Systemzeit).

**Timer-Warnungen:** Ab wie vielen Minuten ein Team „orange" oder „rot" wird (Standard: 45 / 60 Min).

**Sound-Warnungen:** Akustische Signale bei Timerwarnungen aktivieren und konfigurieren.

**Divera 24/7 einrichten:**
1. „Divera Integration aktivieren" → Häkchen setzen
2. API-Key eingeben (aus Divera: Verwaltung → Schnittstellen → API)
3. „Verbindung testen" → prüft ob alles korrekt ist
4. **„Speichern"** klicken

---

## ❗ Häufige Probleme — schnelle Hilfe

| Problem | Lösung |
|---|---|
| Seite lädt nicht / ist leer | **F5** drücken (Seite neu laden) |
| „Einsatz bereits aktiv"-Warnung | Zum Monitor → laufenden Einsatz erst beenden |
| Timer läuft nicht | Prüfen ob beim Team auf **„Start"** gedrückt wurde |
| Notiz kann nicht gespeichert werden | **Team/Quelle** muss ausgewählt sein — Pflichtfeld! |
| Divera zeigt „nicht konfiguriert" | Einstellungen → API-Key hinterlegen und aktivieren |
| Divera zeigt Namen als Nummer (#ID) | Divera Benutzer-ID in Stammdaten beim Personal eintragen |
| Roter Divera-Banner erscheint nicht | Kurz warten (Polling läuft im Hintergrund) oder Seite neu laden |
| Wetter wird nicht angezeigt | ELW-Position auf der Karte setzen |
| Alles weg / Fehlermeldung | Administrator informieren |

---

## ⌨️ Tastenkürzel

| Taste | Funktion |
|---|---|
| **F5** | Seite neu laden |
| **Strg + H** | Zur Startseite |
| **Strg + M** | Zur Karte |
| **Strg + T** | Neues Team anlegen |
| **Strg + N** | Neue Notiz |

---

## 🔄 Typischer Ablauf — Schritt für Schritt

```
1️⃣  Divera-Alarm geht ein
       ↓
2️⃣  Roter Banner auf der Startseite erscheint
       ↓
3️⃣  "Einsatz anlegen" klicken → Formular öffnet sich
       ↓
4️⃣  Alarm in der Divera-Liste anklicken → Felder werden automatisch befüllt
       ↓
5️⃣  "Einsatz starten" klicken → Monitor öffnet sich
       ↓
6️⃣  "+ Team anlegen" für jedes Team → Start-Button drücken
       ↓
7️⃣  Funksprüche und Notizen laufend erfassen
       ↓
8️⃣  Karte öffnen → Suchgebiete einzeichnen → Teams zuweisen
       ↓
9️⃣  ELW-Marker setzen → Wetter wird automatisch angezeigt
       ↓
🔟  Roter "Beenden"-Button → Ergebnis eingeben → Einsatz archiviert
```

> **Merksatz für den Einsatz:**
> **Alarm → Einsatz anlegen → Teams starten → Funk & Notizen → Beenden → fertig**
