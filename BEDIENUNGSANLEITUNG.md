# 📋 Bedienungsanleitung — Einsatzüberwachung (ELW)
### Für Personen ohne technische Vorkenntnisse

---

## 🚀 1. Programm starten

1. ELW-PC einschalten und warten, bis der Desktop erscheint.
2. Auf dem Desktop das Symbol **„Einsatzüberwachung"** doppelklicken.
   → Der Browser öffnet sich automatisch (z. B. `http://10.0.0.1`).
3. Es erscheint die **Startseite (Dashboard)** — das Programm ist sofort bereit.

> ⚠️ Falls der Browser eine Fehlermeldung zeigt: kurz warten (ca. 30 Sekunden) und **F5** drücken.

---

## 🏠 2. Startseite (Dashboard)

Die Startseite zeigt immer eine **Übersicht des aktuellen Standes**:

| Bereich | Inhalt |
|---|---|
| **Teams gesamt** | Wie viele Teams insgesamt angelegt sind |
| **Teams im Einsatz** | Wie viele Teams aktuell laufen (grüner Timer) |
| **Kritische Teams** | Teams, die zu lange unterwegs sind (rot, mit Zahl) |
| **Einträge gesamt** | Summe aller Notizen und Funksprüche |
| **Aktueller Einsatz** | Typ, Einsatzort, Alarmzeit, Einsatzleiter, Führungsassistent |
| **Letzte Notizen** | Die 3 zuletzt erfassten Notizen |
| **Letzte Funksprüche** | Die 3 zuletzt erfassten Funksprüche |
| **Server-Status** | Ob der Server erreichbar ist; „Status aktualisieren"-Button |

Oben rechts erscheint ein Badge:
- 🟢 **„Einsatz aktiv"** — ein Einsatz läuft gerade
- ⚫ **„Kein Einsatz"** — System wartet auf neuen Einsatz

### ⚡ Divera-Alarm-Banner
Wenn Divera 24/7 konfiguriert ist und ein aktiver Alarm vorliegt, erscheint **ganz oben** ein **roter Banner**:
- Zeigt Alarmtitel, Einsatzort und Uhrzeit
- Schaltfläche **„Einsatz anlegen"** → springt direkt zum Formular (bereits vorausgefüllt!)
- Schaltfläche **„Divera-Details"** → öffnet die Divera-Statusseite

---

## 🚨 3. Neuen Einsatz anlegen

**Linkes Menü → „Neuer Einsatz"** (oder „Einsatz anlegen" auf der Startseite)

### Hinweis: Einsatz bereits aktiv
Falls ein Einsatz noch läuft, erscheint eine gelbe Warnung mit einem Link zum laufenden Monitor. Zuerst den alten Einsatz beenden, dann einen neuen anlegen.

---

### Linke Karte: „Einsatzinformationen"

| Feld | Pflicht | Was eintragen | Beispiel |
|---|---|---|---|
| **Typ** | ✅ | Auswahl: *Einsatz* oder *Übung* | Einsatz |
| **Timer-Start (Alarmierung)** | — | Uhrzeit der Alarmierung (Datum wird automatisch gesetzt) | 14:32 |
| **Einsatznummer** | — | Nummer von der Leitstelle | 2024-042 |
| **Einsatzort** | ✅ | Straße, Ort oder GPS-Koordinaten | Waldweg 5, Musterstadt |
| **Stichwort** | — | Kurzbezeichnung des Einsatzes | Vermisstensuche |
| **Karten-Adresse** | — | Adresse für die automatische Karten-Suche | Musterstadt Innenstadt |
| **Alarmiert durch (Melder)** | — | Wer hat alarmiert | Polizei, Leitstelle, Angehörige |
| **Staffel-Name** | — | Name der eigenen Staffel | RHS Musterstadt |
| **Anzahl Teams (geplant)** | — | Geplante Teamanzahl | 5 |
| **Bemerkungen** | — | Lagebeschreibung, Anfahrt, sonstige Hinweise | Waldgebiet, Zufahrt über B3 |

### Rechte Karte: „Führungspersonal"

