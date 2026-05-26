using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages.KarteComponents;

public partial class KartePunkteTab
{
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private const double CoordinateEpsilon = 0.0000001;

    [Parameter, EditorRequired] public List<MapMarker> Markers { get; set; } = new();
    [Parameter] public bool HasElwPosition { get; set; }
    [Parameter] public bool ClickToPlaceActive { get; set; }
    [Parameter] public string CoordInputMode { get; set; } = "click";
    [Parameter] public EventCallback OnSetElwPosition { get; set; }
    [Parameter] public EventCallback<string> CoordInputModeChanged { get; set; }
    [Parameter] public EventCallback OnActivateClickToPlace { get; set; }
    [Parameter] public EventCallback OnDeactivateClickToPlace { get; set; }
    [Parameter] public EventCallback OnMarkersChanged { get; set; }

    public string InputLabel => _inputMarkerLabel;

    public void ClearInputLabel()
    {
        _inputMarkerLabel = "";
        StateHasChanged();
    }

    public void OpenEditDialogFromDrag(string markerId, double lat, double lng)
    {
        var marker = EinsatzService.CurrentEinsatz.MapMarkers.FirstOrDefault(m => m.Id == markerId);
        if (marker == null) return;

        _editingMarker = marker;
        if (string.IsNullOrEmpty(_editMarkerLabel))
            _editMarkerLabel = marker.Label;
        _editMarkerLat = lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        _editMarkerLng = lng.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        _editMarkerUtm = FormatUtmString(lat, lng);
        _editMarkerMessage = "✓ Position aktualisiert. Klicken Sie \"Speichern\" um die Änderung zu übernehmen.";
        _editMarkerMessageType = "alert-info";
        _showMarkerEditDialog = true;
        _markerMessage = "";
        StateHasChanged();
    }

    public async Task PlaceMarkerAtPositionAsync(double lat, double lng, string? label = null)
    {
        await PlaceMarkerAtPosition(lat, lng, label);
    }

    private string _inputMarkerLabel = "";
    private string _inputLatitude = "";
    private string _inputLongitude = "";
    private string _inputUtm = "";
    private string _markerMessage = "";
    private string _markerMessageType = "alert-info";
    private int _markerCounter = 0;

    private bool _inputOpen = false;

    private string _mode = "click";

    private bool _showMarkerEditDialog = false;
    private MapMarker? _editingMarker = null;
    private string _editMarkerLabel = "";
    private string _editMarkerLat = "";
    private string _editMarkerLng = "";
    private string _editMarkerUtm = "";
    private string _editMarkerMessage = "";
    private string _editMarkerMessageType = "alert-info";

    protected override void OnInitialized()
    {
        _markerCounter = EinsatzService.CurrentEinsatz.MapMarkers.Count;
        _mode = CoordInputMode;
    }

    protected override void OnParametersSet()
    {
        if (CoordInputMode == "click" && _mode != "click")
            _mode = "click";
    }

    private async Task ToggleInputPanel()
    {
        if (_inputOpen)
        {
            _inputOpen = false;
            _markerMessage = "";
            if (ClickToPlaceActive)
                await OnDeactivateClickToPlace.InvokeAsync();
        }
        else
        {
            _inputOpen = true;
            if (_mode == "click" && !ClickToPlaceActive)
                await OnActivateClickToPlace.InvokeAsync();
        }
    }

    private async Task ActivateClickToPlaceModeAsync()
    {
        _mode = "click";
        await CoordInputModeChanged.InvokeAsync("click");
        await OnActivateClickToPlace.InvokeAsync();
    }

    private async Task SetCoordInputModeAsync(string mode)
    {
        _mode = mode;
        _markerMessage = "";
        await CoordInputModeChanged.InvokeAsync(mode);
        if (ClickToPlaceActive)
            await OnDeactivateClickToPlace.InvokeAsync();
    }

    private async Task PlaceMarkerFromLatLong()
    {
        _markerMessage = "";
        if (!TryParseDecimalInput(_inputLatitude, out var lat) ||
            !TryParseDecimalInput(_inputLongitude, out var lng))
        {
            _markerMessage = "Ungültige Koordinaten. Bitte Dezimalgrad eingeben (z.B. 49.31880 / 8.43120).";
            _markerMessageType = "alert-danger";
            return;
        }
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
        {
            _markerMessage = "Koordinaten außerhalb des gültigen Bereichs.";
            _markerMessageType = "alert-danger";
            return;
        }
        await PlaceMarkerAtPosition(lat, lng, _inputMarkerLabel);
        _inputLatitude = "";
        _inputLongitude = "";
        _inputMarkerLabel = "";
    }

    private async Task PlaceMarkerFromUtm()
    {
        _markerMessage = "";
        if (!UtmConverter.TryParseUtm(_inputUtm ?? "", out int zone, out char band, out double easting, out double northing))
        {
            _markerMessage = "Ungültiges UTM-Format. Bitte z.B. \"32U 461344 5481745\" eingeben.";
            _markerMessageType = "alert-danger";
            return;
        }
        var (lat, lng) = UtmConverter.UtmToLatLong(zone, band, easting, northing);
        await PlaceMarkerAtPosition(lat, lng, _inputMarkerLabel);
        _inputUtm = "";
        _inputMarkerLabel = "";
    }

