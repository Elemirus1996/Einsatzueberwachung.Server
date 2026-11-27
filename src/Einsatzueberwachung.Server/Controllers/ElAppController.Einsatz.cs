using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Native Einsatzleitung, Stufe 5: Einsatz starten und beenden wie /el/start und /el/beenden (docs/APP_API.md Abschnitte 6 und 7)</summary>
public sealed partial class ElAppController
{
    [HttpGet("einsatz/ergebnisse")]
    public IActionResult GetErgebnisse()
    {
        if (!Lesen()) return Zugang();
        return Ok(Ergebnisse());
    }

    /// <summary>Auswahllisten fuer den Start: offene Divera-Alarme, Einsatzleiter und Fuehrungsassistenten aus den Stammdaten</summary>
    [HttpGet("einsatz/vorlage")]
    public async Task<IActionResult> GetEinsatzVorlage()
    {
        if (!Lesen()) return Zugang();
        var personal = stammdaten is null ? [] : (await stammdaten.GetPersonalListAsync()).Where(p => p.IsActive)
            .OrderBy(p => p.Nachname).ThenBy(p => p.Vorname).ToList();
        List<Einsatzueberwachung.Domain.Models.Divera.DiveraAlarm> alarme = [];
        try { if (divera is { IsConfigured: true }) alarme = await divera.GetActiveAlarmsAsync(); }
        catch { /* Divera nicht erreichbar: Start von Hand geht trotzdem */ }

        return Ok(new
        {
            aktiv = ElPositionService.Laeuft(einsatz!.CurrentEinsatz),
            szenarien = new[] { EinsatzSzenarioType.Mantrailer, EinsatzSzenarioType.Flaeche, EinsatzSzenarioType.Truemmer, EinsatzSzenarioType.Sonstige }
                .Select(s => s.ToString()),
            divera = alarme.Select(a => new
            {
                id = a.Id, titel = a.Title, ort = a.Address, lat = a.Lat, lng = a.Lng,
                zeit = a.Date == default ? (DateTime?)null : AppZeit.Utc(a.Date)
            }),
            einsatzleiter = personal.Where(p => p.Skills.HasFlag(PersonalSkills.Einsatzleiter)).Select(p => p.FullName),
            fuehrungsassistenten = personal.Where(p => p.Skills.HasFlag(PersonalSkills.Fuehrungsassistent)).Select(p => p.FullName),
            ergebnisse = Ergebnisse(),
            alarmiertDurch = AlarmiertDurchExtensions.Alle.Select(x => new { wert = x.ToString(), anzeige = x.Anzeige() }),
            kartenarten = PdfBerichtOptionen.KartenartenFuer(EsriKacheln.Vorhanden(einstellungen is null ? null : (await einstellungen.GetAppSettingsAsync())?.EsriApiKey))
                .Select(k => new { wert = k.Art.ToString(), anzeige = k.Name })
        });
    }

    public sealed record ElEinsatzStart(string? Id, string? Stichwort, string? Ort, double? Lat, double? Lng, string? Szenario,
        bool Uebung, string? Einsatzleiter, string? Fuehrungsassistent, int? DiveraAlarmId,
        string? AlarmiertDurch = null, string? Melder = null);

