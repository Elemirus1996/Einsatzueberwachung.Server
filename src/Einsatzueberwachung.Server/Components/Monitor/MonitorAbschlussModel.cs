using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Components.Monitor;

/// <summary>
/// Angaben für den Einsatzbericht im Beenden-Dialog des Monitors (gleiche Regeln wie APP_API EL Abschnitt 10).
/// Wird beim Öffnen aus dem laufenden Einsatz gefüllt und erst beim Beenden übernommen.
/// </summary>
public sealed class MonitorAbschlussModel
{
    public sealed class TeamZeile
    {
        public string TeamId { get; init; } = "";
        public string TeamName { get; init; } = "";
        public List<Einsatzart> Erlaubt { get; init; } = new();
        public string Einsatzart { get; set; } = "";
    }

    public sealed class TrailZeile
    {
        public string SucheId { get; init; } = "";
        public string Text { get; init; } = "";
        public string Abbruchgrund { get; set; } = "";
        public string Geruchsartikel { get; set; } = "";
    }

    /// <summary>Abwählbare Abschnitte des Berichts (Tracks steuert der eigene Haken im Dialog)</summary>
    public static readonly IReadOnlyList<(string Wert, string Anzeige)> AbschnittOptionen =
    [
        ("vermisste", "Vermisste Person"),
        ("zeitleiste", "Zeitlicher Ablauf"),
        ("teams", "Suche je Team und Trails"),
        ("karten", "Karten"),
        ("funk", "Funk und Notizen"),
        ("vorort", "Vor Ort (Personal und Hunde)")
    ];

    public bool IstEinsatz { get; private set; } = true;
    /// <summary>Name der Art („Polizei“ …) oder leer</summary>
    public string AlarmiertDurch { get; set; } = "";
    public bool KeinePolizei { get; set; }

    /// <summary>Polizeiangaben gefragt: Einsatz, von der Polizei alarmiert, nicht „Keine Polizei beteiligt“</summary>
    public bool PolizeiGefragt => IstEinsatz && AlarmiertDurch == nameof(Einsatzueberwachung.Domain.Models.Enums.AlarmiertDurch.Polizei) && !KeinePolizei;

    public string Vorgangsnummer { get; set; } = "";
    public string Melder { get; set; } = "";
    public string PolizeiName { get; set; } = "";
    public string PolizeiDienstnummer { get; set; } = "";
    public string PolizeiTelefon { get; set; } = "";

    public TimeOnly? FundZeit { get; set; }
    public string FundOrt { get; set; } = "";
    public double? FundLat { get; set; }
    public double? FundLng { get; set; }
    public string FundTeamId { get; set; } = "";
    public string UebergabeAn { get; set; } = "";
    public MapMarker? FundVorschlag { get; private set; }

    public List<TeamZeile> Teams { get; } = new();
    public List<TrailZeile> Trails { get; } = new();
    public IReadOnlyList<string> Abbruchgruende { get; private set; } = AbschlussAngaben.StandardAbbruchgruende;
    public HashSet<string> Abschnitte { get; } = new(AbschnittOptionen.Select(a => a.Wert));
    public bool UnvollstaendigBestaetigt { get; set; }

    public void Laden(EinsatzData einsatz, IReadOnlyList<Team> teams, IEnumerable<DogEntry> hunde, IReadOnlyList<string> abbruchgruende)
    {
        IstEinsatz = einsatz.IstEinsatz;
        AlarmiertDurch = einsatz.AlarmiertDurch?.ToString() ?? "";
        KeinePolizei = einsatz.KeinePolizei;
        Vorgangsnummer = einsatz.Vorgangsnummer ?? "";
        Melder = AbschlussAngaben.Melder(einsatz.Alarmiert);
        PolizeiName = einsatz.PolizeiKontaktName ?? "";
        PolizeiDienstnummer = einsatz.PolizeiDienstnummer ?? "";
        PolizeiTelefon = einsatz.PolizeiTelefon ?? "";

        var fund = einsatz.Fund ?? new EinsatzFund();
        FundZeit = fund.ZeitUtc is { } z ? TimeOnly.FromDateTime(Ortszeit.AusUtc(DateTime.SpecifyKind(z, DateTimeKind.Utc))) : null;
        FundOrt = fund.Ort ?? "";
        FundLat = fund.Latitude;
        FundLng = fund.Longitude;
        FundTeamId = fund.TeamId ?? "";
        UebergabeAn = fund.UebergabeAn ?? "";
        FundVorschlag = AbschlussAngaben.FundVorschlag(einsatz);

        var hundeListe = hunde.ToList();
        Teams.Clear();
        foreach (var t in AbschlussAngaben.Hundeteams(teams))
        {
            var erlaubt = EinsatzartExtensions.AusSpezialisierungen(
                hundeListe.FirstOrDefault(d => d.Id == t.DogId)?.Specializations ?? t.DogSpecialization);
            if (erlaubt.Count == 0) erlaubt = EinsatzartExtensions.Alle.ToList();
            if (t.Einsatzart is { } art && !erlaubt.Contains(art)) erlaubt.Add(art);
            Teams.Add(new TeamZeile { TeamId = t.TeamId, TeamName = t.TeamName, Erlaubt = erlaubt, Einsatzart = t.Einsatzart?.ToString() ?? "" });
        }

        Trails.Clear();
        foreach (var s in AbschlussAngaben.Trails(einsatz, teams))
            Trails.Add(new TrailZeile
            {
                SucheId = s.Id,
                Text = $"{s.TeamName}, {Ortszeit.AusUtc(s.SearchStart):HH:mm}–{Ortszeit.AusUtc(s.SearchEnd):HH:mm}",
                Abbruchgrund = s.Abbruchgrund ?? "",
                Geruchsartikel = s.Geruchsartikel ?? ""
            });

        Abbruchgruende = abbruchgruende;
        Abschnitte.Clear();
        Abschnitte.UnionWith(AbschnittOptionen.Select(a => a.Wert));
        UnvollstaendigBestaetigt = false;
    }

