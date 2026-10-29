# Third-Party Licenses

Dieses Dokument listet alle Drittanbieter-Abhängigkeiten von Einsatzueberwachung.Server und deren Lizenzen.

---

## NuGet-Pakete

| Paket | Version | Lizenz | Hinweis |
|---|---|---|---|
| QuestPDF | 2025.7.4 | AGPL-3.0 | Kostenlos zulässig, da Projekt unter MIT (Open Source) veröffentlicht |
| ClosedXML | 0.105.0 | MIT | |
| FluentValidation | 12.1.1 | Apache-2.0 | |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | Apache-2.0 | |
| SkiaSharp | 3.119.2 | MIT | |
| Svg.Skia | 5.1.1 | MIT | SVG-Staffellogo für die App als PNG (nur Rastern, SVG wird vorher bereinigt) |
| Svg.Model, Svg.Animation, Svg.SceneGraph, ShimSkiaSharp | 5.1.1 | MIT | Abhängigkeiten von Svg.Skia |
| Svg.Custom | 5.1.1 | MS-PL | Abhängigkeit von Svg.Skia (Grundlage SVG.NET) |
| ExCSS | 4.3.1 | MIT | Abhängigkeit von Svg.Skia |
| HarfBuzzSharp | 8.3.1.3 | MIT | Textdarstellung in SVG-Logos |
| QRCoder | 1.6.0 | MIT | |
| Microsoft.EntityFrameworkCore | 9.0.0 | MIT | |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.0 | MIT | |
| Microsoft.AspNetCore.SignalR.Client | 9.0.0 | MIT | |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | MIT | |
| Swashbuckle.AspNetCore | 7.2.0 | MIT | |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | |
| Microsoft.Extensions.Http | 9.0.5 | MIT | |
| System.Text.Json | 9.0.5 | MIT | |
| xunit | 2.9.2 | Apache-2.0 | Nur Test-Projekt |
| coverlet.collector | 6.0.2 | MIT | Nur Test-Projekt |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | Nur Test-Projekt |

## JavaScript / CSS (CDN & lokal)

| Bibliothek | Version | Lizenz | Einbindung |
|---|---|---|---|
| Leaflet | 1.9.4 | BSD-2-Clause | CDN (unpkg.com) |
| Leaflet.Draw | 1.0.4 | MIT | CDN (cdnjs.cloudflare.com) |
| Bootstrap | 5.3.3 | MIT | lokal (wwwroot/lib/) |
| Bootstrap Icons | 1.11.3 | MIT | CDN (cdn.jsdelivr.net) |
| Proj4js | 2.9.2 | MIT | CDN (cdnjs.cloudflare.com) |
| JetBrains Mono (Google Fonts) | — | OFL-1.1 | CDN (fonts.googleapis.com) |

## Framework / Runtime

| Komponente | Version | Lizenz |
|---|---|---|
| .NET Runtime | 9.0 | MIT |
| ASP.NET Core | 9.0 | MIT |
| Windows Desktop Runtime (LiveTracking) | 9.0 | MIT |
