using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Angaben aus dem Abschlussdialog (Monitor oder EL-Seite /el/beenden).</summary>
public sealed record EinsatzAbschlussAnfrage(
    DateTime ErwarteterEinsatzBeginn,
    string Ergebnis,
    string Bemerkungen,
    TimeOnly? Endzeit,
    bool TracksImPdf,
    IReadOnlyList<string> PersonalVorOrt,
    IReadOnlyList<string> HundeVorOrt,
    Einsatzueberwachung.Domain.Models.Enums.MapTileType Kartenart = Einsatzueberwachung.Domain.Models.Enums.MapTileType.Streets,
    IReadOnlyCollection<string>? TeamIds = null,
    // Genauer Zeitpunkt des Endes in Ortszeit (EL-App); hat Vorrang vor Endzeit
    DateTime? Ende = null,
    // Kartenpunkte ins Archiv und in den Bericht (Variante C); sonst wird kein Punkt archiviert
    bool PunkteImBericht = false,
    // Angaben aus dem Beenden-Dialog (APP_API EL Abschnitt 10); werden vor dem Beenden gespeichert
    AbschlussEingabe? Angaben = null,
    // false: fehlen danach noch Angaben, wird nicht beendet (AbschlussUnvollstaendigException)
    bool UnvollstaendigBestaetigt = true,
    // Abschnitte des Berichts; null = alle
    IReadOnlyCollection<string>? Abschnitte = null);

/// <summary>Beenden abgelehnt, weil Angaben fehlen und die Einsatzleitung das nicht ausdrücklich bestätigt hat.</summary>
public sealed class AbschlussUnvollstaendigException(IReadOnlyList<string> fehlend)
    : InvalidOperationException("Es fehlen noch Angaben: " + string.Join(", ", fehlend))
{
    public IReadOnlyList<string> Fehlend { get; } = fehlend;
}

public sealed record EinsatzAbschlussErgebnis(ArchivedEinsatz Archiviert, PdfExportResult Pdf);

/// <summary>
/// Einsatz beenden, archivieren und als PDF ablegen. Gemeinsam fuer den Monitor und die EL-Ansicht, damit beide
/// gleich abschliessen. Mehrere Leitungsplaetze koennen gleichzeitig offen sein – der Einsatz wird nur einmal
/// abgeschlossen; wer zu spaet kommt, bekommt eine Meldung statt eines zweiten (leeren) Archiveintrags.
/// </summary>
public sealed class EinsatzAbschlussService
{
    public static readonly IReadOnlyList<string> ErgebnisOptionen = new[]
    {
        "Person gefunden - lebend",
        "Person gefunden - verstorben",
        "Person anderweitig gefunden",
        "Abbruch - Wetter",
        "Abbruch - Dunkelheit",
        "Suche erfolglos abgeschlossen",
        "Uebung abgeschlossen"
    };

    public const string BereitsBeendetMeldung = "Der Einsatz wurde inzwischen an einem anderen Platz beendet.";

    private readonly IEinsatzService _einsatzService;
    private readonly IArchivService _archivService;
    private readonly IPdfExportService _pdfExportService;
    private readonly ITimeService _timeService;
    private readonly ISettingsService? _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EinsatzAbschlussService(
        IEinsatzService einsatzService,
        IArchivService archivService,
        IPdfExportService pdfExportService,
        ITimeService timeService,
        ISettingsService? settings = null)
    {
        _settings = settings;
        _einsatzService = einsatzService;
        _archivService = archivService;
        _pdfExportService = pdfExportService;
        _timeService = timeService;
    }

    /// <summary>Prueft die Pflichtangaben; liefert die Fehlermeldung oder null.</summary>
    public static string? Validate(string ergebnis, bool ressourcenGeprueft)
    {
        if (string.IsNullOrWhiteSpace(ergebnis)) return "Bitte ein Ergebnis auswählen.";
        if (!ressourcenGeprueft) return "Bitte die Prüfung der Ressourcen bestätigen.";
        return null;
    }

