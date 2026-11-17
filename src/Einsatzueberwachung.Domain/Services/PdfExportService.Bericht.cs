using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Einsatzbericht ab 1.17.0: Seite 1 fasst alles zusammen, was Polizei und Behörde brauchen
    /// (Muster gestaltung/polizeibericht-seite1.html, Karte „polizeibericht“).
    /// </summary>
    public partial class PdfExportService
    {
        private const string BerichtTinte = "#1C2430";
        private const string BerichtGrau = "#5C6470";
        private const string BerichtLinie = "#D5D9DF";
        private const string BerichtRot = "#B2342A";
        private const string BerichtGruen = "#2E7D4F";
        private const string BerichtGruenFlaeche = "#EEF7F1";
        private const string BerichtFlaeche = "#F3F5F8";

        private void ComposeBerichtSeite1(IContainer container, ArchivedEinsatz einsatz, StaffelInfo staffel)
        {
            container.Column(col =>
            {
                col.Item().Element(c => ComposeBerichtKopf(c, einsatz, staffel));
                col.Item().PaddingTop(8).Element(c => ComposeBerichtKennleiste(c, einsatz));
                col.Item().PaddingTop(10).Element(c => ComposeBerichtErgebnis(c, einsatz));

                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Element(c => ComposeBerichtPolizei(c, einsatz));
                    row.ConstantItem(18);
                    row.RelativeItem().Element(c => ComposeBerichtVermisste(c, einsatz));
                });

                col.Item().Element(c => ComposeBerichtAblauf(c, einsatz));

                var suchen = BerichtSuchen(einsatz);
                if (suchen.Count > 0)
                    col.Item().Element(c => ComposeBerichtSuchen(c, einsatz, suchen));

                var trails = suchen.Where(s => s.IstTrail).ToList();
                if (trails.Count > 0)
                    col.Item().Element(c => ComposeBerichtTrails(c, trails));

                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => ComposeBerichtKraefte(c, einsatz));
                    row.ConstantItem(18);
                    row.RelativeItem().Element(c => ComposeBerichtUnterschrift(c, einsatz));
                });
            });
        }

        private void ComposeBerichtKopf(IContainer container, ArchivedEinsatz einsatz, StaffelInfo staffel)
        {
            container.BorderTop(3).BorderColor(einsatz.IstEinsatz ? BerichtRot : BerichtTinte).PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(einsatz.IstEinsatz ? "Einsatzbericht" : "Übungsbericht").FontSize(17).Bold().FontColor(BerichtTinte);
                    c.Item().PaddingTop(2).Text("Personensuche mit Rettungshunden · automatisch erstellt aus den Einsatzdaten")
                        .FontSize(9).FontColor(BerichtGrau);
                });

                row.ConstantItem(190).AlignRight().Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(staffel.Name))
                        c.Item().AlignRight().Text(staffel.Name).FontSize(9.5f).Bold().FontColor(BerichtTinte);
                    if (!string.IsNullOrWhiteSpace(staffel.Address))
                        c.Item().AlignRight().Text(staffel.Address).FontSize(8.5f).FontColor(BerichtGrau);
                    var kontakt = string.Join(" · ", new[] { staffel.Telefon, staffel.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (kontakt.Length > 0)
                        c.Item().AlignRight().Text(kontakt).FontSize(8.5f).FontColor(BerichtGrau);
                });

                if (TryLoadLogoBytes(staffel.LogoPath, out var logo))
                    row.ConstantItem(58).PaddingLeft(10).AlignRight().Height(44).Image(logo).FitArea();
            });
        }

        private static void ComposeBerichtKopfSchmal(IContainer container, ArchivedEinsatz einsatz, StaffelInfo staffel)
        {
            container.PaddingBottom(8).BorderBottom(1).BorderColor(BerichtLinie).PaddingBottom(4).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(einsatz.IstEinsatz ? "Einsatzbericht " : "Übungsbericht ").Bold();
                    text.Span(Leer(einsatz.EinsatzNummer));
                    if (!string.IsNullOrWhiteSpace(einsatz.Vorgangsnummer))
                        text.Span($" · Polizei {einsatz.Vorgangsnummer}").FontColor(BerichtGrau);
                });
                row.AutoItem().AlignRight().Text(staffel.Name).FontSize(8.5f).FontColor(BerichtGrau);
            });
        }

        private static void ComposeBerichtKennleiste(IContainer container, ArchivedEinsatz einsatz)
        {
            var art = einsatz.IstEinsatz ? "Einsatz" : "Übung";
            if (einsatz.Szenario != EinsatzSzenarioType.Unbestimmt)
                art += " · " + SzenarioAnzeige(einsatz.Szenario);

            var felder = new (string Titel, string Wert)[]
            {
                ("Einsatznummer", Leer(einsatz.EinsatzNummer)),
                ("Vorgangsnummer Polizei", Leer(einsatz.Vorgangsnummer)),
                ("Datum", (einsatz.AlarmierungsZeit ?? einsatz.EinsatzDatum).ToString("dd.MM.yyyy")),
                ("Art", art)
            };

            container.Border(1).BorderColor(BerichtLinie).Row(row =>
            {
                for (var i = 0; i < felder.Length; i++)
                {
                    var (titel, wert) = felder[i];
                    var zelle = row.RelativeItem();
                    if (i < felder.Length - 1)
                        zelle = zelle.BorderRight(1).BorderColor(BerichtLinie);
                    zelle.PaddingVertical(5).PaddingHorizontal(8).Column(c =>
                    {
                        c.Item().Text(titel.ToUpperInvariant()).FontSize(7).FontColor(BerichtGrau).LetterSpacing(0.04f);
                        c.Item().Text(wert).Bold();
                    });
                }
            });
        }

        private static void ComposeBerichtErgebnis(IContainer container, ArchivedEinsatz einsatz)
        {
            var fund = einsatz.Fund ?? new EinsatzFund();
            var istFund = ErgebnisText.IstFund(einsatz.Ergebnis);
            var farbe = istFund ? BerichtGruen : BerichtTinte;
            var flaeche = istFund ? BerichtGruenFlaeche : BerichtFlaeche;

            container.Border(1.5f).BorderColor(farbe).Background(flaeche).PaddingVertical(8).PaddingHorizontal(10).Column(col =>
            {
                col.Item().Text(string.IsNullOrWhiteSpace(einsatz.Ergebnis) ? "Ergebnis nicht angegeben" : ErgebnisText.Anzeige(einsatz.Ergebnis))
                    .FontSize(13).Bold().FontColor(farbe);

                if (!istFund)
                {
                    if (!string.IsNullOrWhiteSpace(einsatz.Bemerkungen))
                        col.Item().PaddingTop(4).Text(einsatz.Bemerkungen).FontSize(9.5f);
                    return;
                }

                var finder = einsatz.Teams?.FirstOrDefault(t => t.TeamId == fund.TeamId);
                var links = new List<(string, string)>
                {
                    ("Fundzeit", fund.ZeitUtc is { } z ? $"{Ortszeit.AusUtc(z):dd.MM.yyyy, HH:mm} Uhr" : "–"),
                    ("Fundort", Leer(fund.Ort)),
                    ("Gefunden durch", finder is null ? "–" : TeamMitArt(finder.TeamName, finder.Einsatzart))
                };
                var rechts = new List<(string, string)>
                {
                    ("WGS84", fund.Latitude is { } la && fund.Longitude is { } lo ? Wgs84(la, lo) : "–"),
                    ("UTM", fund.Latitude is { } la2 && fund.Longitude is { } lo2 ? Utm(la2, lo2) : "–"),
                    ("Übergabe an", Leer(fund.UebergabeAn))
                };

                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem(1.2f).Element(c => BerichtFelder(c, links, 0.36f));
                    row.ConstantItem(16);
                    row.RelativeItem().Element(c => BerichtFelder(c, rechts, 0.36f));
                });

                if (!string.IsNullOrWhiteSpace(einsatz.Bemerkungen))
                    col.Item().PaddingTop(4).Text(einsatz.Bemerkungen).FontSize(9.5f);
            });
        }

        private static void ComposeBerichtPolizei(IContainer container, ArchivedEinsatz einsatz)
        {
            var kontakte = EinsatzKontaktDaten.From(einsatz);
            container.Column(col =>
            {
                BerichtUeberschrift(col, "Polizei");
                col.Item().Element(c => BerichtFelder(c, new List<(string, string)>
                {
                    ("Ansprechpartner", Leer(kontakte.PolizeiKontaktName)),
                    ("Dienstnummer", Leer(kontakte.PolizeiDienstnummer)),
                    ("Telefon", Leer(kontakte.PolizeiTelefon)),
                    ("Alarmiert durch", Leer(AbschlussAngaben.Melder(einsatz.Alarmiert)))
                }, 0.42f));
            });
        }

        private static void ComposeBerichtVermisste(IContainer container, ArchivedEinsatz einsatz)
        {
            var person = einsatz.Vermisste?.FirstOrDefault();
            container.Column(col =>
            {
                BerichtUeberschrift(col, einsatz.Vermisste?.Count > 1 ? "Vermisste Personen" : "Vermisste Person");
                if (person is null)
                {
                    col.Item().Text("keine Angaben").FontColor(BerichtGrau);
                    return;
                }

                var name = person.VollerName;
                if (!string.IsNullOrWhiteSpace(person.Alter))
                    name += $", {person.Alter} Jahre";
                var gesehen = string.Join(", ", new[] { person.ZuletztGesehenOrt, person.ZuletztGesehenZeit }.Where(s => !string.IsNullOrWhiteSpace(s)));

                col.Item().Element(c => BerichtFelder(c, new List<(string, string)>
                {
                    ("Name", Leer(name)),
                    ("Zuletzt gesehen", Leer(gesehen)),
                    ("Besonderheiten", Leer(person.Besonderheiten))
                }, 0.42f));

                if (einsatz.Vermisste!.Count > 1)
                    col.Item().PaddingTop(2).Text($"weitere: {string.Join(", ", einsatz.Vermisste.Skip(1).Select(v => v.VollerName))}")
                        .FontSize(8.5f).FontColor(BerichtGrau);
            });
        }

        private static void ComposeBerichtAblauf(IContainer container, ArchivedEinsatz einsatz)
        {
            var eintraege = new List<(DateTime Zeit, string Text)>();
            if (einsatz.AlarmierungsZeit is { } alarm)
                eintraege.Add((alarm, AbschlussAngaben.Melder(einsatz.Alarmiert) is { Length: > 0 } melder ? $"Alarmierung durch {melder}" : "Alarmierung"));
            var ort = string.IsNullOrWhiteSpace(einsatz.Einsatzort) ? "" : $", Einsatzort {einsatz.Einsatzort}";
            eintraege.Add((einsatz.EinsatzDatum, $"Einsatz eröffnet{ort}"));

            foreach (var suche in einsatz.CompletedSearches ?? new())
            {
                var text = $"Suchbeginn {suche.TeamName}";
                var zusatz = new List<string>();
                if (suche.Einsatzart is { } art) zusatz.Add(art.Anzeige());
                if (!string.IsNullOrWhiteSpace(suche.SearchAreaName) && !suche.IstTrail) zusatz.Add(suche.SearchAreaName!);
                if (suche.IstTrail && !string.IsNullOrWhiteSpace(suche.Geruchsartikel)) zusatz.Add($"Geruchsartikel: {suche.Geruchsartikel}");
                if (zusatz.Count > 0) text += $" ({string.Join(", ", zusatz)})";
                eintraege.Add((Ortszeit.AusUtc(suche.SearchStart), text));
            }

            if (einsatz.Fund?.ZeitUtc is { } fundZeit)
            {
                var text = ErgebnisText.Anzeige(einsatz.Ergebnis);
                if (!string.IsNullOrWhiteSpace(einsatz.Fund.UebergabeAn))
                    text += $", Übergabe an {einsatz.Fund.UebergabeAn}";
                eintraege.Add((Ortszeit.AusUtc(fundZeit), text));
            }

            if (einsatz.EinsatzEnde is { } ende)
                eintraege.Add((ende, einsatz.Dauer is { } d ? $"Einsatzende · Dauer {DauerKurz(d)}" : "Einsatzende"));

            var mehrereTage = eintraege.Select(e => e.Zeit.Date).Distinct().Count() > 1;

            container.Column(col =>
            {
                BerichtUeberschrift(col, "Zeitlicher Ablauf");
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(mehrereTage ? 74 : 40); c.RelativeColumn(); });
                    foreach (var (zeit, text) in eintraege.OrderBy(e => e.Zeit))
                    {
                        t.Cell().PaddingVertical(1.5f).Text(mehrereTage ? zeit.ToString("dd.MM. HH:mm") : zeit.ToString("HH:mm")).Bold();
                        t.Cell().PaddingVertical(1.5f).Text(text);
                    }
                });
            });
        }

        private static void ComposeBerichtSuchen(IContainer container, ArchivedEinsatz einsatz, List<CompletedSearch> suchen)
        {
            container.Column(col =>
            {
                BerichtUeberschrift(col, "Suche je Team");
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.6f); c.RelativeColumn(1.8f); c.RelativeColumn(1.2f); c.RelativeColumn(0.8f); c.RelativeColumn(1f);
                    });
                    BerichtKopfzeile(t, "Team / Hund", "Art", "Zeitraum", "Strecke", "Ergebnis");

                    foreach (var suche in suchen)
                    {
                        var team = einsatz.Teams?.FirstOrDefault(x => x.TeamId == suche.TeamId);
                        var teamText = team is null || string.IsNullOrWhiteSpace(team.DogName) || team.DogName == suche.TeamName
                            ? suche.TeamName
                            : $"{suche.TeamName} / {team.DogName}";
                        var art = suche.Einsatzart?.Anzeige() ?? "–";
                        if (!suche.IstTrail && !string.IsNullOrWhiteSpace(suche.SearchAreaName))
                            art += $", {suche.SearchAreaName}";
                        var zeitraum = $"{Ortszeit.AusUtc(suche.SearchStart):HH:mm}–{Ortszeit.AusUtc(suche.SearchEnd):HH:mm}";
                        var strecke = SuchStrecke(suche) is { } m ? StreckeText(m) : "–";
                        var ergebnis = IstFundSuche(einsatz, suche) ? "Fund" : "ohne Befund";

                        BerichtZeile(t, teamText, art, zeitraum, strecke, ergebnis);
                    }
                });
            });
        }

        private static void ComposeBerichtTrails(IContainer container, List<CompletedSearch> trails)
        {
            container.Column(col =>
            {
                BerichtUeberschrift(col, "Trails (Mantrailer)");
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(0.9f); c.RelativeColumn(2f); c.RelativeColumn(1.6f); c.RelativeColumn(1.5f); c.RelativeColumn(1f);
                    });
                    BerichtKopfzeile(t, "Team", "Start", "Ende der Spur", "Ende (UTM)", "Geruchsartikel");

                    foreach (var trail in trails)
                    {
                        var punkte = TrailPunkte(trail);
                        var start = punkte.Count > 0 ? $"{Ortszeit.AusUtc(trail.SearchStart):HH:mm}, {Utm(punkte[0].Latitude, punkte[0].Longitude)}" : Ortszeit.AusUtc(trail.SearchStart).ToString("HH:mm");
                        var ende = $"{Ortszeit.AusUtc(trail.SearchEnd):HH:mm}" + (string.IsNullOrWhiteSpace(trail.Abbruchgrund) ? "" : $", {trail.Abbruchgrund}");
                        var endeUtm = punkte.Count > 0 ? Utm(punkte[^1].Latitude, punkte[^1].Longitude) : "–";
                        BerichtZeile(t, trail.TeamName, start, ende, endeUtm, Leer(trail.Geruchsartikel));
                    }
                });
                col.Item().PaddingTop(3).Text("Karte jedes Trails mit Start, Verlauf und Ende auf den Folgeseiten.").FontSize(8.5f).FontColor(BerichtGrau);
            });
        }

        private static void ComposeBerichtKraefte(IContainer container, ArchivedEinsatz einsatz)
        {
            var leitung = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(einsatz.Einsatzleiter) ? null : $"{einsatz.Einsatzleiter} (EL)",
                string.IsNullOrWhiteSpace(einsatz.Fuehrungsassistent) ? null : $"{einsatz.Fuehrungsassistent} (FüAss)"
            }.Where(s => s is not null));

            var teams = einsatz.AnzahlTeams > 0 ? einsatz.AnzahlTeams : einsatz.Teams?.Count ?? 0;
            var personen = einsatz.PersonalNamen?.Count > 0 ? einsatz.PersonalNamen.Count : einsatz.AnzahlPersonal;
            var hunde = einsatz.HundeNamen?.Count > 0 ? einsatz.HundeNamen.Count : einsatz.AnzahlHunde;

            container.Column(col =>
            {
                BerichtUeberschrift(col, "Eingesetzte Kräfte");
                col.Item().Element(c => BerichtFelder(c, new List<(string, string)>
                {
                    ("Einsatzleitung", Leer(leitung)),
                    ("Vor Ort", $"{teams} {(teams == 1 ? "Team" : "Teams")} · {personen} {(personen == 1 ? "Person" : "Personen")} · {hunde} {(hunde == 1 ? "Hund" : "Hunde")}")
                }, 0.38f));
            });
        }

        private static void ComposeBerichtUnterschrift(IContainer container, ArchivedEinsatz einsatz)
        {
            container.Column(col =>
            {
                BerichtUeberschrift(col, "Bestätigung Einsatzleiter");
                col.Item().Text(Leer(einsatz.Einsatzleiter));
                col.Item().PaddingTop(26).BorderTop(1).BorderColor(BerichtTinte).PaddingTop(2)
                    .Text("Datum, Unterschrift").FontSize(8).FontColor(BerichtGrau);
            });
        }

        private void ComposeBerichtFuss(IContainer container, ArchivedEinsatz einsatz)
        {
            var zone = Ortszeit.Zone.IsDaylightSavingTime(einsatz.EinsatzDatum) ? "MESZ" : "MEZ";
            container.BorderTop(1).BorderColor(BerichtLinie).PaddingTop(4).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(BerichtGrau));
                    text.Span($"{Leer(einsatz.EinsatzNummer)} · erstellt {Now:dd.MM.yyyy HH:mm} · Zeiten in {zone} · Seite ");
                    text.CurrentPageNumber();
                    text.Span(" von ");
                    text.TotalPages();
                });
                row.ConstantItem(90).Element(ComposeFussLogo);
            });
        }

        // ---------- Daten ----------

        private static List<CompletedSearch> BerichtSuchen(ArchivedEinsatz einsatz) =>
            (einsatz.CompletedSearches ?? new()).OrderBy(s => s.SearchStart).ToList();

        private static bool IstFundSuche(ArchivedEinsatz einsatz, CompletedSearch suche)
        {
            var fund = einsatz.Fund;
            if (fund?.TeamId is null || fund.TeamId != suche.TeamId || fund.ZeitUtc is not { } zeit)
                return false;
            // SearchStart/SearchEnd sind UTC; der Fund kann kurz nach dem Stopp eingetragen sein
            return suche.SearchStart <= zeit.AddMinutes(1) && zeit <= suche.SearchEnd.AddMinutes(15);
        }

        /// <summary>Hund-Track bevorzugt, sonst Laufweg des Menschen.</summary>
        private static List<TrackPoint> TrailPunkte(CompletedSearch suche) =>
            (suche.Tracks.FirstOrDefault(t => t.TrackType == TrackType.CollarTrack && t.Points.Count > 0)
             ?? suche.Tracks.FirstOrDefault(t => t.Points.Count > 0))?.Points ?? new();

        private static double? SuchStrecke(CompletedSearch suche)
        {
            var track = suche.Tracks.FirstOrDefault(t => t.TrackType == TrackType.CollarTrack && t.Points.Count >= 2)
                        ?? suche.Tracks.FirstOrDefault(t => t.Points.Count >= 2);
            return track?.TotalDistanceMeters;
        }

        // ---------- Bausteine ----------

        private static void BerichtUeberschrift(ColumnDescriptor col, string titel)
        {
            col.Item().PaddingTop(12).PaddingBottom(4).BorderBottom(1.5f).BorderColor(BerichtTinte).PaddingBottom(2)
                .Text(titel.ToUpperInvariant()).FontSize(9.5f).Bold().LetterSpacing(0.05f).FontColor(BerichtTinte);
        }

        private static void BerichtFelder(IContainer container, List<(string Titel, string Wert)> felder, float titelAnteil)
        {
            container.Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(titelAnteil); c.RelativeColumn(1 - titelAnteil); });
                foreach (var (titel, wert) in felder)
                {
                    t.Cell().PaddingVertical(1.5f).PaddingRight(6).Text(titel).FontColor(BerichtGrau);
                    t.Cell().PaddingVertical(1.5f).Text(wert);
                }
            });
        }

        private static void BerichtKopfzeile(TableDescriptor t, params string[] titel)
        {
            t.Header(h =>
            {
                foreach (var x in titel)
                    h.Cell().BorderBottom(1).BorderColor(BerichtLinie).PaddingVertical(2).PaddingRight(4)
                        .Text(x.ToUpperInvariant()).FontSize(7.5f).FontColor(BerichtGrau).LetterSpacing(0.03f);
            });
        }

        private static void BerichtZeile(TableDescriptor t, params string[] werte)
        {
            foreach (var w in werte)
                t.Cell().BorderBottom(1).BorderColor(BerichtLinie).PaddingVertical(2.5f).PaddingRight(4).Text(w).FontSize(9.5f);
        }

        private static string Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? "–" : wert.Trim();

        private static string TeamMitArt(string team, Einsatzart? art) => art is { } a ? $"{team} ({a.Anzeige()})" : team;

        private static string Wgs84(double lat, double lng)
        {
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            return $"{Math.Abs(lat).ToString("F6", de)}° {(lat >= 0 ? "N" : "S")} · {Math.Abs(lng).ToString("F6", de)}° {(lng >= 0 ? "O" : "W")}";
        }

        private static string Utm(double lat, double lng)
        {
            var (zone, band, ost, nord) = UtmConverter.LatLongToUtm(lat, lng);
            return $"{zone}{band} {ost:0} {nord:0}";
        }

        private static string StreckeText(double meter) =>
            meter >= 1000 ? $"{(meter / 1000).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} km" : $"{meter:0} m";

        private static string DauerKurz(TimeSpan d) =>
            d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{(int)Math.Round(d.TotalMinutes)} min";

        private static string SzenarioAnzeige(EinsatzSzenarioType s) => s switch
        {
            EinsatzSzenarioType.Flaeche => "Fläche",
            EinsatzSzenarioType.Truemmer => "Trümmer",
            _ => s.ToString()
        };
    }
}