    private async Task PlaceMarkerAtPosition(double lat, double lng, string? label = null)
    {
        string markerLabel;
        if (!string.IsNullOrWhiteSpace(label))
        {
            markerLabel = label.Trim();
        }
        else
        {
            _markerCounter++;
            markerLabel = $"P{_markerCounter}";
        }

        var (utmZone, utmBand, utmEasting, utmNorthing) = UtmConverter.LatLongToUtm(lat, lng);
        var marker = new MapMarker
        {
            Label = markerLabel,
            Latitude = lat,
            Longitude = lng,
            UtmZone = UtmConverter.FormatUtmZone(utmZone, utmBand),
            UtmEasting = utmEasting,
            UtmNorthing = utmNorthing,
            CreatedAt = DateTime.Now
        };

        await EinsatzService.AddMapMarkerAsync(marker);
        await JSRuntime.InvokeVoidAsync("LeafletMap.setCoordinateMarker",
            "einsatzMap", marker.Id, lat, lng, markerLabel, "", marker.Color);
        await JSRuntime.InvokeVoidAsync("LeafletMap.centerMap", "einsatzMap", lat, lng, 16);

        var noteText = $"📍 Koordinaten-Marker \"{markerLabel}\" gesetzt: {lat:F6}° / {lng:F6}° (UTM: {marker.FormattedUtm})";
        await EinsatzService.AddGlobalNoteWithSourceAsync(noteText, "system", "Einsatzleitung", "Notiz",
            GlobalNotesEntryType.EinsatzUpdate, "Karte");

        _markerMessage = $"✓ Punkt \"{markerLabel}\" gesetzt ({lat:F6}° / {lng:F6}°)";
        _markerMessageType = "alert-success";

        await OnMarkersChanged.InvokeAsync();
        StateHasChanged();
    }

    private async Task RemoveMarker(MapMarker marker)
    {
        await EinsatzService.RemoveMapMarkerAsync(marker.Id);
        await JSRuntime.InvokeVoidAsync("LeafletMap.removeCoordinateMarker", "einsatzMap", marker.Id);

        var noteText = $"📍 Koordinaten-Marker \"{marker.Label}\" entfernt ({marker.FormattedLatLng})";
        await EinsatzService.AddGlobalNoteWithSourceAsync(noteText, "system", "Einsatzleitung", "Notiz",
            GlobalNotesEntryType.EinsatzUpdate, "Karte");

        _markerMessage = $"Punkt \"{marker.Label}\" entfernt.";
        _markerMessageType = "alert-info";

        await OnMarkersChanged.InvokeAsync();
        StateHasChanged();
    }

    private async Task ZoomToMarker(MapMarker marker)
    {
        await JSRuntime.InvokeVoidAsync("LeafletMap.zoomToCoordinateMarker", "einsatzMap", marker.Id);
    }

    private void StartEditMarker(MapMarker marker)
    {
        _editingMarker = marker;
        _editMarkerLabel = marker.Label;
        _editMarkerLat = marker.Latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        _editMarkerLng = marker.Longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        _editMarkerUtm = marker.FormattedUtm;
        _editMarkerMessage = "";
        _showMarkerEditDialog = true;
    }

    private void CloseMarkerEditDialog()
    {
        _showMarkerEditDialog = false;
        _editingMarker = null;
        _editMarkerMessage = "";
    }

    private void OnEditLatChanged(ChangeEventArgs e)
    {
        _editMarkerLat = e.Value?.ToString() ?? "";
        SyncLatLngToUtm();
    }

    private void OnEditLngChanged(ChangeEventArgs e)
    {
        _editMarkerLng = e.Value?.ToString() ?? "";
        SyncLatLngToUtm();
    }

    private void OnEditUtmChanged(ChangeEventArgs e)
    {
        _editMarkerUtm = e.Value?.ToString() ?? "";
        SyncUtmToLatLng();
    }

    private void SyncLatLngToUtm()
    {
        _editMarkerMessage = "";
        if (TryParseDecimalInput(_editMarkerLat, out var lat) &&
            TryParseDecimalInput(_editMarkerLng, out var lng) &&
            lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180)
        {
            _editMarkerUtm = FormatUtmString(lat, lng);
        }
    }