Jedes Feld bietet **zwei Eingabewege**:
1. **Dropdown**: aus der Stammdaten-Liste wählen (empfohlen)
2. **Freitextfeld**: Namen direkt eintippen (wenn Person nicht in Stammdaten)

| Feld | Beschreibung |
|---|---|
| **Einsatzleiter** | Verantwortliche Führungskraft |
| **Führungsassistent** | Unterstützung der Einsatzleitung (optional) |

### Divera 24/7 — Alarm-Import (nur wenn konfiguriert)

Wenn Divera aktiviert ist, erscheint unterhalb des Formulars die Karte **„Aus Divera 24/7 importieren"**:

- Aktive Alarme werden automatisch aufgelistet
- Jeder Alarm zeigt: Titel, Text, Adresse, Uhrzeit, Prioritäts-Badge (⚠️ PRIO), GPS-Verfügbarkeit und Rückmeldungen (wie viele kommen in 30 Min / 1 Std / nicht einsatzbereit)
- **Alarm anklicken** → alle Felder im Formular werden automatisch ausgefüllt (Titel → Stichwort, Adresse → Einsatzort & Karten-Adresse, GPS → Kartenposition, Uhrzeit → Timer-Start)
- **„Alarme aktualisieren"**-Button → lädt frische Daten von Divera

### ✅ Einsatz starten

Schaltfläche **„Einsatz starten"** (blauer Button, rechte Karte) klicken.
→ Das System wechselt automatisch zum **Einsatz-Monitor**.

---

## 📊 4. Einsatz-Monitor

**Linkes Menü → „Monitor"**

Hauptseite während eines laufenden Einsatzes.

**Header-Bereich** zeigt:
- Einsatztyp und Ort
- Datum und Uhrzeit des Einsatzbeginns
- Gesamte Einsatzdauer
- Aktuelles **Wetter** (Temperatur, Wind, Niederschlag, DWD-Warnung) — wird automatisch angezeigt, wenn die ELW-Position auf der Karte gesetzt ist

---

### 4a. Team anlegen

1. **„+ Team anlegen"** klicken (oben rechts der Teams-Karte)
2. Felder ausfüllen:

| Feld | Was eintragen |
|---|---|
| **Teamname** | Pflichtfeld, z. B. *Team Maier*, *Alpha-1* |
| **Hundeführer** | Name aus der Liste oder Freitext |
| **Hund** | Name des Einsatzhundes |
| **Helfer** | Begleitperson |
| **Suchgebiet** | Dem Team zugewiesenes Suchgebiet |
| **Drohnenteam** | Checkbox: Team nutzt eine Drohne |
| **Supportteam** | Checkbox: kein Timer erforderlich (Versorger, ELW-Besatzung) |

3. **„Speichern"** klicken

---

### 4b. Timer-Farben verstehen

| Farbe / Badge | Bedeutung |
|---|---|
| 🟢 **Grün** | Team läuft normal, alles ok |
| 🟡 **Orange / „Warnung"** | Team ist bereits länger unterwegs — Aufmerksamkeit empfohlen |
| 🔴 **Rot / „KRITISCH"** | Team ist zu lange unterwegs — sofort Funkkontakt herstellen! |
| 🔵 **„IN PAUSE"** | Team macht Pflichtpause, Countdown läuft |
| ✅ **„PAUSE OK"** | Pflichtpause ist abgeleistet |