    [HttpPost("einsatz/start")]
    public Task<IActionResult> EinsatzStarten([FromBody] ElEinsatzStart? anfrage) => Aktion(anfrage?.Id, async () =>
    {
        var a = anfrage!;
        if (!Enum.TryParse<EinsatzSzenarioType>(a.Szenario, ignoreCase: false, out var szenario) || int.TryParse(a.Szenario, out _)
            || !Enum.IsDefined(szenario))
            return BadRequest(new { error = "Bitte ein Szenario auswählen.", szenarien = Enum.GetNames<EinsatzSzenarioType>() });
        // Ohne Feld (Apps vor 1.17.0) bleibt die Art leer und wird beim Einsatzende abgefragt
        var alarmiertDurch = AlarmiertDurchExtensions.AusApiWert(a.AlarmiertDurch);
        if (a.AlarmiertDurch is not null && alarmiertDurch is null)
            return BadRequest(new { error = "Bitte auswählen, wer alarmiert hat.", alarmiertDurch = AlarmiertDurchExtensions.Alle.Select(x => x.ToString()) });
        if (a.Melder is { Length: > AbschlussAngaben.MaxTextLaenge }) return BadRequest(new { error = $"melder höchstens {AbschlussAngaben.MaxTextLaenge} Zeichen." });
        if (a.Lat is { } lat1 && (Math.Abs(lat1) > 90 || a.Lng is not { } lng1 || Math.Abs(lng1) > 180) || a.Lat is null && a.Lng is not null)
            return BadRequest(new { error = "Ungültige Koordinate." });

        var modell = new EinsatzData();
        if (einstellungen is not null) EinsatzStartHelper.ApplyStaffel(modell, await einstellungen.GetStaffelSettingsAsync());
        var koordinate = false;
        if (a.DiveraAlarmId is { } alarmId)
        {
            var alarm = divera is { IsConfigured: true } ? await divera.GetAlarmByIdAsync(alarmId) : null;
            if (alarm is null) return NotFound(new { error = "Divera-Alarm nicht gefunden." });
            koordinate = EinsatzStartHelper.ApplyDiveraAlarm(modell, alarm);
        }

        // Angaben der App gehen vor dem Divera-Alarm, leere Angaben lassen ihn stehen
        if (!string.IsNullOrWhiteSpace(a.Ort)) modell.Einsatzort = a.Ort.Trim();
        if (!string.IsNullOrWhiteSpace(a.Stichwort)) modell.Stichwort = a.Stichwort.Trim();
        if (!string.IsNullOrWhiteSpace(a.Einsatzleiter)) modell.Einsatzleiter = a.Einsatzleiter.Trim();
        if (!string.IsNullOrWhiteSpace(a.Fuehrungsassistent)) modell.Fuehrungsassistent = a.Fuehrungsassistent.Trim();
        if (!string.IsNullOrWhiteSpace(a.Melder)) modell.Alarmiert = a.Melder.Trim();
        modell.AlarmiertDurch = alarmiertDurch;
        if (a.Lat is { } lat && a.Lng is { } lng) { EinsatzStartHelper.ApplyCoordinates(modell, lat, lng); koordinate = true; }
        modell.Szenario = szenario;
        modell.IstEinsatz = !a.Uebung;
        if (EinsatzStartHelper.Validate(modell, artPflicht: false) is { } fehler) return BadRequest(new { error = fehler });

        var jetzt = zeit?.Now ?? Ortszeit.Jetzt;
        modell.EinsatzDatum = jetzt;
        if (modell.AlarmierungsZeit is null || a.DiveraAlarmId is null) modell.AlarmierungsZeit = jetzt;
        if (!koordinate || string.IsNullOrWhiteSpace(modell.MapAddress)) modell.MapAddress = modell.Einsatzort;

        await einsatz!.StartEinsatzAsync(modell);
        audit?.Log(modell.IstEinsatz ? "Einsatz gestartet (EL-App)" : "Übung gestartet (EL-App)", $"{Geraet(null)}: {modell.Einsatzort}");
        return Ok(new { doppelt = false, einsatz = new { aktiv = true, beginn = AppZeit.Utc(modell.EinsatzDatum) } });
    }, laufenderEinsatzNoetig: false);

    public sealed record ElPdfWahl(bool Tracks = true, string? Kartenart = null, IReadOnlyList<string>? TeamIds = null, bool Punkte = false,
        IReadOnlyList<string>? Abschnitte = null);

    public sealed record ElAbschlussFund(string? Zeit, double? Lat, double? Lng, string? Ort, string? UebergabeAn, string? TeamId);

    public sealed record ElAbschluss(string? Vorgangsnummer, string? Melder, AbschlussPolizei? Polizei, ElAbschlussFund? Fund,
        IReadOnlyList<AbschlussTeam>? Teams, IReadOnlyList<AbschlussTrail>? Trails, string? AlarmiertDurch = null, bool? KeinePolizei = null);

