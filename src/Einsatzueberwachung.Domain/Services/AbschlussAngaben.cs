using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public sealed record AbschlussPolizei(string? Name, string? Dienstnummer, string? Telefon);

    /// <summary>Fund beim Einsatzende; Zeit in UTC.</summary>
    public sealed record AbschlussFund(DateTime? ZeitUtc, double? Lat, double? Lng, string? Ort, string? UebergabeAn, string? TeamId);

    public sealed record AbschlussTeam(string TeamId, string? Einsatzart);

    public sealed record AbschlussTrail(string SucheId, string? Abbruchgrund, string? Geruchsartikel);

    /// <summary>Angaben aus dem Beenden-Dialog (Monitor und EL-App, APP_API EL-Rolle Abschnitt 10). null = unverändert.</summary>
    public sealed record AbschlussEingabe(
        string? Vorgangsnummer = null,
        string? Melder = null,
        AbschlussPolizei? Polizei = null,
        AbschlussFund? Fund = null,
        IReadOnlyList<AbschlussTeam>? Teams = null,
        IReadOnlyList<AbschlussTrail>? Trails = null,
        string? AlarmiertDurch = null,
        bool? KeinePolizei = null);

    /// <summary>
    /// Was der Beenden-Dialog abfragt: fehlende Angaben ermitteln und Eingaben prüfen und übernehmen.
    /// Gemeinsam für den Monitor und die EL-App, damit beide dieselben Regeln haben.
    /// </summary>
    public static class AbschlussAngaben
    {
        public const int MaxTextLaenge = 200;

        public static readonly IReadOnlyList<string> StandardAbbruchgruende = new[]
        {
            "Person gefunden",
            "Spur verloren",
            "Hund erschöpft",
            "Abbruch durch Einsatzleitung",
            "Gefahr im Gelände"
        };

        /// <summary>
        /// „Alarmiert durch“ ohne den Platzhalter aus der Start-Schnittstelle (dort steht ohne Melder die Uhrzeit,
        /// EinsatzController) – sonst stünde im Bericht eine Uhrzeit als Melder.
        /// </summary>
        public static string Melder(string? alarmiert) =>
            string.IsNullOrWhiteSpace(alarmiert) || System.Text.RegularExpressions.Regex.IsMatch(alarmiert.Trim(), @"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$")
                ? string.Empty
                : alarmiert.Trim();

        public static IReadOnlyList<string> Abbruchgruende(AppSettings? einstellungen) =>
            einstellungen?.Abbruchgruende is { Count: > 0 } liste ? liste : StandardAbbruchgruende;

        /// <summary>Teams, für die eine Einsatzart gefragt ist: Hundeteams, keine Drohnen- oder Unterstützungsteams.</summary>
        public static IEnumerable<Team> Hundeteams(IEnumerable<Team> teams) =>
            teams.Where(t => !t.IsDroneTeam && !t.IsSupportTeam);

        /// <summary>Abgeschlossene Suchen mit Einsatzart Mantrailer (ohne eigene Art zählt die Art des Teams).</summary>
        public static List<CompletedSearch> Trails(EinsatzData einsatz, IEnumerable<Team> teams)
        {
            var artJeTeam = teams.ToDictionary(t => t.TeamId, t => t.Einsatzart);
            return (einsatz.CompletedSearches ?? new())
                .Where(s => (s.Einsatzart ?? artJeTeam.GetValueOrDefault(s.TeamId)) == Einsatzart.Mantrailer)
                .OrderBy(s => s.SearchStart)
                .ToList();
        }

        /// <summary>Jüngster Fund-Punkt eines Teams oder der Einsatzleitung als Vorschlag für den Fundort.</summary>
        public static MapMarker? FundVorschlag(EinsatzData einsatz) =>
            einsatz.MapMarkers?.Where(m => m.IstFund).OrderByDescending(m => m.CreatedAt).FirstOrDefault();

        /// <summary>
        /// Polizeiangaben sind gefragt bei einem Einsatz (keine Übung), den die Polizei alarmiert hat, solange nicht
        /// „Keine Polizei beteiligt“ bestätigt ist (Entscheidung Paul 17.11.2026).
        /// </summary>
        public static bool PolizeiGefragt(EinsatzData einsatz) =>
            einsatz.IstEinsatz && einsatz.AlarmiertDurch == Models.Enums.AlarmiertDurch.Polizei && !einsatz.KeinePolizei;

        /// <summary>
        /// Noch leere Angaben: alarmiertDurch (nur Einsätze), vorgangsnummer und polizei (nur wenn PolizeiGefragt),
        /// teams, trails – mit Ergebnis zusätzlich fund, wenn das Ergebnis ein Fund ist.
        /// </summary>
        public static List<string> Fehlend(EinsatzData einsatz, IEnumerable<Team> teams, string? ergebnis = null)
        {
            var liste = teams.ToList();
            var fehlend = new List<string>();
            if (einsatz.IstEinsatz && einsatz.AlarmiertDurch is null) fehlend.Add("alarmiertDurch");
            if (PolizeiGefragt(einsatz) && string.IsNullOrWhiteSpace(einsatz.Vorgangsnummer)) fehlend.Add("vorgangsnummer");
            if (PolizeiGefragt(einsatz) && string.IsNullOrWhiteSpace(einsatz.PolizeiKontaktName)) fehlend.Add("polizei");
            if (Hundeteams(liste).Any(t => t.Einsatzart is null)) fehlend.Add("teams");
            if (Trails(einsatz, liste).Any(s => string.IsNullOrWhiteSpace(s.Abbruchgrund) || string.IsNullOrWhiteSpace(s.Geruchsartikel)))
                fehlend.Add("trails");
            if (ErgebnisText.IstFund(ergebnis) && FundFehlt(einsatz.Fund))
                fehlend.Add("fund");
            return fehlend;
        }

        private static bool FundFehlt(EinsatzFund? fund) =>
            fund?.ZeitUtc is null || fund.Latitude is null && string.IsNullOrWhiteSpace(fund.Ort);

        /// <summary>Prüft die Eingabe vollständig und übernimmt sie erst dann. Rückgabe: Fehlertext oder null.</summary>
        public static string? Anwenden(EinsatzData einsatz, IList<Team> teams, AbschlussEingabe eingabe,
            IReadOnlyList<string> abbruchgruende, DateTime nowUtc)
        {
            if (Pruefen(einsatz, teams, eingabe, abbruchgruende, nowUtc) is { } fehler)
                return fehler;

            if (Models.Enums.AlarmiertDurchExtensions.AusApiWert(eingabe.AlarmiertDurch) is { } alarmiert) einsatz.AlarmiertDurch = alarmiert;
            if (eingabe.KeinePolizei is { } keine) einsatz.KeinePolizei = keine;
            if (eingabe.Vorgangsnummer is not null) einsatz.Vorgangsnummer = eingabe.Vorgangsnummer.Trim();
            if (eingabe.Melder is not null) einsatz.Alarmiert = eingabe.Melder.Trim();
            if (eingabe.Polizei is { } p)
            {
                if (p.Name is not null) einsatz.PolizeiKontaktName = p.Name.Trim();
                if (p.Dienstnummer is not null) einsatz.PolizeiDienstnummer = p.Dienstnummer.Trim();
                if (p.Telefon is not null) einsatz.PolizeiTelefon = p.Telefon.Trim();
            }
            if (eingabe.Fund is { } f)
            {
                einsatz.Fund ??= new EinsatzFund();
                if (f.ZeitUtc is not null) einsatz.Fund.ZeitUtc = DateTime.SpecifyKind(f.ZeitUtc.Value, DateTimeKind.Utc);
                if (f.Lat is not null && f.Lng is not null)
                {
                    einsatz.Fund.Latitude = Math.Round(f.Lat.Value, 6);
                    einsatz.Fund.Longitude = Math.Round(f.Lng.Value, 6);
                }
                if (f.Ort is not null) einsatz.Fund.Ort = f.Ort.Trim();
                if (f.UebergabeAn is not null) einsatz.Fund.UebergabeAn = f.UebergabeAn.Trim();
                if (f.TeamId is not null) einsatz.Fund.TeamId = f.TeamId.Length == 0 ? null : f.TeamId;
            }
            foreach (var t in eingabe.Teams ?? Array.Empty<AbschlussTeam>())
            {
                var team = teams.First(x => x.TeamId == t.TeamId);
                var art = EinsatzartExtensions.AusApiWert(t.Einsatzart);
                team.Einsatzart = art;
                // Suchen ohne festgehaltene Art bekommen die Art des Teams
                foreach (var s in (einsatz.CompletedSearches ?? new()).Where(s => s.TeamId == team.TeamId && s.Einsatzart is null))
                    s.Einsatzart = art;
            }
            foreach (var t in eingabe.Trails ?? Array.Empty<AbschlussTrail>())
            {
                var suche = einsatz.CompletedSearches!.First(s => s.Id == t.SucheId);
                if (t.Abbruchgrund is not null) suche.Abbruchgrund = t.Abbruchgrund.Trim();
                if (t.Geruchsartikel is not null) suche.Geruchsartikel = t.Geruchsartikel.Trim();
            }
            return null;
        }

        private static string? Pruefen(EinsatzData einsatz, IList<Team> teams, AbschlussEingabe e,
            IReadOnlyList<string> abbruchgruende, DateTime nowUtc)
        {
            var texte = new[] { e.Vorgangsnummer, e.Melder, e.Polizei?.Name, e.Polizei?.Dienstnummer, e.Polizei?.Telefon,
                e.Fund?.Ort, e.Fund?.UebergabeAn }
                .Concat((e.Trails ?? Array.Empty<AbschlussTrail>()).Select(t => t.Geruchsartikel));
            if (texte.Any(t => t is { Length: > MaxTextLaenge }))
                return $"Texte höchstens {MaxTextLaenge} Zeichen.";
            if (!string.IsNullOrEmpty(e.AlarmiertDurch) && Models.Enums.AlarmiertDurchExtensions.AusApiWert(e.AlarmiertDurch) is null)
                return "alarmiertDurch muss Polizei, Feuerwehr, Rettungsdienst oder AndereStaffel sein.";

            if (e.Fund is { } f)
            {
                if (f.Lat is null != f.Lng is null || f.Lat is { } la && (!double.IsFinite(la) || Math.Abs(la) > 90)
                    || f.Lng is { } lo && (!double.IsFinite(lo) || Math.Abs(lo) > 180))
                    return "Fundort: lat und lng nur zusammen und im gültigen Bereich.";
                if (f.ZeitUtc is { } z && z > nowUtc.AddMinutes(1))
                    return "Fundzeit liegt in der Zukunft.";
                if (f.TeamId is { Length: > 0 } fid && teams.All(t => t.TeamId != fid))
                    return "Unbekanntes Team beim Fund.";
            }

            foreach (var t in e.Teams ?? Array.Empty<AbschlussTeam>())
            {
                if (teams.All(x => x.TeamId != t.TeamId)) return $"Unbekanntes Team {t.TeamId}.";
                if (t.Einsatzart is not null && EinsatzartExtensions.AusApiWert(t.Einsatzart) is null)
                    return $"Unbekannte Einsatzart {t.Einsatzart}.";
            }

            foreach (var t in e.Trails ?? Array.Empty<AbschlussTrail>())
            {
                if (einsatz.CompletedSearches?.FirstOrDefault(s => s.Id == t.SucheId) is not { } suche) return $"Unbekannte Suche {t.SucheId}.";
                // Ein gespeicherter Grund bleibt gültig, auch wenn er inzwischen aus der Liste entfernt wurde
                if (!string.IsNullOrWhiteSpace(t.Abbruchgrund) && !abbruchgruende.Contains(t.Abbruchgrund.Trim())
                    && t.Abbruchgrund.Trim() != suche.Abbruchgrund)
                    return "Abbruchgrund muss aus der Liste sein.";
            }
            return null;
        }
    }
}