Bei **Drohnenteams** und **Supportteams** läuft kein Timer (dort steht „Kein Timer erforderlich").

---

### 4c. Team-Buttons

| Button | Funktion |
|---|---|
| **Start** (grün) | Timer starten (Team rückt aus) |
| **Stopp** (gelb) | Timer anhalten (Team kehrt zurück) |
| **Reset** (grau) | Timer auf 0 zurücksetzen (neues Gebiet, neue Runde) |
| **Bearbeiten** (blau) | Teamname, Suchgebiet, Personal ändern |

---

### 4d. Notizen & Funksprüche erfassen

Rechte Spalte im Monitor: **„Notizen & Funksprüche"**

**Neuen Eintrag hinzufügen:**

| Feld | Beschreibung |
|---|---|
| **Team/Quelle** ✅ Pflicht | Das Team oder „Einsatzleitung" als Absender wählen |
| **Typ** | *Notiz* (interne Vermerke) oder *Funk* (Funksprüche) |
| **Text** | Inhalt des Eintrags |

Dann **„Hinzufügen"** klicken → Eintrag erscheint sofort mit Zeitstempel.

**Schnell-Notizen** (wenn konfiguriert): Vordefinierte Buttons wie „Anmarsch", „Im Gebiet", „Rückkehr" → einmal klicken = automatischer Eintrag mit Zeitstempel.

**Auf Eintrag antworten:**
Unter jedem Eintrag gibt es ein kleines Antwortfeld. Team/Quelle auswählen, Text eingeben, **„Antworten"** klicken → wird als eingerückter Thread gespeichert.

**Historie eines Eintrags:**
**„Historie"**-Button klickt man zum Anzeigen aller nachträglichen Änderungen (wer hat wann was geändert).

---

### 4e. Popout-Fenster (für Zweiten Monitor)

- Beim Teams-Bereich oben: kleines **„↗"**-Icon → öffnet Teams als separates Fenster
- Beim Notizen-Bereich oben: kleines **„↗"**-Icon → öffnet Notizen als separates Fenster

Ideal für Zwei-Monitor-Setups (z. B. Notizen auf Monitor 2).

---

### 4f. Einsatz beenden

Roter Button **„Beenden"** oben rechts.

Es öffnet sich ein Bestätigungsformular:

| Feld | Beschreibung |
|---|---|
| **Ergebnis** | z. B. „Person gefunden", „Person gefunden – verletzt", „Einsatz abgebrochen" |
| **Ende-Zeit** | Uhrzeit des Einsatzendes |
| **Bemerkungen** | Abschlusshinweise |

Eine **Zusammenfassung** zeigt nochmals: Anzahl Teams, Personal, Hunde, Notizen/Funk.

Nach Bestätigung:
- Einsatz wird **archiviert**
- **PDF-Bericht** wird automatisch erstellt
- System ist sofort bereit für den nächsten Einsatz

---

## 🗺️ 5. Karte

**Linkes Menü → „Karte"** (oder im Monitor auf **„Karte"** klicken)

| Funktion | Beschreibung |
|---|---|
| **Suchgebiete zeichnen** | Als Polygone (Flächen) einzeichnen und benennen |
| **Teams zuweisen** | Gebiete direkt einem Team zuordnen |
| **ELW-Marker setzen** | Position der Einsatzleitung → aktiviert automatische Wetteranzeige im Monitor |
| **Suchgebiet-Marker** | Zusätzliche Punkte (Fundstellen, Treffpunkte) markieren |
| **Farben** | Gebiete können farblich unterschieden werden |
| **Druckansicht** | Karte für den Ausdruck aufbereiten |

> 💡 Die Karte funktioniert am besten, wenn bei „Einsatz anlegen" unter **Karten-Adresse** eine Adresse eingegeben wurde (automatische Zentrierung).

---

## ⚡ 6. Divera 24/7

**Linkes Menü → „Divera 24/7"**

> Diese Seite ist nur verfügbar, wenn in den **Einstellungen** ein Divera API-Key hinterlegt ist.

---

### 6a. Was zeigt die Seite?

**Linke Hälfte: Aktive Alarme**

Alle aktuell offenen Divera-Alarme mit:
- **Alarmtitel** (mit ⚠️-Icon bei Prio-Alarm)
- **Alarmtext** (Zusatzinfo aus Divera)
- **Adresse** des Einsatzortes
- **Zeitstempel** (Datum und Uhrzeit der Alarmierung)
- **Rückmelde-Badges**:

| Badge | Bedeutung |
|---|---|
| 🟢 X geantwortet | X Personen haben generell geantwortet |
| 🟢 X 30 Min | X Personen sind in 30 Minuten vor Ort |
| 🟡 X 1 Std | X Personen sind in 1 Stunde vor Ort |
| 🔴 X nicht einsatzbereit | X Personen sind verhindert |
| ⚫ X keine Antwort | X Personen haben noch nicht geantwortet |