    /// <exception cref="InvalidOperationException">Einsatz ist bereits beendet (anderer Leitungsplatz).</exception>
    public async Task<EinsatzAbschlussErgebnis> AbschliessenAsync(EinsatzAbschlussAnfrage anfrage)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _einsatzService.CurrentEinsatz;
            // Nach dem Abschluss legt ResetEinsatz einen neuen (leeren) Einsatz mit neuem Beginn an
            if (current.EinsatzDatum != anfrage.ErwarteterEinsatzBeginn)
            {
                throw new InvalidOperationException(BereitsBeendetMeldung);
            }

            if (anfrage.Angaben is { } angaben)
            {
                var gruende = AbschlussAngaben.Abbruchgruende(_settings is null ? null : await _settings.GetAppSettingsAsync());
                if (AbschlussAngaben.Anwenden(current, _einsatzService.Teams, angaben, gruende, DateTime.UtcNow) is { } fehler)
                    throw new ArgumentException(fehler);
            }
            if (!anfrage.UnvollstaendigBestaetigt
                && AbschlussAngaben.Fehlend(current, _einsatzService.Teams, anfrage.Ergebnis) is { Count: > 0 } fehlend)
                throw new AbschlussUnvollstaendigException(fehlend);

            await _einsatzService.EndEinsatzAsync();

            var snapshot = BuildArchiveSnapshot(_einsatzService);
            snapshot.EinsatzEnde = anfrage.Ende ?? ResolveEndTime(anfrage.Endzeit, _timeService.Now);
            snapshot.MapMarkers = anfrage.PunkteImBericht ? _einsatzService.CurrentEinsatz.MapMarkers.ToList() : new List<MapMarker>();

            var archived = await _archivService.ArchiveEinsatzAsync(
                snapshot,
                anfrage.Ergebnis,
                anfrage.Bemerkungen.Trim(),
                anfrage.PersonalVorOrt.ToList(),
                anfrage.HundeVorOrt.ToList());

            var pdf = await _pdfExportService.ExportArchivedEinsatzToPdfAsync(archived,
                anfrage.TracksImPdf ? new PdfBerichtOptionen(Kartenart: anfrage.Kartenart, TeamIds: anfrage.TeamIds, Abschnitte: anfrage.Abschnitte)
                    : new PdfBerichtOptionen(Tracks: false, Kartenart: anfrage.Kartenart, Abschnitte: anfrage.Abschnitte));