    public void VorschlagUebernehmen()
    {
        if (FundVorschlag is not { } p) return;
        FundLat = p.Latitude;
        FundLng = p.Longitude;
        FundZeit ??= TimeOnly.FromDateTime(p.CreatedAt);
        if (!string.IsNullOrWhiteSpace(p.TeamId)) FundTeamId = p.TeamId;
    }

    /// <summary>Fehlende Angaben in Worten, gleiche Regeln wie AbschlussAngaben.Fehlend</summary>
    public List<string> Fehlend(string? ergebnis)
    {
        var fehlend = new List<string>();
        if (IstEinsatz && AlarmiertDurch.Length == 0) fehlend.Add("Alarmiert durch");
        if (PolizeiGefragt && string.IsNullOrWhiteSpace(Vorgangsnummer)) fehlend.Add("Vorgangsnummer der Polizei");
        if (PolizeiGefragt && string.IsNullOrWhiteSpace(PolizeiName)) fehlend.Add("Polizeikontakt");
        if (ErgebnisText.IstFund(ergebnis) && (FundZeit is null || FundLat is null && string.IsNullOrWhiteSpace(FundOrt)))
            fehlend.Add("Fundzeit und Fundort");
        if (Teams.Any(t => t.Einsatzart.Length == 0)) fehlend.Add("„Eingesetzt als“ je Team");
        if (Trails.Any(t => string.IsNullOrWhiteSpace(t.Abbruchgrund) || string.IsNullOrWhiteSpace(t.Geruchsartikel)))
            fehlend.Add("Abbruchgrund und Geruchsartikel je Trail");
        return fehlend;
    }

    /// <summary>Fundzeit liegt vor dem Ende; eine Uhrzeit nach „jetzt“ war am Vortag (wie die Ende-Zeit).</summary>
    public AbschlussEingabe ZuEingabe(DateTime jetztOrtszeit, string? ergebnis)
    {
        AbschlussFund? fund = null;
        if (ErgebnisText.IstFund(ergebnis))
        {
            DateTime? zeitUtc = null;
            if (FundZeit is { } z)
            {
                var lokal = jetztOrtszeit.Date + z.ToTimeSpan();
                if (lokal > jetztOrtszeit.AddMinutes(1)) lokal = lokal.AddDays(-1);
                zeitUtc = Ortszeit.NachUtc(lokal);
            }
            fund = new AbschlussFund(zeitUtc, FundLat, FundLng, FundOrt, UebergabeAn, FundTeamId);
        }

        return new AbschlussEingabe(
            Vorgangsnummer, Melder, new AbschlussPolizei(PolizeiName, PolizeiDienstnummer, PolizeiTelefon), fund,
            Teams.Select(t => new AbschlussTeam(t.TeamId, t.Einsatzart.Length == 0 ? null : t.Einsatzart)).ToList(),
            Trails.Select(t => new AbschlussTrail(t.SucheId, t.Abbruchgrund, t.Geruchsartikel)).ToList(),
            AlarmiertDurch.Length == 0 ? null : AlarmiertDurch,
            KeinePolizei);
    }

    public List<string> GewaehlteAbschnitte(bool tracks)
    {
        var liste = AbschnittOptionen.Select(a => a.Wert).Where(Abschnitte.Contains).ToList();
        if (tracks) liste.Add("tracks");
        return liste;
    }
}
