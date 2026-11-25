# Third-Party Licenses

Dieses Dokument listet alle Drittanbieter-Abhängigkeiten von Einsatzueberwachung.Server und deren Lizenzen.

---

## NuGet-Pakete

| Paket | Version | Lizenz | Hinweis |
|---|---|---|---|
| QuestPDF | 2025.7.4 | QuestPDF Community License | Kostenlos u. a. für Open-Source-Projekte unter OSI-Lizenz (dieses Projekt: MIT), gemeinnützige Organisationen und Unternehmen unter 1 Mio. USD Jahresumsatz; ausgenommen sind laut Lizenz Behörden und öffentliche Stellen. Im Code `LicenseType.Community`. Siehe questpdf.com/license |
| ClosedXML | 0.105.0 | MIT | |
| FluentValidation | 12.1.1 | Apache-2.0 | |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | Apache-2.0 | |
| SkiaSharp | 3.119.2 | MIT | |
| SkiaSharp.NativeAssets.Linux | 3.119.2 | MIT | native Bibliothek für Linux |
| Svg.Skia | 5.1.1 | MIT | SVG-Staffellogo für die App als PNG (nur Rastern, SVG wird vorher bereinigt) |
| Svg.Model, Svg.Animation, Svg.SceneGraph, ShimSkiaSharp | 5.1.1 | MIT | Abhängigkeiten von Svg.Skia |
| Svg.Custom | 5.1.1 | MS-PL | Abhängigkeit von Svg.Skia (Grundlage SVG.NET) |
| ExCSS | 4.3.1 | MIT | Abhängigkeit von Svg.Skia |
| HarfBuzzSharp | 8.3.1.3 | MIT | Textdarstellung in SVG-Logos |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT | native Bibliothek, legt der Release-Workflow ins Paket |
| QRCoder | 1.6.0 | MIT | |
| Microsoft.EntityFrameworkCore | 9.0.0 | MIT | |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.0 | MIT | |
| Microsoft.AspNetCore.SignalR.Client | 9.0.0 | MIT | |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | MIT | |
| Swashbuckle.AspNetCore | 7.2.0 | MIT | mit Swagger, SwaggerGen, SwaggerUI |
| ClosedXML.Parser | 2.0.0 | MIT | Abhängigkeit von ClosedXML |
| DocumentFormat.OpenXml, DocumentFormat.OpenXml.Framework | 3.1.1 | MIT | Abhängigkeit von ClosedXML |
| ExcelNumberFormat | 1.1.0 | MIT | Abhängigkeit von ClosedXML |
| RBush.Signed | 4.0.0 | MIT | Abhängigkeit von ClosedXML |
| SixLabors.Fonts | 1.0.0 | Apache-2.0 | Abhängigkeit von ClosedXML |
| SQLitePCLRaw (bundle_e_sqlite3, core, lib, provider) | 2.1.10 | Apache-2.0 | Abhängigkeit von EF Core Sqlite; SQLite selbst ist gemeinfrei (Public Domain) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | |
| Microsoft.Extensions.Http | 9.0.5 | MIT | |
| System.Text.Json | 9.0.5 | MIT | |
| xunit | 2.9.2 | Apache-2.0 | Nur Test-Projekt |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | Nur Test-Projekt |
| coverlet.collector | 6.0.2 | MIT | Nur Test-Projekt |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | Nur Test-Projekt |

## JavaScript / CSS / Schriften (alle lokal, keine CDN)

| Bibliothek | Version | Lizenz | Einbindung |
|---|---|---|---|
| Leaflet | 1.9.4 | BSD-2-Clause | lokal (wwwroot/lib/leaflet) |
| Leaflet.Draw | 1.0.4 | MIT | lokal (wwwroot/lib/leaflet-draw) |
| Bootstrap | 5.3.3 | MIT | lokal (wwwroot/lib/) |
| Bootstrap Icons | 1.11.3 | MIT | lokal (wwwroot/lib/bootstrap-icons) |
| Font Awesome Free | 6.7.2 | Icons CC BY 4.0, Schriften OFL-1.1, Code MIT | lokal (wwwroot/lib/fontawesome) |
| Proj4js | 2.9.2 | MIT | lokal (wwwroot/lib/proj4) |
| hls.js | 1.5.13 | Apache-2.0 | lokal (wwwroot/lib/hls), nur Drohnen-Stream |
| JetBrains Mono | @fontsource 5.1.1 | OFL-1.1 | lokal (wwwroot/lib/jetbrains-mono) |

## Kartendienste und Kartendaten (werden zur Laufzeit geladen, nicht mitgeliefert)

| Dienst | Lizenz / Bedingungen | Hinweis |
|---|---|---|
| OpenStreetMap-Kacheln (tile.openstreetmap.org) | Daten ODbL, Kacheln CC BY-SA; Tile Usage Policy der OSMF | Quellenvermerk „© OpenStreetMap-Mitwirkende“ mit Link; kein Vorabladen |
| OpenTopoMap | CC BY-SA 3.0, Daten ODbL und SRTM | Quellenvermerk wie von OpenTopoMap empfohlen |
| Nominatim (Adresssuche) | Nominatim Usage Policy der OSMF | nur auf Knopfdruck, eigener User-Agent |
| Luftbilder der Länder (DOP20, WMS) | je Land CC BY 4.0, dl-de/by-2.0, dl-de/zero-2.0 oder ohne Bedingungen | Liste und Quellenvermerke: `wwwroot/data/luftbild-laender.json` |
| Esri World Imagery (optional) | Esri-Nutzungsbedingungen, nur mit eigenem Schlüssel der Staffel (ArcGIS Location Platform) | „Powered by Esri“ |
| Wetter über Bright Sky (api.brightsky.dev) | Daten: Open Data des Deutschen Wetterdienstes (Quellenangabe „Deutscher Wetterdienst“) | Bright Sky ist ein freier Dienst, der die DWD-Daten bereitstellt |

## Framework / Runtime

| Komponente | Version | Lizenz |
|---|---|---|
| .NET Runtime | 9.0 | MIT |
| ASP.NET Core | 9.0 | MIT |
| Windows Desktop Runtime (LiveTracking) | 9.0 | MIT |