            _einsatzService.ResetEinsatz();
            return new EinsatzAbschlussErgebnis(archived, pdf);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gewaehlte Uhrzeit als Einsatzende. Liegt sie nach der aktuellen Uhrzeit, war sie gestern (Abschluss kurz nach
    /// Mitternacht, Ende z. B. 23:50) – ein Ende in der Zukunft gibt es nicht.
    /// </summary>
    public static DateTime ResolveEndTime(TimeOnly? endzeit, DateTime now)
    {
        if (!endzeit.HasValue) return now;
        var ende = now.Date.Add(endzeit.Value.ToTimeSpan());
        return ende > now ? ende.AddDays(-1) : ende;
    }

    /// <summary>Personal und Hunde aus Teamzuweisungen und Einsatz-Check-in fuer die Vor-Ort-Liste vorauswaehlen.</summary>
    public static (HashSet<string> PersonalIds, HashSet<string> HundeIds) PreselectVorOrt(
        IEnumerable<Team> teams,
        IEnumerable<PersonalEntry> personal,
        IEnumerable<DogEntry> hunde,
        IEnumerable<EinsatzResource>? eingecheckteRessourcen = null)
    {
        var personalListe = personal.ToList();
        var hundeListe = hunde.ToList();
        var personalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hundeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var personalIdsInListe = personalListe
            .Select(person => person.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hundeIdsInListe = hundeListe
            .Select(dog => dog.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var personnelByName = personalListe
            .Where(person => !string.IsNullOrWhiteSpace(person.FullName))
            .GroupBy(person => person.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        var dogsByName = hundeListe
            .Where(dog => !string.IsNullOrWhiteSpace(dog.Name))
            .GroupBy(dog => dog.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            if (!string.IsNullOrWhiteSpace(team.HundefuehrerName)
                && personnelByName.TryGetValue(team.HundefuehrerName.Trim(), out var fuehrerId))
            {
                personalIds.Add(fuehrerId);
            }

            foreach (var helferName in team.HelferNames)
            {
                if (!string.IsNullOrWhiteSpace(helferName)
                    && personnelByName.TryGetValue(helferName.Trim(), out var helferId))
                {
                    personalIds.Add(helferId);
                }
            }

            if (!string.IsNullOrWhiteSpace(team.DogName)
                && dogsByName.TryGetValue(team.DogName.Trim(), out var dogId))
            {
                hundeIds.Add(dogId);
            }
        }

        foreach (var resource in eingecheckteRessourcen ?? [])
        {
            if (string.IsNullOrWhiteSpace(resource.EntityId))
            {
                continue;
            }

            if (resource.EntityType == Einsatzueberwachung.Domain.Models.Enums.ResourceEntityType.Person
                && personalIdsInListe.Contains(resource.EntityId))
            {
                personalIds.Add(resource.EntityId);
            }
            else if (resource.EntityType == Einsatzueberwachung.Domain.Models.Enums.ResourceEntityType.Dog
                && hundeIdsInListe.Contains(resource.EntityId))
            {
                hundeIds.Add(resource.EntityId);
            }
        }

        return (personalIds, hundeIds);
    }

    public static EinsatzData BuildArchiveSnapshot(IEinsatzService einsatzService)
    {
        var current = einsatzService.CurrentEinsatz;
        return new EinsatzData
        {
            Einsatzleiter = current.Einsatzleiter,
            Fuehrungsassistent = current.Fuehrungsassistent,
            Alarmiert = current.Alarmiert,
            Stichwort = current.Stichwort,
            Szenario = current.Szenario,
            Einsatzort = current.Einsatzort,
            MapAddress = current.MapAddress,
            ExportPfad = current.ExportPfad,
            IstEinsatz = current.IstEinsatz,
            AnzahlTeams = einsatzService.Teams.Count,
            EinsatzDatum = current.EinsatzDatum,
            EinsatzEnde = current.EinsatzEnde,
            EinsatzNummer = current.EinsatzNummer,
            Vorgangsnummer = current.Vorgangsnummer,
            Fund = current.Fund,
            AlarmiertDurch = current.AlarmiertDurch,
            KeinePolizei = current.KeinePolizei,
            StaffelName = current.StaffelName,
            StaffelAdresse = current.StaffelAdresse,
            StaffelTelefon = current.StaffelTelefon,
            StaffelEmail = current.StaffelEmail,
            StaffelLogoPfad = current.StaffelLogoPfad,
            EinsatzkontakteMigriert = current.EinsatzkontakteMigriert,
            PolizeiKontaktName = current.PolizeiKontaktName,
            PolizeiDienstnummer = current.PolizeiDienstnummer,
            PolizeiTelefon = current.PolizeiTelefon,
            PolizeiVermisstenmeldungAufgenommen = current.PolizeiVermisstenmeldungAufgenommen,
            PolizeiKoordinationBesprochen = current.PolizeiKoordinationBesprochen,
            PolizeiSuchabschnittAbgestimmt = current.PolizeiSuchabschnittAbgestimmt,
            PolizeiRueckmeldepflichtVereinbart = current.PolizeiRueckmeldepflichtVereinbart,
            PolizeiDatenschutzGeklaert = current.PolizeiDatenschutzGeklaert,
            BosEinheit = current.BosEinheit,
            BosZugfuehrer = current.BosZugfuehrer,
            BosFunkrufname = current.BosFunkrufname,
            BosAufgabenteilung = current.BosAufgabenteilung,
            BosAbschnittAbgestimmt = current.BosAbschnittAbgestimmt,
            BosRessourcenBesprochen = current.BosRessourcenBesprochen,
            AlarmierungsZeit = current.AlarmierungsZeit,
            ElwPosition = current.ElwPosition,
            Teams = einsatzService.Teams.ToList(),
            SearchAreas = current.SearchAreas.ToList(),
            GlobalNotesEntries = einsatzService.GlobalNotes.ToList(),
            TrackSnapshots = current.TrackSnapshots.ToList(),
            // Szenario, abgeschlossene Suchen und Vermisste fehlten bis 1.10 im Archiv
            CompletedSearches = current.CompletedSearches?.ToList() ?? new List<CompletedSearch>(),
            Vermisste = current.Vermisste?.ToList() ?? new List<VermisstenInfo>()
        };
    }
}