---

**Rechte Hälfte: Personal-Ampel**

Zeigt den aktuellen Verfügbarkeitsstatus aller Mitglieder:

| Badge | Bedeutung |
|---|---|
| 🟢 **verfügbar** | Grüner Status in Divera |
| 🟡 **bedingt** | Gelber Status (eingeschränkt verfügbar) |
| 🔴 **nicht** | Roter Status (nicht verfügbar) |

Die Tabelle listet alle Mitglieder mit ihrem Status (z. B. „30 Minuten", „1 Stunde", „Nicht einsatzbereit", „Kommt").

> Wenn die Mitgliederliste mit dem verwendeten API-Key nicht abrufbar ist, werden stattdessen die Rückmeldungen des aktuellen Alarms angezeigt.

---

### 6b. Polling-Anzeige (oben rechts auf der Seite)

Die Seite aktualisiert sich **automatisch im Hintergrund**:

| Situation | Aktualisierungsintervall |
|---|---|
| Kein Alarm aktiv | Alle **10 Minuten** (Standard) |
| Alarm aktiv | Alle **60 Sekunden** (Standard) |

Neben der Uhrzeit des letzten Updates steht das aktuelle Intervall. Mit **„Aktualisieren"** kann manuell sofort neu geladen werden.

---

### 6c. Verbindungsstatus

| Badge | Bedeutung |
|---|---|
| 🟢 **Verbunden** | Divera API ist erreichbar, Daten werden geladen |
| 🔴 **Verbindungsfehler** | Keine Verbindung — Fehlermeldung erscheint darunter |

---

## 📂 7. Einsatz-Archiv

**Linkes Menü → „Archiv"**

Zeigt alle abgeschlossenen Einsätze als Liste. Pro Einsatz:
- Einsatzort, Typ, Datum, Dauer
- Anzahl Teams, Personal, Notizen
- Schaltfläche zum Öffnen des gespeicherten PDF-Berichts

---

## ⛅ 8. Wetter

**Linkes Menü → „Wetter"**

Zeigt aktuelle Wetterdaten vom Deutschen Wetterdienst (DWD):
- Temperatur
- Windgeschwindigkeit
- Niederschlag (mm/h)
- **DWD-Warnungsstufe** (wird als farbiges Badge angezeigt, wenn eine aktive Warnung vorliegt)

Das Wetter wird für den **Standort der ELW** abgerufen. Dazu muss in der Karte ein ELW-Marker gesetzt sein.

---

## 👥 9. Stammdaten

**Linkes Menü → „Stammdaten"**

Verwaltet das gesamte Personal, die Hunde und Drohnen der Staffel.

### Tabs:
- **Personal** — alle Einsatzkräfte
- **Hunde** — alle Suchhunde mit Hundeführer-Zuordnung
- **Drohnen** — alle verfügbaren Drohnen

### Personal — wichtige Felder:

| Feld | Beschreibung |
|---|---|
| Vorname / Nachname | Name der Person |
| Qualifikationen | z. B. Hundeführer, Teamführer, Sanitäter |
| **Divera Benutzer-ID** | Numerische ID aus Divera (z. B. `681743`) — verknüpft die Person mit Divera-Rückmeldungen |

**Divera-ID eintragen:** In der Divera-App unter „Profil" oder aus einer Alarm-Rückmeldung (dort als `#681743` angezeigt). Wenn hinterlegt, werden Rückmeldungen in der Divera-Ansicht mit dem echten Namen statt der internen ID angezeigt.

### Export / Import:
- **Excel-Export** → alle Stammdaten als `.xlsx` Datei herunterladen
- **Vorlage herunterladen** → leere Excel-Vorlage für Massenimport
- **Datei hochladen** → vorbereitete Excel-Datei importieren (ersetzt alle bestehenden Daten!)

---

## ⚙️ 10. Einstellungen

**Linkes Menü → „Einstellungen"**

### Staffel-Informationen

