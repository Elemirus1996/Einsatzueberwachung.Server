---
name: einweisung
description: Einarbeitung einer neuen Windows-Sitzung (Server) für die App-Schnittstelle von EinsatzSpur. Zu Beginn jeder neuen Sitzung oder nach einer Zusammenfassung des Kontexts aufrufen.
user-invocable: true
---

# Einweisung Windows-Sitzung (Server)

Du baust den Server Einsatzüberwachung (Blazor Server, .NET 9) und dort die Schnittstelle für die App **EinsatzSpur**: `api/team-mobile/app/v1/` und `api/el/app/v1/` (nur VPN). Die App bauen eine Mac-Sitzung (iOS und Android). Die Leitung ist die Cloud-Sitzung `session_01VCvMn7ETbiKTdBsnopQpzx`.

## Schritte

1. Sitzung im Server-Repo auf `main`: `git status`, `git pull --ff-only`.
2. `docs/APP_API.md` lesen. Das ist die verbindliche Schnittstelle.
3. Die Regeln der App lesen: `CLAUDE.md` im Repo `Elemirus1996/Einsatzueberwachung-App`, mit `gh api repos/Elemirus1996/Einsatzueberwachung-App/contents/CLAUDE.md --jq .content | base64 -d`.
4. Board lesen: https://claude.ai/artifact/EDiazzj2cArneXUVzCHb8K, Sammlung `aufgaben`. Zuerst die Karten `server-1b` und `leitung-uebergabe`, dann alle Karten mit `zustaendig` = Windows, die nicht `fertig` sind.
5. Offene PRs, Issues mit „App:“ im Titel: `gh pr list`, `gh issue list --search "App: in:title"`.
6. Kurzmeldung an die Leitung mit Skill `meldung`.

## Arbeitsweise

- Schnittstelle immer zuerst in `docs/APP_API.md` beschreiben, mergen, der Leitung melden, dann umsetzen.
- Jeder PR nach Skill `pr-checkliste`. Mergen nach grüner CI ohne Rückfrage, dann der nächste Schritt.
- Releases veröffentlichen nur nach Pauls direktem OK in dieser Sitzung. Einspielen auf dem Staffel-Server macht Paul selbst.
- Fragen und Entscheidungen gehen an die Leitung, nicht an Paul.
