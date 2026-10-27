using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Stammdaten
{
    private async Task SaveDroneAsync()
    {
        if (string.IsNullOrWhiteSpace(_droneDraft.Name) || string.IsNullOrWhiteSpace(_droneDraft.Modell))
        {
            _status = "Drohnenname und Modell sind erforderlich.";
            return;
        }

        if (_editingDroneId is null)
        {
            await MasterDataService.AddDroneAsync(CloneDrone(_droneDraft));
            _status = "Drohne angelegt.";
            _showCreateDroneModal = false;
        }
        else
        {
            await MasterDataService.UpdateDroneAsync(CloneDrone(_droneDraft));
            _status = "Drohne gespeichert.";
        }

        ResetDroneForm();
        await RefreshAsync();
    }

    private void EditDrone(string id)
    {
        var drone = _drones.First(entry => entry.Id == id);
        _editingDroneId = id;
        _droneDraft = CloneDrone(drone);
    }

    private async Task DeleteDroneAsync(string id)
    {
        await MasterDataService.DeleteDroneAsync(id);
        if (_editingDroneId == id)
        {
            ResetDroneForm();
        }

        _status = "Drohne gelöscht.";
        await RefreshAsync();
    }

    private void ResetDroneForm()
    {
        _editingDroneId = null;
        _droneDraft = new DroneEntry();
    }

    private void OpenCreateDroneModal()
    {
        ResetDroneForm();
        _showCreateDroneModal = true;
    }

    private void CloseCreateDroneModal()
    {
        _showCreateDroneModal = false;
        ResetDroneForm();
    }

    private static DroneEntry CloneDrone(DroneEntry source)
    {
        return new DroneEntry
        {
            Id = source.Id,
            Name = source.Name,
            Modell = source.Modell,
            Hersteller = source.Hersteller,
            Seriennummer = source.Seriennummer,
            DrohnenpilotId = source.DrohnenpilotId,
            Notizen = source.Notizen,
            LivestreamUrl = source.LivestreamUrl,
            IsActive = source.IsActive
        };
    }
}