| Feld | Beschreibung |
|---|---|
| **Staffel-Name** | Name der Staffel (erscheint im PDF-Bericht) |
| **Adresse** | Standortadresse der Staffel |
| **Telefon / E-Mail** | Kontaktdaten |
| **Logo** | Staffel-Logo hochladen (Datei-Upload, für PDF-Export) |

---

### Divera 24/7 — Einrichtung

1. **„Divera 24/7 Integration aktivieren"** — Checkbox aktivieren
2. **API-Key eingeben**:
   - In Divera öffnen: **Verwaltung → Schnittstellen → API-Access-Key**
   - Key kopieren und hier einfügen
   - 👁️-Button: Key sichtbar/verbergen umschalten
3. **API-URL**: Standardwert `https://app.divera247.com/api/v2` — nur ändern, wenn euer Anbieter eine andere URL nennt
4. **Poll-Intervall (kein Alarm)**: Wie oft (in Sekunden) Divera im Ruhezustand abgefragt wird (Standard: 600 = 10 Min)
5. **Poll-Intervall (Alarm aktiv)**: Wie oft bei aktivem Alarm abgefragt wird (Standard: 60 = 1 Min)
6. **„Verbindung testen"** — prüft ob der API-Key korrekt ist:
   - ✅ „Verbindung erfolgreich!" → alles ok
   - ❌ „Verbindung fehlgeschlagen" → API-Key prüfen oder Internetverbindung prüfen
7. **„Speichern"** nicht vergessen!

---

## 🔑 Divera API-Key finden

**In der Divera 24/7 App / Webseite:**
1. Als Administrator einloggen
2. Oben rechts: Organisations-Menü → **Verwaltung**
3. Links: **Schnittstellen** → **API**
4. Dort findet sich der **Access-Key** (lange Zeichenkette)
5. Key kopieren und in den Einstellungen der Einsatzüberwachung einfügen

---

## ❗ 11. Häufige Probleme und Lösungen

| Problem | Lösung |
|---|---|
| Seite lädt nicht / leer | **F5** drücken zum Neu-Laden |
| „Einsatz bereits aktiv" | Zum Monitor → laufenden Einsatz zuerst beenden |
| Timer läuft nicht | Prüfen ob **„Start"** beim Team gedrückt wurde |
| Notiz lässt sich nicht speichern | **Team/Quelle** muss ausgewählt sein (Pflichtfeld!) |
| Divera zeigt „nicht konfiguriert" | Einstellungen → API-Key hinterlegen und aktivieren |
| Divera-Verbindungsfehler | API-Key prüfen, Internetverbindung prüfen, „Verbindung testen" |
| Divera-Namen als #ID angezeigt | Divera-Benutzer-ID in Stammdaten beim jeweiligen Personal eintragen |
| Kein Alarm-Banner auf Startseite | Divera-Polling läuft im Hintergrund — kurz warten oder Seite neu laden |
| Wetter wird nicht angezeigt | ELW-Position auf der Karte als Marker setzen |
| Alles weg / Fehlermeldung | IT/Administrator informieren |

---

## ⌨️ 12. Tastenkürzel

| Taste | Funktion |
|---|---|
| **F5** | Seite neu laden |
| **Strg + H** | Startseite |
| **Strg + M** | Karte |
| **Strg + T** | Neues Team anlegen |
| **Strg + N** | Neue Notiz |

---

## 🔄 13. Typischer Ablauf mit Divera

```
📱 Divera-Alarm geht ein
        ↓
🟥 Roter Banner auf der Startseite erscheint
        ↓
🔗 "Einsatz anlegen" klicken → Formular öffnet sich
        ↓
📋 Alarm in der Divera-Importliste anklicken → Felder werden befüllt
        ↓
✅ "Einsatz starten" → Monitor öffnet sich
        ↓
👥 Teams anlegen → Start-Button pro Team
        ↓
📻 Funksprüche und Notizen erfassen
        ↓
🔴 "Beenden" → Einsatz archivieren, PDF erstellt
```

> 📌 **Merksatz:**
> **Divera-Alarm → Einsatz anlegen (auto-befüllt) → Teams starten → Notizen erfassen → Beenden → Archiv**
