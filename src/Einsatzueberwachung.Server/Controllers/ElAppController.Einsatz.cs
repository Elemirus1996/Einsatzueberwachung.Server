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
            kartenarten = PdfBerichtOptionen.Kartenarten.Select(k => new { wert = k.Art.ToString(), anzeige = k.Name })
        });
    }

    public sealed record ElEinsatzStart(string? Id, string? Stichwort, string? Ort, double? Lat, double? Lng, string? Szenario,
        bool Uebung, string? Einsatzleiter, string? Fuehrungsassistent, int? DiveraAlarmId);

    [HttpPost("einsatz/start")]
    public Task<IActionResult> EinsatzStarten([FromBody] ElEinsatzStart? anfrage) => Aktion(anfrage?.Id, async () =>
    {
        var a = anfrage!;
        if (!Enum.TryParse<EinsatzSzenarioType>(a.Szenario, ignoreCase: false, out var szenario) || int.TryParse(a.Szenario, out _)
            || !Enum.IsDefined(szenario))
            return BadRequest(new { error = "Bitte ein Szenario auswählen.", szenarien = Enum.GetNames<EinsatzSzenarioType>() });
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
        if (a.Lat is { } lat && a.Lng is { } lng) { EinsatzStartHelper.ApplyCoordinates(modell, lat, lng); koordinate = true; }
        modell.Szenario = szenario;
        modell.IstEinsatz = !a.Uebung;
        if (EinsatzStartHelper.Validate(modell) is { } fehler) return BadRequest(new { error = fehler });

        var jetzt = zeit?.Now ?? Ortszeit.Jetzt;
        modell.EinsatzDatum = jetzt;
        if (modell.AlarmierungsZeit is null || a.DiveraAlarmId is null) modell.AlarmierungsZeit = jetzt;
        if (!koordinate || string.IsNullOrWhiteSpace(modell.MapAddress)) modell.MapAddress = modell.Einsatzort;

        await einsatz!.StartEinsatzAsync(modell);
        audit?.Log(modell.IstEinsatz ? "Einsatz gestartet (EL-App)" : "Übung gestartet (EL-App)", $"{Geraet(null)}: {modell.Einsatzort}");
        return Ok(new { doppelt = false, einsatz = new { aktiv = true, beginn = AppZeit.Utc(modell.EinsatzDatum) } });
    }, laufenderEinsatzNoetig: false);

    public sealed record ElPdfWahl(bool Tracks = true, string? Kartenart = null, IReadOnlyList<string>? TeamIds = null);

    public sealed record ElEinsatzEnde(string? Id, string? Ergebnis, bool RessourcenGeprueft, string? Bemerkungen = null,
        string? Endzeit = null, ElPdfWahl? Pdf = null);

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

        // Vor-Ort-Liste wie die Vorauswahl in /el/beenden: Personal und Hunde aus den eingeteilten Teams
        var personal = stammdaten is null ? [] : (await stammdaten.GetPersonalListAsync()).Where(p => p.IsActive).ToList();
        var hunde = stammdaten is null ? [] : (await stammdaten.GetDogListAsync()).Where(d => d.IsActive).ToList();
        var (personalIds, hundeIds) = EinsatzAbschlussService.PreselectVorOrt(einsatz!.Teams, personal, hunde);

        EinsatzAbschlussErgebnis ergebnis;
        try
        {
            ergebnis = await abschluss.AbschliessenAsync(new EinsatzAbschlussAnfrage(
                einsatz.CurrentEinsatz.EinsatzDatum,
                a.Ergebnis,
                a.Bemerkungen ?? "",
                null,
                a.Pdf?.Tracks ?? true,
                personal.Where(p => personalIds.Contains(p.Id)).Select(p => p.FullName).Where(n => n.Length > 0).Distinct().OrderBy(n => n).ToList(),
                hunde.Where(d => hundeIds.Contains(d.Id)).Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList(),
                kartenart,
                a.Pdf?.TeamIds,
                ende));
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

    private static bool MitZeitzone(string text) =>
        text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:\d{2}$");

    private static IEnumerable<object> Ergebnisse() =>
        EinsatzAbschlussService.ErgebnisOptionen.Select(o => new { wert = o, anzeige = ErgebnisText.Anzeige(o) });
}
