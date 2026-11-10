using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{    private void OpenNewTeamModal()
    {
        ResetTeamForm();
        ClearTeamFormMessage();
        _showTeamModal = true;
    }

    private void CloseTeamModal()
    {
        _showTeamModal = false;
        CancelTeamEdit();
    }

    private void CancelTeamEdit()
    {
        ResetTeamForm();
        ClearTeamFormMessage();
    }

    private void ResetTeamForm()
    {
        _editingTeamId = null;
        _teamForm.TeamType = "dog";
        _teamForm.TeamName = string.Empty;
        _teamForm.DogId = string.Empty;
        _teamForm.DroneId = string.Empty;
        _teamForm.HundefuehrerId = string.Empty;
        _teamForm.HelferId = string.Empty;
        _teamForm.HelferId2 = string.Empty;
        _teamForm.HelferId3 = string.Empty;
        _teamForm.SearchAreaId = string.Empty;
        _teamForm.CollarId = string.Empty;
        _teamForm.FirstWarningMinutes = _appSettings.DefaultFirstWarningMinutes;
        _teamForm.SecondWarningMinutes = _appSettings.DefaultSecondWarningMinutes;
        _teamForm.Notes = string.Empty;
    }

    private void SetTeamFormMessage(string message, bool isError)
    {
        _teamFormMessage = message;
        _teamFormIsError = isError;
    }

    private void ClearTeamFormMessage()
    {
        _teamFormMessage = string.Empty;
        _teamFormIsError = false;
    }

    internal sealed class TeamEditorModel : MonitorTeamEditorModel
    {
    }

    private void OpenEditEinsatzModal()
    {
        PopulateEditEinsatzForm();
        _showEditEinsatzModal = true;
    }

    private void PopulateEditEinsatzForm()
    {
        var e = EinsatzService.CurrentEinsatz;
        var legacyContacts = e.EinsatzkontakteMigriert ? new List<VermisstenInfo>() : e.Vermisste ?? new();
        _editEinsatzForm.IstEinsatz = e.IstEinsatz;
        _editEinsatzForm.EinsatzNummer = e.EinsatzNummer;
        _editEinsatzForm.PolizeiKontaktName = FirstNonEmpty(e.PolizeiKontaktName, legacyContacts.Select(person => person.PolizeiKontaktName));
        _editEinsatzForm.PolizeiDienstnummer = FirstNonEmpty(e.PolizeiDienstnummer, legacyContacts.Select(person => person.PolizeiDienstnummer));
        _editEinsatzForm.PolizeiTelefon = FirstNonEmpty(e.PolizeiTelefon, legacyContacts.Select(person => person.PolizeiTelefon));
        _editEinsatzForm.PolizeiVermisstenmeldungAufgenommen = e.PolizeiVermisstenmeldungAufgenommen || legacyContacts.Any(person => person.PolizeiVermisstenmeldungAufgenommen);
        _editEinsatzForm.PolizeiKoordinationBesprochen = e.PolizeiKoordinationBesprochen || legacyContacts.Any(person => person.PolizeiKoordinationBesprochen);
        _editEinsatzForm.PolizeiSuchabschnittAbgestimmt = e.PolizeiSuchabschnittAbgestimmt || legacyContacts.Any(person => person.PolizeiSuchabschnittAbgestimmt);
        _editEinsatzForm.PolizeiRueckmeldepflichtVereinbart = e.PolizeiRueckmeldepflichtVereinbart || legacyContacts.Any(person => person.PolizeiRueckmeldepflichtVereinbart);
        _editEinsatzForm.PolizeiDatenschutzGeklaert = e.PolizeiDatenschutzGeklaert || legacyContacts.Any(person => person.PolizeiDatenschutzGeklaert);
        _editEinsatzForm.BosEinheit = FirstNonEmpty(e.BosEinheit, legacyContacts.Select(person => person.BosEinheit));
        _editEinsatzForm.BosZugfuehrer = FirstNonEmpty(e.BosZugfuehrer, legacyContacts.Select(person => person.BosZugfuehrer));
        _editEinsatzForm.BosFunkrufname = FirstNonEmpty(e.BosFunkrufname, legacyContacts.Select(person => person.BosFunkrufname));
        _editEinsatzForm.BosAufgabenteilung = FirstNonEmpty(e.BosAufgabenteilung, legacyContacts.Select(person => person.BosAufgabenteilung));
        _editEinsatzForm.BosAbschnittAbgestimmt = e.BosAbschnittAbgestimmt || legacyContacts.Any(person => person.BosAbschnittAbgestimmt);
        _editEinsatzForm.BosRessourcenBesprochen = e.BosRessourcenBesprochen || legacyContacts.Any(person => person.BosRessourcenBesprochen);
        _editEinsatzForm.AlarmierungsZeit = e.AlarmierungsZeit?.ToString("HH:mm") ?? string.Empty;
        _editEinsatzForm.Einsatzort = e.Einsatzort;
        _editEinsatzForm.MapAddress = e.MapAddress;
        _editEinsatzForm.Alarmiert = e.Alarmiert;
        _editEinsatzForm.AnzahlTeams = e.AnzahlTeams;
        _editEinsatzForm.Einsatzleiter = e.Einsatzleiter;
        _editEinsatzForm.Fuehrungsassistent = e.Fuehrungsassistent;
        _editEinsatzForm.Bemerkungen = e.ExportPfad;
        _editEinsatzMessage = string.Empty;
        _editEinsatzIsError = false;
    }

    private void CloseEditEinsatzModal()
    {
        _showEditEinsatzModal = false;
    }

    private async Task SaveEditEinsatzAsync()
    {
        if (string.IsNullOrWhiteSpace(_editEinsatzForm.Einsatzort))
        {
            _editEinsatzMessage = "Einsatzort ist erforderlich.";
            _editEinsatzIsError = true;
            return;
        }

        var current = EinsatzService.CurrentEinsatz;
        var prevFa = current.Fuehrungsassistent;

        DateTime? newAlarmierungsZeit = current.AlarmierungsZeit;
        if (!string.IsNullOrWhiteSpace(_editEinsatzForm.AlarmierungsZeit)
            && TimeOnly.TryParse(_editEinsatzForm.AlarmierungsZeit, out var alarmTime))
        {
            var baseDate = current.AlarmierungsZeit ?? current.EinsatzDatum;
            newAlarmierungsZeit = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day,
                alarmTime.Hour, alarmTime.Minute, 0);
        }

        var updated = new EinsatzData
        {
            IstEinsatz = _editEinsatzForm.IstEinsatz,
            EinsatzNummer = _editEinsatzForm.EinsatzNummer,
            EinsatzkontakteMigriert = true,
            PolizeiKontaktName = _editEinsatzForm.PolizeiKontaktName,
            PolizeiDienstnummer = _editEinsatzForm.PolizeiDienstnummer,
            PolizeiTelefon = _editEinsatzForm.PolizeiTelefon,
            PolizeiVermisstenmeldungAufgenommen = _editEinsatzForm.PolizeiVermisstenmeldungAufgenommen,
            PolizeiKoordinationBesprochen = _editEinsatzForm.PolizeiKoordinationBesprochen,
            PolizeiSuchabschnittAbgestimmt = _editEinsatzForm.PolizeiSuchabschnittAbgestimmt,
            PolizeiRueckmeldepflichtVereinbart = _editEinsatzForm.PolizeiRueckmeldepflichtVereinbart,
            PolizeiDatenschutzGeklaert = _editEinsatzForm.PolizeiDatenschutzGeklaert,
            BosEinheit = _editEinsatzForm.BosEinheit,
            BosZugfuehrer = _editEinsatzForm.BosZugfuehrer,
            BosFunkrufname = _editEinsatzForm.BosFunkrufname,
            BosAufgabenteilung = _editEinsatzForm.BosAufgabenteilung,
            BosAbschnittAbgestimmt = _editEinsatzForm.BosAbschnittAbgestimmt,
            BosRessourcenBesprochen = _editEinsatzForm.BosRessourcenBesprochen,
            Einsatzort = _editEinsatzForm.Einsatzort,
            MapAddress = _editEinsatzForm.MapAddress,
            Alarmiert = _editEinsatzForm.Alarmiert,
            AlarmierungsZeit = newAlarmierungsZeit,
            AnzahlTeams = _editEinsatzForm.AnzahlTeams,
            ExportPfad = _editEinsatzForm.Bemerkungen,
            Einsatzleiter = _editEinsatzForm.Einsatzleiter,
            Fuehrungsassistent = _editEinsatzForm.Fuehrungsassistent
        };

        await EinsatzService.UpdateEinsatzAsync(updated);

        // Wenn FA gewechselt wurde, gespeichertes Layout des neuen FA laden
        if (!string.Equals(updated.Fuehrungsassistent, prevFa, StringComparison.OrdinalIgnoreCase))
        {
            _currentLayout = await DashboardLayoutService.LoadLayoutAsync(updated.Fuehrungsassistent);
        }

        _showEditEinsatzModal = false;
    }

    private void OpenVermisstenModalNew()
    {
        _vForm = new Einsatzueberwachung.Domain.Models.VermisstenInfo { Id = Guid.NewGuid() };
        _vermisstenMessage = string.Empty;
        _showVermisstenModal = true;
    }

    private void OpenVermisstenModal() => OpenVermisstenModalAt(null);

    private void OpenVermisstenModalAt(string? sectionId)
    {
        var list = EinsatzService.CurrentEinsatz.Vermisste ?? new();
        var existing = _vermisstenPanelSelectedId.HasValue
            ? list.FirstOrDefault(v => v.Id == _vermisstenPanelSelectedId.Value)
            : list.FirstOrDefault();

        _vForm = existing is not null
            ? new Einsatzueberwachung.Domain.Models.VermisstenInfo
            {
                Id = existing.Id,
                Vorname = existing.Vorname,
                Nachname = existing.Nachname,
                Alter = existing.Alter,
                Geburtsdatum = existing.Geburtsdatum,
                Kleidung = existing.Kleidung,
                Besonderheiten = existing.Besonderheiten,
                ZuletztGesehenOrt = existing.ZuletztGesehenOrt,
                ZuletztGesehenZeit = existing.ZuletztGesehenZeit,
                ZuletztGesehenVon = existing.ZuletztGesehenVon,
                Vorerkrankungen = existing.Vorerkrankungen,
                Medikamente = existing.Medikamente,
                Orientierung = existing.Orientierung,
                Mobilitaet = existing.Mobilitaet,
                Suizidrisiko = existing.Suizidrisiko,
                Bewaffnet = existing.Bewaffnet,
                PolizeiKontaktName = existing.PolizeiKontaktName,
                PolizeiDienstnummer = existing.PolizeiDienstnummer,
                PolizeiTelefon = existing.PolizeiTelefon,
                PolizeiVermisstenmeldungAufgenommen = existing.PolizeiVermisstenmeldungAufgenommen,
                PolizeiKoordinationBesprochen = existing.PolizeiKoordinationBesprochen,
                PolizeiSuchabschnittAbgestimmt = existing.PolizeiSuchabschnittAbgestimmt,
                PolizeiRueckmeldepflichtVereinbart = existing.PolizeiRueckmeldepflichtVereinbart,
                PolizeiDatenschutzGeklaert = existing.PolizeiDatenschutzGeklaert,
                BosEinheit = existing.BosEinheit,
                BosZugfuehrer = existing.BosZugfuehrer,
                BosFunkrufname = existing.BosFunkrufname,
                BosAufgabenteilung = existing.BosAufgabenteilung,
                BosAbschnittAbgestimmt = existing.BosAbschnittAbgestimmt,
                BosRessourcenBesprochen = existing.BosRessourcenBesprochen
            }
            : new Einsatzueberwachung.Domain.Models.VermisstenInfo { Id = Guid.NewGuid() };

        _vermisstenMessage = string.Empty;
        _pendingVermisstenModalSectionScrollTarget = sectionId;
        _showVermisstenModal = true;
    }

    private void CloseVermisstenModal()
    {
        _pendingVermisstenModalSectionScrollTarget = null;
        _showVermisstenModal = false;
    }

    private async Task SaveVermisstenAsync()
    {
        await EinsatzService.UpsertVermisstenAsync(_vForm);
        _vermisstenPanelSelectedId = _vForm.Id;
        _vermisstenMessage = "Gespeichert.";
        _vermisstenIsError = false;
        await Task.Delay(1200);
        _showVermisstenModal = false;
    }

    private async Task DeleteCurrentVermisstenAsync()
    {
        if (!_vermisstenPanelSelectedId.HasValue) return;
        await EinsatzService.RemoveVermisstenAsync(_vermisstenPanelSelectedId.Value);
        _vermisstenPanelSelectedId = null;
    }

    private IEnumerable<(string Label, Func<bool> Getter, Action<bool> Setter)> GetPolizeiChecklist()
    {
        yield return ("Vermisstenmeldung aufgenommen",
            () => _editEinsatzForm.PolizeiVermisstenmeldungAufgenommen,
            v => _editEinsatzForm.PolizeiVermisstenmeldungAufgenommen = v);
        yield return ("Einsatzkoordination besprochen",
            () => _editEinsatzForm.PolizeiKoordinationBesprochen,
            v => _editEinsatzForm.PolizeiKoordinationBesprochen = v);
        yield return ("Suchabschnitte abgestimmt",
            () => _editEinsatzForm.PolizeiSuchabschnittAbgestimmt,
            v => _editEinsatzForm.PolizeiSuchabschnittAbgestimmt = v);
        yield return ("Rückmeldepflicht vereinbart",
            () => _editEinsatzForm.PolizeiRueckmeldepflichtVereinbart,
            v => _editEinsatzForm.PolizeiRueckmeldepflichtVereinbart = v);
        yield return ("Datenschutz (Personendaten) geklärt",
            () => _editEinsatzForm.PolizeiDatenschutzGeklaert,
            v => _editEinsatzForm.PolizeiDatenschutzGeklaert = v);
    }

    /// <summary>
    /// Validates that an ID coming from a URL query parameter only contains characters
    /// expected in a GUID (hex digits and hyphens) before it is forwarded to JavaScript.
    /// </summary>
    private static bool IsValidEntityId(string id) =>
        id.Length <= 64 && id.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF-".AsSpan()) == -1;

}
