---
name: tests-kurz
description: Build und Tests mit knapper Ausgabe ausführen, um Kontext zu sparen. Immer statt der normalen Build-/Test-Befehle verwenden.
user-invocable: true
---

# Build und Tests mit kurzer Ausgabe

Lange Build- und Testausgaben kommen nie vollständig in den Kontext. Ausgabe in eine Datei schreiben, nur Zusammenfassung und Fehler lesen.

## Server (.NET)

Nur das Testprojekt bauen, wie im CI. Die Solution enthält `Einsatzueberwachung.LiveTracking` (net9.0-windows) und scheitert unter macOS und Linux.

PowerShell:
```
dotnet test src/Einsatzueberwachung.Tests --verbosity quiet --nologo *> $env:TEMP\einsatzspur-server.log; "Exit $LASTEXITCODE"
Select-String -Path $env:TEMP\einsatzspur-server.log -Pattern "Failed|Passed!|Failed!|error" | Select-Object -Last 30
```

Bash:
```
dotnet test src/Einsatzueberwachung.Tests --verbosity quiet --nologo > /tmp/einsatzspur-server.log 2>&1; echo "Exit $?"
grep -E "Failed|Passed!|Failed!|error" /tmp/einsatzspur-server.log | tail -30
```

Logdateien unter Windows: `%TEMP%\einsatzspur-*.log`.

## Regeln

- Bei Erfolg reicht eine Zeile: „<n>/<n> grün“.
- Bei Fehlern nur die fehlschlagenden Tests bzw. Fehlerzeilen lesen (`grep`, höchstens 40 Zeilen), nicht die ganze Datei.
- Logdateien liegen unter `/tmp/einsatzspur-*.log` und gehören nicht ins Repo.
