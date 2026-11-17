using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Einsatzbericht ab 1.17.0, Folgeseiten: Übersichtskarte, Karte je Trail, Track-Tabelle, Funk und Notizen.
    /// Flächenabdeckung kommt nicht in den Bericht (Karte „polizeibericht“).
    /// </summary>
    public partial class PdfExportService
    {
        /// <summary>Vorab gerenderte Karten; null = nicht verfügbar (kein Renderer oder offline)</summary>
        private sealed record BerichtKarten(byte[]? Uebersicht, Dictionary<string, byte[]> Trails);

        private static readonly System.Text.RegularExpressions.Regex TestNotiz =
            new(@"^\s*test\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Tracks aller Suchen; ältere Archive ohne Suchen nehmen die Track-Liste</summary>
        private static List<TeamTrackSnapshot> BerichtTracks(ArchivedEinsatz einsatz, PdfBerichtOptionen optionen)
        {
            var tracks = einsatz.CompletedSearches?.Count > 0
                ? einsatz.CompletedSearches.SelectMany(s => s.Tracks).ToList()
                : einsatz.TrackSnapshots?.ToList() ?? new();
            return tracks.Where(t => t.Points.Count >= 2 && (optionen.TeamIds is null || optionen.TeamIds.Contains(t.TeamId))).ToList();
        }

        private static List<CompletedSearch> BerichtTrails(ArchivedEinsatz einsatz, PdfBerichtOptionen optionen) =>
            BerichtSuchen(einsatz).Where(s => s.IstTrail && (optionen.TeamIds is null || optionen.TeamIds.Contains(s.TeamId))).ToList();

        private async Task<BerichtKarten> RenderBerichtKartenAsync(ArchivedEinsatz einsatz, PdfBerichtOptionen optionen)
        {
            var trailKarten = new Dictionary<string, byte[]>();
            if (_mapRenderer is null || !optionen.Zeigt("karten"))
                return new BerichtKarten(null, trailKarten);

            byte[]? uebersicht = null;
            var tracks = BerichtTracks(einsatz, optionen);
            if (tracks.Count > 0 || einsatz.SearchAreas?.Count > 0 || einsatz.MapMarkers?.Count > 0)
            {
                try
                {
                    uebersicht = await _mapRenderer.RenderSearchAreaMapAsync(einsatz.SearchAreas ?? new(), einsatz.ElwPosition,
                        new MapPrintOptions { TileType = optionen.Kartenart, ShowGpsTracks = true, ShowPointMarkers = true },
                        einsatz.MapMarkers, tracks, width: 1500, height: 1060);
                }
                catch (Exception)
                {
                    // Ohne Kartenkacheln (offline) bleibt der Bericht ohne Übersichtskarte
                }
            }

            foreach (var trail in BerichtTrails(einsatz, optionen))
            {
                var punkte = TrailPunkte(trail);
                if (punkte.Count < 2) continue;
                try
                {
                    var bild = await _mapRenderer.RenderTrackMapAsync(punkte, null, TrailFarbe(trail), null, 1200, 700, optionen.Kartenart);
                    if (bild is not null) trailKarten[trail.Id] = bild;
                }
                catch (Exception)
                {
                    // Trail ohne Karte: Tabelle mit Start und Ende bleibt
                }
            }

            return new BerichtKarten(uebersicht, trailKarten);
        }

        private static string TrailFarbe(CompletedSearch trail) =>
            trail.Tracks.FirstOrDefault(t => t.TrackType == TrackType.CollarTrack)?.Color is { Length: > 0 } farbe ? farbe : "#B2342A";

        private void ComposeBerichtFolgeseiten(IContainer container, ArchivedEinsatz einsatz, PdfBerichtOptionen optionen, BerichtKarten karten)
        {
            var tracks = BerichtTracks(einsatz, optionen);
            var trails = BerichtTrails(einsatz, optionen);
            var notizen = (einsatz.GlobalNotesEntries ?? new())
                .Where(n => n.Type == GlobalNotesEntryType.Manual && !TestNotiz.IsMatch(n.Text ?? ""))
                .OrderBy(n => n.Timestamp).ToList();

            container.Column(col =>
            {
                var erstes = true;
                void Abschnitt(Action<ColumnDescriptor> inhalt, bool neueSeite = false)
                {
                    if (!erstes && neueSeite) col.Item().PageBreak();
                    erstes = false;
                    // Abschnitt beginnt nicht mit der Überschrift allein am Seitenende
                    col.Item().EnsureSpace(120).Column(inhalt);
                }

                if (optionen.Zeigt("karten") && (karten.Uebersicht is not null || tracks.Count > 0))
                    Abschnitt(c => ComposeBerichtUebersicht(c, einsatz, tracks, karten.Uebersicht));

                if (optionen.Zeigt("karten"))
                    foreach (var trail in trails)
                        Abschnitt(c => ComposeBerichtTrailKarte(c, einsatz, trail, karten.Trails.GetValueOrDefault(trail.Id)), neueSeite: true);

                if (optionen.Zeigt("tracks") && optionen.Tracks && tracks.Count > 0)
                    Abschnitt(c => ComposeBerichtTrackTabelle(c, einsatz, optionen));

                if (optionen.Zeigt("vermisste") && einsatz.Vermisste?.Any() == true)
                    Abschnitt(c => ComposeBerichtVermissteVoll(c, einsatz.Vermisste));

                if (optionen.Zeigt("teams") && einsatz.Teams?.Any() == true)
                    Abschnitt(c => ComposeBerichtTeams(c, einsatz.Teams));

                if (optionen.Zeigt("karten") && einsatz.MapMarkers?.Any() == true)
                    Abschnitt(c => ComposeBerichtPunkte(c, einsatz));

                if (optionen.Zeigt("vorort") && ((einsatz.PersonalNamen?.Any() ?? false) || (einsatz.HundeNamen?.Any() ?? false)))
                    Abschnitt(c => ComposeBerichtVorOrt(c, einsatz));

                if (optionen.Zeigt("funk") && notizen.Count > 0)
                    Abschnitt(c => ComposeBerichtFunk(c, notizen));

                if (erstes)
                    col.Item().Text("Keine weiteren Abschnitte ausgewählt.").FontColor(BerichtGrau);
            });
        }

        private static void ComposeBerichtUebersicht(ColumnDescriptor col, ArchivedEinsatz einsatz, List<TeamTrackSnapshot> tracks, byte[]? karte)
        {
            BerichtUeberschrift(col, "Übersichtskarte");
            if (karte is not null)
                col.Item().Border(1).BorderColor(BerichtLinie).Image(karte).FitWidth();
            else
                col.Item().Text("Karte nicht verfügbar (keine Kartenkacheln erreichbar).").FontColor(BerichtGrau);

            // Legende: Teamfarbe = Farbe des Tracks
            var legende = tracks.GroupBy(t => t.TeamId).Select(g =>
            {
                var team = einsatz.Teams?.FirstOrDefault(x => x.TeamId == g.Key);
                var name = team?.TeamName ?? g.First().TeamName;
                return (Farbe: g.First().Color, Text: TeamMitArt(name, team?.Einsatzart));
            }).ToList();
            if (legende.Count == 0) return;

            col.Item().PaddingTop(6).Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(9));
                foreach (var (farbe, name) in legende)
                {
                    text.Span("■ ").FontColor(string.IsNullOrWhiteSpace(farbe) ? BerichtTinte : farbe).FontSize(11);
                    text.Span(name + "     ");
                }
            });
            col.Item().PaddingTop(2).Text("Nordpfeil oben rechts, Maßstab unten links. Kartenpunkte und ELW wie im Monitor.")
                .FontSize(8).FontColor(BerichtGrau);
        }

        private static void ComposeBerichtTrailKarte(ColumnDescriptor col, ArchivedEinsatz einsatz, CompletedSearch trail, byte[]? karte)
        {
            var team = einsatz.Teams?.FirstOrDefault(t => t.TeamId == trail.TeamId);
            var titel = team is null || string.IsNullOrWhiteSpace(team.DogName) ? trail.TeamName : $"{trail.TeamName} / {team.DogName}";
            BerichtUeberschrift(col, $"Trail {titel}");

            if (karte is not null)
                col.Item().Border(1).BorderColor(BerichtLinie).Image(karte).FitWidth();
            else if (trail.Tracks.FirstOrDefault(t => t.Points.Count >= 2) is { } track)
                col.Item().Height(290).Svg(BuildTrackSvg(track, 520, 300));

            var punkte = TrailPunkte(trail);
            var laenge = SuchStrecke(trail);
            var start = punkte.Count > 0 ? punkte[0] : null;
            var ende = punkte.Count > 0 ? punkte[^1] : null;

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Element(c => BerichtFelder(c, new List<(string, string)>
                {
                    ("Start", $"{Ortszeit.AusUtc(trail.SearchStart):HH:mm} Uhr"),
                    ("WGS84", start is null ? "–" : Wgs84(start.Latitude, start.Longitude)),
                    ("UTM", start is null ? "–" : Utm(start.Latitude, start.Longitude)),
                    ("Geruchsartikel", Leer(trail.Geruchsartikel))
                }, 0.34f));
                row.ConstantItem(18);
                row.RelativeItem().Element(c => BerichtFelder(c, new List<(string, string)>
                {
                    ("Ende der Spur", $"{Ortszeit.AusUtc(trail.SearchEnd):HH:mm} Uhr"),
                    ("WGS84", ende is null ? "–" : Wgs84(ende.Latitude, ende.Longitude)),
                    ("UTM", ende is null ? "–" : Utm(ende.Latitude, ende.Longitude)),
                    ("Abbruchgrund", Leer(trail.Abbruchgrund))
                }, 0.34f));
            });
            col.Item().PaddingTop(4).Text($"Länge {(laenge is { } m ? StreckeText(m) : "–")} · Dauer {DauerKurz(trail.SearchEnd - trail.SearchStart)}")
                .FontColor(BerichtGrau);
        }

        private static void ComposeBerichtTrackTabelle(ColumnDescriptor col, ArchivedEinsatz einsatz, PdfBerichtOptionen optionen)
        {
            BerichtUeberschrift(col, "Tracks");
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.5f); c.RelativeColumn(0.9f); c.RelativeColumn(1.6f); c.RelativeColumn(1.1f); c.RelativeColumn(0.8f); c.RelativeColumn(0.8f);
                });
                BerichtKopfzeile(t, "Team", "Track", "Art / Gebiet", "Zeitraum", "Strecke", "Dauer");

                foreach (var suche in BerichtSuchen(einsatz).Where(s => optionen.TeamIds is null || optionen.TeamIds.Contains(s.TeamId)))
                {
                    foreach (var track in suche.Tracks.Where(x => x.Points.Count >= 2))
                    {
                        var art = suche.Einsatzart?.Anzeige() ?? "–";
                        if (!suche.IstTrail && !string.IsNullOrWhiteSpace(suche.SearchAreaName)) art += $", {suche.SearchAreaName}";
                        BerichtZeile(t,
                            suche.TeamName,
                            track.TrackType == TrackType.HumanTrack ? "Mensch" : "Hund",
                            art,
                            $"{Ortszeit.AusUtc(suche.SearchStart):HH:mm}–{Ortszeit.AusUtc(suche.SearchEnd):HH:mm}",
                            StreckeText(track.TotalDistanceMeters),
                            DauerKurz(suche.SearchEnd - suche.SearchStart));
                    }
                }
            });
        }

        private static void ComposeBerichtVermissteVoll(ColumnDescriptor col, List<VermisstenInfo> vermisste)
        {
            BerichtUeberschrift(col, vermisste.Count > 1 ? "Vermisste Personen" : "Vermisste Person");
            foreach (var v in vermisste)
            {
                var gesehen = string.Join(", ", new[] { v.ZuletztGesehenOrt, v.ZuletztGesehenZeit,
                    string.IsNullOrWhiteSpace(v.ZuletztGesehenVon) ? null : $"von {v.ZuletztGesehenVon}" }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var felder = new List<(string, string)>
                {
                    ("Name", v.VollerName), ("Alter", v.Alter), ("Geburtsdatum", v.Geburtsdatum), ("Beschreibung", v.Beschreibung),
                    ("Kleidung", v.Kleidung), ("Besonderheiten", v.Besonderheiten), ("Zuletzt gesehen", gesehen),
                    ("Vorerkrankungen", v.Vorerkrankungen), ("Medikamente", v.Medikamente),
                    ("Orientierung", v.Orientierung switch { OrientierungsStatus.Gut => "gut", OrientierungsStatus.Eingeschraenkt => "eingeschränkt", _ => "" }),
                    ("Mobilität", v.Mobilitaet switch
                    {
                        MobilitaetsStatus.ZuFuss => "zu Fuß", MobilitaetsStatus.Rollator => "Rollator", MobilitaetsStatus.Rollstuhl => "Rollstuhl",
                        MobilitaetsStatus.Fahrzeug => "Fahrzeug", _ => ""
                    }),
                    ("Suizidrisiko", v.Suizidrisiko == RisikoStatus.Unbekannt ? "" : v.Suizidrisiko == RisikoStatus.Ja ? "ja" : "nein"),
                    ("Bewaffnet", v.Bewaffnet == RisikoStatus.Unbekannt ? "" : v.Bewaffnet == RisikoStatus.Ja ? "ja" : "nein")
                }.Where(f => !string.IsNullOrWhiteSpace(f.Item2)).ToList();
                col.Item().PaddingBottom(6).Element(c => BerichtFelder(c, felder, 0.25f));
            }
        }

        private static void ComposeBerichtTeams(ColumnDescriptor col, List<ArchivedTeam> teams)
        {
            BerichtUeberschrift(col, "Teams");
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(1.2f); c.RelativeColumn(1f); c.RelativeColumn(1f); c.RelativeColumn(2.6f); });
                BerichtKopfzeile(t, "Team", "Hund / Drohne", "Eingesetzt als", "Personal");
                foreach (var team in teams)
                    BerichtZeile(t, team.TeamName,
                        Leer(string.IsNullOrWhiteSpace(team.DogName) ? team.DroneName : team.DogName),
                        team.Einsatzart?.Anzeige() ?? "–",
                        Leer(string.Join(", ", team.MemberNames.Where(n => !string.IsNullOrWhiteSpace(n)))));
            });
        }

        private static void ComposeBerichtPunkte(ColumnDescriptor col, ArchivedEinsatz einsatz)
        {
            BerichtUeberschrift(col, "Kartenpunkte");
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(44); c.RelativeColumn(2.4f); c.RelativeColumn(1.6f); c.RelativeColumn(1.2f); });
                BerichtKopfzeile(t, "Zeit", "Punkt", "WGS84", "UTM");
                foreach (var p in einsatz.MapMarkers.OrderBy(m => m.CreatedAt))
                    BerichtZeile(t, p.CreatedAt.ToString("HH:mm"), (p.IstFund ? "Fund: " : "") + Leer(p.Label),
                        $"{p.Latitude.ToString("F5", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))}, {p.Longitude.ToString("F5", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))}",
                        Utm(p.Latitude, p.Longitude));
            });
        }

        private static void ComposeBerichtVorOrt(ColumnDescriptor col, ArchivedEinsatz einsatz)
        {
            BerichtUeberschrift(col, "Vor Ort");
            col.Item().Element(c => BerichtFelder(c, new List<(string, string)>
            {
                ($"Personal ({einsatz.PersonalNamen?.Count ?? 0})", Leer(string.Join(", ", einsatz.PersonalNamen ?? new()))),
                ($"Hunde ({einsatz.HundeNamen?.Count ?? 0})", Leer(string.Join(", ", einsatz.HundeNamen ?? new())))
            }, 0.25f));
        }

        private static void ComposeBerichtFunk(ColumnDescriptor col, List<GlobalNotesEntry> notizen)
        {
            BerichtUeberschrift(col, "Funk und Notizen");
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(64); c.RelativeColumn(1.1f); c.RelativeColumn(4f); });
                BerichtKopfzeile(t, "Zeit", "Von", "Text");
                foreach (var n in notizen)
                    BerichtZeile(t, n.Timestamp.ToString("dd.MM. HH:mm"), Leer(n.SourceTeamName), NotizText(n.Text, n.Timestamp));
            });
            col.Item().PaddingTop(3).Text("Ohne Timer-, System- und Testeinträge.").FontSize(8).FontColor(BerichtGrau);
        }
    }
}