    public sealed record ElEinsatzEnde(string? Id, string? Ergebnis, bool RessourcenGeprueft, string? Bemerkungen = null,
        string? Endzeit = null, ElPdfWahl? Pdf = null, IReadOnlyList<string>? PersonalIds = null, IReadOnlyList<string>? HundeIds = null,
        ElAbschluss? Abschluss = null, bool? UnvollstaendigBestaetigt = null);

    /// <summary>Alles, was der Beenden-Dialog abfragt, mit den aktuellen Werten (APP_API EL Abschnitt 10)</summary>
    [HttpGet("einsatz/abschluss")]
    public async Task<IActionResult> GetAbschluss()
    {
        if (!Lesen()) return Zugang();
        var e = einsatz!.CurrentEinsatz;
        if (!ElPositionService.Laeuft(e)) return Konflikt("Kein laufender Einsatz.");
        var teams = einsatz.Teams.ToList();
        var hunde = stammdaten is null ? [] : await stammdaten.GetDogListAsync();
        var fund = e.Fund ?? new EinsatzFund();
        var vorschlag = AbschlussAngaben.FundVorschlag(e);

        return Ok(new
        {
            uebung = !e.IstEinsatz,
            alarmiertDurch = e.AlarmiertDurch?.ToString(),
            keinePolizei = e.KeinePolizei,
            polizeiGefragt = AbschlussAngaben.PolizeiGefragt(e),
            vorgangsnummer = e.Vorgangsnummer ?? "",
            melder = AbschlussAngaben.Melder(e.Alarmiert),
            polizei = new { name = e.PolizeiKontaktName ?? "", dienstnummer = e.PolizeiDienstnummer ?? "", telefon = e.PolizeiTelefon ?? "" },
            fund = new
            {
                zeit = fund.ZeitUtc is { } z ? AppZeit.Utc(DateTime.SpecifyKind(z, DateTimeKind.Utc)) : (DateTime?)null,
                lat = fund.Latitude, lng = fund.Longitude, ort = fund.Ort ?? "", uebergabeAn = fund.UebergabeAn ?? "", teamId = fund.TeamId,
                vorschlag = vorschlag is null ? null : new
                {
                    lat = vorschlag.Latitude, lng = vorschlag.Longitude, zeit = AppZeit.Utc(vorschlag.CreatedAt), teamId = vorschlag.TeamId
                }
            },
            teams = AbschlussAngaben.Hundeteams(teams).Select(t => new
            {
                teamId = t.TeamId,
                teamName = t.TeamName,
                einsatzart = t.Einsatzart?.ToString(),
                erlaubt = EinsatzartExtensions.AusSpezialisierungen(
                    hunde.FirstOrDefault(d => d.Id == t.DogId)?.Specializations ?? t.DogSpecialization).Select(a => a.ToString())
            }),
            trails = AbschlussAngaben.Trails(e, teams).Select(s => new
            {
                sucheId = s.Id, teamId = s.TeamId, teamName = s.TeamName,
                beginn = AppZeit.Utc(s.SearchStart), ende = AppZeit.Utc(s.SearchEnd),
                abbruchgrund = s.Abbruchgrund ?? "", geruchsartikel = s.Geruchsartikel ?? ""
            }),
            abbruchgruende = AbschlussAngaben.Abbruchgruende(einstellungen is null ? null : await einstellungen.GetAppSettingsAsync()),
            fehlend = AbschlussAngaben.Fehlend(e, teams)
        });
    }

    /// <summary>Aktives Personal und aktive Hunde wie in /el/beenden, sortiert, mit Vorauswahl aus Teams und Einsatz-Check-in</summary>
    private async Task<(List<PersonalEntry> Personal, List<DogEntry> Hunde, HashSet<string> PersonalIds, HashSet<string> HundeIds)> VorOrtListen()
    {
        var personal = stammdaten is null ? [] : Components.Pages.SelectionListOrdering
            .OrderPersonnelByFirstName((await stammdaten.GetPersonalListAsync()).Where(p => p.IsActive)).ToList();
        var hunde = stammdaten is null ? [] : (await stammdaten.GetDogListAsync()).Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
        var (personalIds, hundeIds) = EinsatzAbschlussService.PreselectVorOrt(
            einsatz!.Teams,
            personal,
            hunde,
            einsatz.GetEinsatzResources());
        return (personal, hunde, personalIds, hundeIds);
    }

