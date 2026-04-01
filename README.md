# Einsatzueberwachung.Server

Linux-faehiger Nachbau der Anwendung Einsatzueberwachung.Web mit separater Mobile-PWA.

## Projektstruktur

- `src/Einsatzueberwachung.Server`: Hauptanwendung (Blazor Server)
- `src/Einsatzueberwachung.Domain`: Domain-Modelle und Services
- `src/Einsatzueberwachung.Mobile`: Mobile PWA mit 4 Kernfunktionen
- `deploy`: Linux-Deployment (Nginx, systemd, WireGuard, Setup-Skript)

## Deployment-Bausteine

- Nginx Reverse Proxy: `deploy/nginx/einsatzueberwachung.conf`
- systemd Dienste: `deploy/systemd/einsatzueberwachung-*.service`
- systemd Timer: Backup und Healthcheck in `deploy/systemd`
- Skripte: `deploy/scripts/backup.sh`, `deploy/scripts/health-check.sh`, `deploy/scripts/apply-update.sh`

## Laufzeitdaten unter Linux

- Persistente Daten: `/opt/einsatzueberwachung/data`
- Berichte/PDFs: `/opt/einsatzueberwachung/data/berichte`
- Konfiguration ueber Env Vars: `EINSATZUEBERWACHUNG_DATA_DIR`, `EINSATZUEBERWACHUNG_REPORT_DIR`, `EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD`

## Updater

- Startseite zeigt den Update-Status (installiert/verfuegbar, letzter Check, letzte Installation)
- Auto-Check alle 6 Stunden (abschaltbar ueber `AppSettings.AutoCheckUpdates`)
- Manuelle Trigger ueber UI oder API:
	- `POST /api/update/check`
	- `POST /api/update/install`
	- `GET /api/update/status`

## Mobile Funktionen

- Einsatz anlegen
- Team-Uebersicht
- Notizen
- Funksprueche

## Lokaler Start (Development)

```bash
dotnet build Einsatzueberwachung.Server.sln
dotnet run --project src/Einsatzueberwachung.Server
dotnet run --project src/Einsatzueberwachung.Mobile
```

## Deployment

Siehe `DEPLOYMENT.md`.

Fuer die End-to-End-Abnahme (Desktop + Mobile ueber `/mobile`) siehe in `DEPLOYMENT.md` den Abschnitt **10. Abnahme: Desktop + Mobile (`/mobile`)**.
