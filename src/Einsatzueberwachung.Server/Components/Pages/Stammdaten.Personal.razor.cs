using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Stammdaten
{
    private async Task SavePersonalAsync()
    {
        if (string.IsNullOrWhiteSpace(_personalDraft.Vorname) || string.IsNullOrWhiteSpace(_personalDraft.Nachname))
        {
            _status = "Vorname und Nachname sind erforderlich.";
            return;
        }

        if (_editingPersonalId is null)
        {
            await MasterDataService.AddPersonalAsync(ClonePersonal(_personalDraft));
            _status = "Personal angelegt.";
            _showCreatePersonalModal = false;
        }
        else
        {
            await MasterDataService.UpdatePersonalAsync(ClonePersonal(_personalDraft));
            _status = "Personal gespeichert.";
        }

        ResetPersonalForm();
        await RefreshAsync();
    }

    private void EditPersonal(string id)
    {
        var person = _personal.First(entry => entry.Id == id);
        _editingPersonalId = id;
        _personalDraft = ClonePersonal(person);
    }

    private async Task DeletePersonalAsync(string id)
    {
        await MasterDataService.DeletePersonalAsync(id);
        if (_editingPersonalId == id)
        {
            ResetPersonalForm();
        }

        _status = "Personal gelöscht.";
        await RefreshAsync();
    }

    private void ResetPersonalForm()
    {
        _editingPersonalId = null;
        _personalDraft = new PersonalEntry();
    }

    private void TogglePersonalSkill(PersonalSkills skill, bool enabled)
    {
        _personalDraft.Skills = enabled
            ? _personalDraft.Skills | skill
            : _personalDraft.Skills & ~skill;
    }

    private void OpenCreatePersonalModal()
    {
        ResetPersonalForm();
        _showCreatePersonalModal = true;
    }

    private void CloseCreatePersonalModal()
    {
        _showCreatePersonalModal = false;
        ResetPersonalForm();
    }

    private static PersonalEntry ClonePersonal(PersonalEntry source)
    {
        return new PersonalEntry
        {
            Id = source.Id,
            Vorname = source.Vorname,
            Nachname = source.Nachname,
            Skills = source.Skills,
            Notizen = source.Notizen,
            IsActive = source.IsActive,
            DiveraUserId = source.DiveraUserId
        };
    }
}