    [HttpGet("einsatz/vorort")]
    public async Task<IActionResult> GetVorOrt()
    {
        if (!Lesen()) return Zugang();
        var (personal, hunde, personalIds, hundeIds) = await VorOrtListen();
        return Ok(new
        {
            personal = personal.Select(p => new { id = p.Id, name = p.FullName, vorausgewaehlt = personalIds.Contains(p.Id) }),
            hunde = hunde.Select(d => new { id = d.Id, name = d.Name, vorausgewaehlt = hundeIds.Contains(d.Id) })
        });
    }

    [HttpPost("einsatz/beenden")]
    public Task<IActionResult> EinsatzBeenden([FromBody] ElEinsatzEnde? anfrage) => Aktion(anfrage?.Id, async () =>
    {
        var a = anfrage!;
        if (abschluss is null) return NotFound();
        if (a.Ergebnis is null || !EinsatzAbschlussService.ErgebnisOptionen.Contains(a.Ergebnis))
            return BadRequest(new { error = "Bitte ein Ergebnis auswählen.", ergebnisse = EinsatzAbschlussService.ErgebnisOptionen });
        if (EinsatzAbschlussService.Validate(a.Ergebnis, a.RessourcenGeprueft) is { } fehler) return BadRequest(new { error = fehler });
        var kartenart = MapTileType.Streets;
        if (a.Pdf?.Kartenart is { } wahl && !(Enum.TryParse(wahl, ignoreCase: false, out kartenart)
            && !int.TryParse(wahl, out _) && PdfBerichtOptionen.Kartenarten.Any(k => k.Art == kartenart)))
            return BadRequest(new { error = "Unbekannte Kartenart.", kartenarten = PdfBerichtOptionen.Kartenarten.Select(k => k.Art.ToString()) });
        if (a.Pdf?.Abschnitte?.FirstOrDefault(x => !PdfBerichtOptionen.AlleAbschnitte.Contains(x)) is { } unbekannt)
            return BadRequest(new { error = $"Unbekannter Abschnitt {unbekannt}.", abschnitte = PdfBerichtOptionen.AlleAbschnitte });
        AbschlussEingabe? angaben = null;
        if (a.Abschluss is { } ab)
        {
            DateTime? fundZeit = null;
            if (!string.IsNullOrWhiteSpace(ab.Fund?.Zeit))
            {
                if (!MitZeitzone(ab.Fund.Zeit) || !DateTimeOffset.TryParse(ab.Fund.Zeit, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fz))
                    return BadRequest(new { error = "fund.zeit als Zeitpunkt in UTC, z. B. „2026-10-31T17:45:00Z“." });
                fundZeit = fz.UtcDateTime;
            }
            angaben = new AbschlussEingabe(ab.Vorgangsnummer, ab.Melder, ab.Polizei,
                ab.Fund is { } f ? new AbschlussFund(fundZeit, f.Lat, f.Lng, f.Ort, f.UebergabeAn, f.TeamId) : null,
                ab.Teams, ab.Trails, ab.AlarmiertDurch, ab.KeinePolizei);
        }

        // Ende als Zeitpunkt in UTC (wie alle Zeiten der App-Schnittstelle); fuer den Bericht in Ortszeit
        DateTime? ende = null;
        if (!string.IsNullOrWhiteSpace(a.Endzeit))
        {
            if (!MitZeitzone(a.Endzeit) || !DateTimeOffset.TryParse(a.Endzeit, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var zeitpunkt))
                return BadRequest(new { error = "endzeit als Zeitpunkt in UTC, z. B. „2026-10-31T17:45:00Z“." });
            ende = Ortszeit.AusUtc(zeitpunkt.UtcDateTime);
            if (ende > (zeit?.Now ?? Ortszeit.Jetzt).AddMinutes(1)) return BadRequest(new { error = "endzeit liegt in der Zukunft." });
            if (ende < einsatz!.CurrentEinsatz.EinsatzDatum) return BadRequest(new { error = "endzeit liegt vor dem Einsatzbeginn." });
        }

        // Vor-Ort-Liste: Auswahl der App, ohne Angabe die Vorauswahl aus den eingeteilten Teams wie in /el/beenden.
        // Unbekannte oder inaktive IDs fallen weg.
        var (personal, hunde, personalIds, hundeIds) = await VorOrtListen();
        if (a.PersonalIds is not null) personalIds = new HashSet<string>(a.PersonalIds, StringComparer.OrdinalIgnoreCase);
        if (a.HundeIds is not null) hundeIds = new HashSet<string>(a.HundeIds, StringComparer.OrdinalIgnoreCase);

        EinsatzAbschlussErgebnis ergebnis;
        try
        {
            ergebnis = await abschluss.AbschliessenAsync(new EinsatzAbschlussAnfrage(
                einsatz!.CurrentEinsatz.EinsatzDatum,
                a.Ergebnis,
                a.Bemerkungen ?? "",
                null,
                a.Pdf?.Tracks ?? true,
                personal.Where(p => personalIds.Contains(p.Id)).Select(p => p.FullName).Where(n => n.Length > 0).Distinct().OrderBy(n => n).ToList(),
                hunde.Where(d => hundeIds.Contains(d.Id)).Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList(),
                kartenart,
                a.Pdf?.TeamIds,
                ende,
                a.Pdf?.Punkte ?? false,
                angaben,
                // Ohne das Feld (Apps vor 1.17.0) beendet der Server wie bisher
                a.UnvollstaendigBestaetigt ?? true,
                a.Pdf?.Abschnitte));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (AbschlussUnvollstaendigException ex)
        {
            return Conflict(new { error = ex.Message, fehlend = ex.Fehlend });
        }
        catch (InvalidOperationException ex)
        {
            return Konflikt(ex.Message); // inzwischen an einem anderen Leitungsplatz beendet
        }

        audit?.Log("Einsatz beendet (EL-App)", $"{Geraet(null)}: {a.Ergebnis}");
        return Ok(new
        {
            doppelt = false,
            archivId = ergebnis.Archiviert.Id,
            einsatzNummer = ergebnis.Archiviert.EinsatzNummer,
            pdf = new { erstellt = ergebnis.Pdf.Success, fehler = ergebnis.Pdf.Success ? null : ergebnis.Pdf.ErrorMessage }
        });
    });

    /// <summary>Bericht eines beendeten Einsatzes als PDF wie der Download im Archiv – zum Öffnen und Teilen in der App (Idee 6.2)</summary>
    [HttpGet("einsatz/bericht/{archivId}")]
    public async Task<IActionResult> GetBericht(string archivId, string? karte = null, string? teams = null, string? tracks = null,
        string? abschnitte = null)
    {
        if (!Lesen()) return Zugang();
        if (archiv is null || pdfExport is null) return NotFound();
        var archiviert = await archiv.GetByIdAsync(archivId);
        if (archiviert is null) return NotFound(new { error = "Bericht nicht gefunden." });

        byte[] bytes;
        try
        {
            bytes = await pdfExport.ExportArchivedEinsatzToPdfBytesAsync(archiviert,
                Extensions.DownloadEndpoints.BerichtOptionenAusAdresse(archiviert, karte, teams, tracks, abschnitte));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Einsatzbericht für die EL-App konnte nicht erstellt werden");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Der Bericht konnte nicht erstellt werden." });
        }

        audit?.Log("Einsatzbericht abgerufen (EL-App)", $"{Geraet(null)}: {archiviert.EinsatzNummer}");
        var name = string.IsNullOrWhiteSpace(archiviert.EinsatzNummer)
            ? $"einsatzbericht-{archiviert.EinsatzDatum:yyyyMMdd-HHmm}"
            : $"einsatzbericht-{archiviert.EinsatzNummer}";
        return File(bytes, "application/pdf", name + ".pdf");
    }

    private static bool MitZeitzone(string text) =>
        text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:\d{2}$");

    private static IEnumerable<object> Ergebnisse() =>
        EinsatzAbschlussService.ErgebnisOptionen.Select(o => new { wert = o, anzeige = ErgebnisText.Anzeige(o), fund = ErgebnisText.IstFund(o) });
}
