using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Stammdaten
{
    private async Task SaveDogAsync()
    {
        if (string.IsNullOrWhiteSpace(_dogDraft.Name) || string.IsNullOrWhiteSpace(_dogDraft.Rasse))
        {
            _status = "Hundename und Rasse sind erforderlich.";
            return;
        }

        if (_editingDogId is null)
        {
            await MasterDataService.AddDogAsync(CloneDog(_dogDraft));
            _status = "Hund angelegt.";
            _showCreateDogModal = false;
        }
        else
        {
            await MasterDataService.UpdateDogAsync(CloneDog(_dogDraft));
            _status = "Hund gespeichert.";
        }

        ResetDogForm();
        await RefreshAsync();
    }

    private void EditDog(string id)
    {
        var dog = _dogs.First(entry => entry.Id == id);
        _editingDogId = id;
        _dogDraft = CloneDog(dog);
    }

    private async Task DeleteDogAsync(string id)
    {
        await MasterDataService.DeleteDogAsync(id);
        if (_editingDogId == id)
        {
            ResetDogForm();
        }

        _status = "Hund gelöscht.";
        await RefreshAsync();
    }

    private void ResetDogForm()
    {
        _editingDogId = null;
        _dogDraft = new DogEntry();
    }

    private void ToggleDogSpecialization(DogSpecialization specialization, bool enabled)
    {
        _dogDraft.Specializations = enabled
            ? _dogDraft.Specializations | specialization
            : _dogDraft.Specializations & ~specialization;
    }

    private void OpenCreateDogModal()
    {
        ResetDogForm();
        _showCreateDogModal = true;
    }

    private void CloseCreateDogModal()
    {
        _showCreateDogModal = false;
        ResetDogForm();
    }

    private void ToggleHundefuehrer(string personalId, bool enabled)
    {
        if (enabled)
        {
            if (!_dogDraft.HundefuehrerIds.Contains(personalId))
                _dogDraft.HundefuehrerIds.Add(personalId);
        }
        else
        {
            _dogDraft.HundefuehrerIds.Remove(personalId);
        }
    }

    private static DogEntry CloneDog(DogEntry source)
    {
        return new DogEntry
        {
            Id = source.Id,
            Name = source.Name,
            Rasse = source.Rasse,
            Alter = source.Alter,
            Specializations = source.Specializations,
            HundefuehrerIds = new List<string>(source.HundefuehrerIds),
            Notizen = source.Notizen,
            IsActive = source.IsActive
        };
    }
}