    private void SyncUtmToLatLng()
    {
        _editMarkerMessage = "";
        if (UtmConverter.TryParseUtm(_editMarkerUtm ?? "", out int zone, out char band, out double easting, out double northing))
        {
            var (lat, lng) = UtmConverter.UtmToLatLong(zone, band, easting, northing);
            _editMarkerLat = lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            _editMarkerLng = lng.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private async Task StartMarkerDragMode()
    {
        if (_editingMarker == null) return;
        var markerId = _editingMarker.Id;
        _showMarkerEditDialog = false;
        _editMarkerMessage = "";
        StateHasChanged();
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.enableCoordinateMarkerDrag", "einsatzMap", markerId);
            await JSRuntime.InvokeVoidAsync("LeafletMap.zoomToCoordinateMarker", "einsatzMap", markerId);
            _markerMessage = "Verschieben Sie den Marker auf der Karte. Der Bearbeiten-Dialog öffnet sich nach dem Loslassen.";
            _markerMessageType = "alert-info";
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _editMarkerMessage = $"Fehler: {ex.Message}";
            _editMarkerMessageType = "alert-danger";
            _showMarkerEditDialog = true;
            StateHasChanged();
        }
    }

    private async Task SaveMarkerEdit()
    {
        if (_editingMarker == null) return;
        _editMarkerMessage = "";

        var newLabel = _editMarkerLabel?.Trim();
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            _editMarkerMessage = "Bitte eine Bezeichnung eingeben.";
            _editMarkerMessageType = "alert-danger";
            return;
        }
        if (!TryParseDecimalInput(_editMarkerLat, out var lat) || !TryParseDecimalInput(_editMarkerLng, out var lng))
        {
            _editMarkerMessage = "Ungültige Koordinaten.";
            _editMarkerMessageType = "alert-danger";
            return;
        }
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
        {
            _editMarkerMessage = "Koordinaten außerhalb des gültigen Bereichs.";
            _editMarkerMessageType = "alert-danger";
            return;
        }

        var oldLabel = _editingMarker.Label;
        var positionChanged = Math.Abs(_editingMarker.Latitude - lat) > CoordinateEpsilon ||
                              Math.Abs(_editingMarker.Longitude - lng) > CoordinateEpsilon;
        var labelChanged = oldLabel != newLabel;

        var updated = await EinsatzService.UpdateMapMarkerAsync(_editingMarker.Id,
            label: newLabel,
            latitude: positionChanged ? lat : null,
            longitude: positionChanged ? lng : null);

        if (updated == null)
        {
            _editMarkerMessage = "Marker nicht gefunden.";
            _editMarkerMessageType = "alert-danger";
            return;
        }

        await JSRuntime.InvokeVoidAsync("LeafletMap.removeCoordinateMarker", "einsatzMap", updated.Id);
        await JSRuntime.InvokeVoidAsync("LeafletMap.setCoordinateMarker",
            "einsatzMap", updated.Id, updated.Latitude, updated.Longitude,
            updated.Label, updated.Description, updated.Color);

        var changes = new List<string>();
        if (labelChanged) changes.Add($"Bezeichnung: \"{oldLabel}\" → \"{newLabel}\"");
        if (positionChanged) changes.Add($"Position: {updated.FormattedLatLng} (UTM: {updated.FormattedUtm})");
        if (changes.Count > 0)
        {
            var noteText = $"📍 Koordinaten-Marker bearbeitet: {string.Join(", ", changes)}";
            await EinsatzService.AddGlobalNoteWithSourceAsync(noteText, "system", "Einsatzleitung", "Notiz",
                GlobalNotesEntryType.EinsatzUpdate, "Karte");
        }

        _markerMessage = $"✓ Marker \"{newLabel}\" aktualisiert.";
        _markerMessageType = "alert-success";

        CloseMarkerEditDialog();
        await OnMarkersChanged.InvokeAsync();
        StateHasChanged();
    }

    private async Task DownloadMarkerGpx(MapMarker marker)
    {
        try
        {
            var dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await JSRuntime.InvokeVoidAsync("downloadFile",
                GpxBuilder.MarkerFileName(marker, dateStamp),
                GpxBuilder.BuildMarkerGpx(marker),
                "application/gpx+xml");
        }
        catch (Exception ex)
        {
            _markerMessage = $"Fehler beim GPX-Export: {ex.Message}";
            _markerMessageType = "alert-danger";
            StateHasChanged();
        }
    }

    private async Task DownloadAllMarkersGpx()
    {
        if (!Markers.Any()) return;
        try
        {
            var dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await JSRuntime.InvokeVoidAsync("downloadFile",
                $"Punkte_{dateStamp}.gpx",
                GpxBuilder.BuildMarkersGpx(Markers.OrderBy(m => m.CreatedAt)),
                "application/gpx+xml");
        }
        catch (Exception ex)
        {
            _markerMessage = $"Fehler beim GPX-Export: {ex.Message}";
            _markerMessageType = "alert-danger";
            StateHasChanged();
        }
    }

    private static bool TryParseDecimalInput(string? input, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        return double.TryParse(input.Replace(",", "."),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static string FormatUtmString(double lat, double lng)
    {
        var (utmZone, utmBand, utmEasting, utmNorthing) = UtmConverter.LatLongToUtm(lat, lng);
        return $"{UtmConverter.FormatUtmZone(utmZone, utmBand)} {utmEasting:F0} {utmNorthing:F0}";
    }
}
