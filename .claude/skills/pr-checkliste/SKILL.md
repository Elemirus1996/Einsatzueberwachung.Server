---
name: pr-checkliste
description: Checkliste und Pflichtabschnitte für jeden Server-PR, der die App-Schnittstelle oder die App betrifft. Vor dem Erstellen eines PRs aufrufen.
user-invocable: true
---

# PR-Checkliste (Server)

## Vor dem PR prüfen

- [ ] Schnittstelle geändert? Dann ist `docs/APP_API.md` im selben oder einem vorherigen gemergten PR beschrieben.
- [ ] Zeiten in UTC mit `Z` (oder Unix-ms), keine Ortszeit ohne Zone.
- [ ] Ungültiges, abgelaufenes oder gesperrtes Token: Team `410` + `{"mode":"stop"}`, EL ebenso. Öffentlicher Zugang für `api/el/`: `404`.
- [ ] Nichts loggen, was Token oder Positionen enthält.
- [ ] Aktionen mit `id` sind entdoppelt, abgelehnte werden nicht gemerkt.
- [ ] Tests für jeden neuen Weg, auch Fehlerfälle (400/403/404/409/410).
- [ ] Neue Fremdbibliothek? In `THIRD_PARTY_LICENSES.md` eintragen und der Leitung melden.
- [ ] nginx geändert? Datei und nötige Schritte notieren.

## Pflichtabschnitte im PR-Text

```
## Inhalt
<was, warum, Issue>

## Auswirkung auf die App
<geänderte/neue Endpunkte, Felder, Statuscodes, Verhalten; was iOS und Android anpassen müssen; sonst „keine“>

## Einspielen
<nginx, Einstellungen, Datenmigration, Hinweise für Paul; sonst „nichts Besonderes“>

## Tests
<Anzahl, was neu abgedeckt ist>
```

Der Abschnitt „Auswirkung auf die App“ wird von der Mac-Sitzung direkt gelesen (Skill `api-abgleich`). Er muss ohne Rückfrage verständlich sein.
