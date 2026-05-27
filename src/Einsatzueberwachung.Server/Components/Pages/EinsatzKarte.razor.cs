using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components.Pages.KarteComponents;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzKarte
{
    [Inject] IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] ICollarTrackingService CollarTrackingService { get; set; } = default!;
    [Inject] ISettingsService SettingsService { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] ILogger<EinsatzKarte> Logger { get; set; } = default!;
    [Inject] MissionTopbarService MissionTopbar { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "embed")]
    private bool _embedMode { get; set; }

    [SupplyParameterFromQuery(Name = "controls")]
    private string? _embedControlsRaw { get; set; }

    private HashSet<string> _embedControls = new(StringComparer.OrdinalIgnoreCase);

    [SupplyParameterFromQuery(Name = "focusCollarId")]
    private string? _focusCollarId { get; set; }

    [SupplyParameterFromQuery(Name = "focusAreaId")]
    private string? _focusAreaId { get; set; }

    private const double CoordinateEpsilon = 0.0000001;

    private List<SearchArea> _searchAreas = new();
    private List<Team> _teams = new();
    private bool _showDialog = false;
    private bool _showKarteDialog = false;
    private string _karteZoomMode = "all";
    private double? _liveMapCenterLat;
    private double? _liveMapCenterLng;
    private int? _liveMapZoom;
    private string _karteTileType = "streets";
    private string _karteGridType = "none";
    private string _mapBaseLayerType = "streets";
    private string _gridLayerType = "none";
    private string _karteTeamFilter = "";
    private bool _karteShowSearchAreas = true;
    private bool _karteShowPoints = true;
    private bool _karteShowGps = false;
    private bool _karteShowPhone = false;
    private SearchArea _currentArea = new();
    private SearchArea? _editingArea = null;
    private string _selectedTeamId = "";
    private SearchArea? _deleteCandidate;
    private string _addressSearch = "";
    private string _searchMessage = "";
    private CancellationTokenSource? _searchMessageCts;

    /// <summary>Setzt die Statusmeldung und lässt sie nach 5 s automatisch verschwinden.</summary>
    private void SetSearchMessage(string msg)
    {
        _searchMessageCts?.Cancel();
        _searchMessageCts = new CancellationTokenSource();
        _searchMessage = msg;
        var token = _searchMessageCts.Token;
        _ = Task.Delay(5000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                InvokeAsync(() => { _searchMessage = string.Empty; StateHasChanged(); });
        }, TaskScheduler.Default);
    }
    private string _lastDrawnGeoJson = "";
    private bool _drawingSaved = false; // Flag: wurde die aktuelle Zeichnung gespeichert?
    private DotNetObjectReference<EinsatzKarte>? _dotNetReference;

    // Polygon-Bearbeitungsmodus
    private bool _polygonEditMode = false;
    private SearchArea? _editingAreaForPolygon = null;

    // Standardposition (Speyer, Deutschland)
    private double _mapCenterLat = 49.3188;
    private double _mapCenterLng = 8.4312;
    private int _mapZoom = 13;

    // ELW-Position
    private bool _hasElwPosition = false;
    private bool _sidebarMinimized = false;

    // Collar-Tracking
    private List<Collar> _collars = new();
    private bool _trackingVisible = false;
    private bool _phoneLayerVisible = false;
    private bool _searchAreasVisible = true;
    private bool _pointMarkersVisible = true;
    private Dictionary<string, string> _oobWarnings = new();
    private Dictionary<string, CollarLocation> _collarLastLocations = new();

    // Abgeschlossene Tracks (Snapshots)
    private Dictionary<string, bool> _completedTrackVisibility = new();

    // Sidebar Tab-Navigation
    private string _activeSidebarTab = "areas";
    private string? _expandedSnapshotId;
    private bool _mapInitialized;
    private bool _collarFocusApplyRunning;
    private string? _pendingFocusCollarId;
    private string? _lastAppliedFocusCollarId;
    private bool _areaFocusApplyRunning;
    private string? _pendingFocusAreaId;
    private string? _lastAppliedFocusAreaId;

    // Koordinaten-Marker
    private List<MapMarker> _mapMarkers = new();

    // Karten-Marker Konfiguration (aus AppSettings geladen)
    private string _collarMarkerIcon = "paw";
    private string _humanMarkerIcon = "phone";

    private string _coordInputMode = "click"; // "click", "latlong", "utm" — auch von KartePunkteTab über @bind synchronisiert
    private bool _clickToPlaceActive = false;

    // Zeichenmodus (Suchgebiete)
    private bool _drawingActive = false;
    private bool _mapTileMenuExpanded;
    private bool _mapGridMenuExpanded;
    private bool _mapContentMenuExpanded = true;

    // Referenz auf KartePunkteTab für JSInvokable-Callbacks
    private KartePunkteTab? _punkteTab;

    private string SelectedMapLayerLabel => _mapBaseLayerType switch
    {
        "satellite" => "Satellit (Esri)",
        "satelliteGoogle" => "Satellit (Google)",
        "hybrid" => "Hybrid (Google)",
        "topo" => "Topografisch",
        _ => "Straßenkarte"
    };

    private string SelectedGridLayerLabel => _gridLayerType switch
    {
        "utm" => "UTM",
        "latlon" => "Lat/Lon",
        _ => "Ohne"
    };

    private string MapContentSummary => $"{(_searchAreasVisible ? 1 : 0) + (_pointMarkersVisible ? 1 : 0) + (_trackingVisible ? 1 : 0) + (_phoneLayerVisible ? 1 : 0)}/4 aktiv";

    protected override async Task OnInitializedAsync()
    {
        _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();
        foreach (var area in _searchAreas)
        {
            if ((area.Coordinates == null || area.Coordinates.Count == 0) && !string.IsNullOrWhiteSpace(area.GeoJsonData))
            {
                ExtractCoordinatesFromGeoJson(area);
            }
        }
        _teams = EinsatzService.Teams;
        _collars = CollarTrackingService.Collars.ToList();
        _mapMarkers = EinsatzService.CurrentEinsatz.MapMarkers.ToList();

        // DotNetObjectReference für Callbacks erstellen
        _dotNetReference = DotNetObjectReference.Create(this);

        // Standardposition aus Einstellungen laden
        var settings = await SettingsService.GetAppSettingsAsync();
        _mapCenterLat = settings.MapDefaultLat;
        _mapCenterLng = settings.MapDefaultLng;
        _mapZoom = settings.MapDefaultZoom;
        _collarMarkerIcon = settings.CollarMarkerIconOrDefault;
        _humanMarkerIcon = settings.HumanMarkerIconOrDefault;

        // Wenn Einsatzort-Adresse vorhanden, versuche zu geocoden
        if (!string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.MapAddress))
        {
            _addressSearch = EinsatzService.CurrentEinsatz.MapAddress;
        }

        // ELW-Position hat Vorrang vor Standardposition
        _hasElwPosition = EinsatzService.CurrentEinsatz.ElwPosition.HasValue;
        if (_hasElwPosition && EinsatzService.CurrentEinsatz.ElwPosition.HasValue)
        {
            var elw = EinsatzService.CurrentEinsatz.ElwPosition.Value;
            _mapCenterLat = elw.Latitude;
            _mapCenterLng = elw.Longitude;
        }

        ConfigureMissionTopbarContent();
    }

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(_focusCollarId)
            && !string.Equals(_focusCollarId, _lastAppliedFocusCollarId, StringComparison.Ordinal))
        {
            _pendingFocusCollarId = _focusCollarId;
        }

        if (!string.IsNullOrWhiteSpace(_focusAreaId)
            && !string.Equals(_focusAreaId, _lastAppliedFocusAreaId, StringComparison.Ordinal))
        {
            _pendingFocusAreaId = _focusAreaId;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                // Karte initialisieren und Callback-Referenz setzen
                await JSRuntime.InvokeVoidAsync("LeafletMap.initialize",
                    "einsatzMap", _mapCenterLat, _mapCenterLng, _mapZoom, _dotNetReference);

                // Bestehende Suchgebiete zur Karte HinzuFügen
                foreach (var area in _searchAreas.Where(a => !string.IsNullOrWhiteSpace(a.GeoJsonData)))
                {
                    await JSRuntime.InvokeVoidAsync("LeafletMap.addSearchArea",
                        "einsatzMap", area.Id, area.GeoJsonData, area.Color, area.Name);
                }

                // ELW-Position wiederherstellen falls vorhanden
                if (_hasElwPosition && EinsatzService.CurrentEinsatz.ElwPosition.HasValue)
                {
                    var elw = EinsatzService.CurrentEinsatz.ElwPosition.Value;
                    await JSRuntime.InvokeVoidAsync("LeafletMap.setMarker",
                        "einsatzMap", "elw", elw.Latitude, elw.Longitude, "ELW (Einsatzleitwagen)", "#FF0000");
                }

                // Einsatzort-Marker setzen falls vorhanden
                if (!string.IsNullOrWhiteSpace(_addressSearch))
                {
                    await SearchAddress();
                }

                // Collar-Tracking initialisieren
                await JSRuntime.InvokeVoidAsync("CollarTracking.initialize", "einsatzMap", _dotNetReference);
                await JSRuntime.InvokeVoidAsync("CollarTracking.setOptions", new { collarIcon = _collarMarkerIcon });

                // Handy-GPS Layer initialisieren
                await JSRuntime.InvokeVoidAsync("PhoneTracking.initialize", "einsatzMap");
                await JSRuntime.InvokeVoidAsync("PhoneTracking.setOptions", new { humanIcon = _humanMarkerIcon });

                // Bestehende Telefon-Tracks laufender Teams laden
                await LoadRunningTeamPhoneTracksAsync("einsatzMap");

                // Domain-Events für Live-Tracking abonnieren
                CollarTrackingService.CollarLocationReceived += OnCollarLocationReceived;
                CollarTrackingService.OutOfBoundsDetected += OnOutOfBoundsDetected;
                CollarTrackingService.CollarHistoryCleared += OnCollarHistoryCleared;
                CollarTrackingService.TrackSnapshotSaved += OnTrackSnapshotSaved;
                EinsatzService.TeamPhoneLocationChanged += OnTeamPhoneLocationChanged;
                EinsatzService.TeamPhoneTrackPointAdded += OnTeamPhoneTrackPointAdded;
                EinsatzService.TrackSnapshotAdded += OnTrackSnapshotSaved;

                // Bestehende Tracking-Daten automatisch laden (falls Daten vor Seitenbesuch gesendet wurden)
                _collars = CollarTrackingService.Collars.ToList();
                if (_collars.Count > 0)
                {
                    _trackingVisible = true;
                    foreach (var collar in _collars)
                    {
                        var history = CollarTrackingService.GetLocationHistory(collar.Id);
                        if (history.Count > 0)
                        {
                            var color = GetCollarColor(collar.Id);
                            var dogLabel = GetDogLabelForCollar(collar.Id);
                            await JSRuntime.InvokeVoidAsync("CollarTracking.loadHistory",
                                "einsatzMap", collar.Id, history, color, dogLabel);
                        }
                    }
                    StateHasChanged();
                                    foreach (var collar in _collars)
                                    {
                                        var history = CollarTrackingService.GetLocationHistory(collar.Id);
                                        if (history.Count > 0)
                                            _collarLastLocations[collar.Id] = history[history.Count - 1];
                                    }
                }

                // Abgeschlossene Tracks (Snapshots) aus diesem Einsatz laden
                var existingSnapshots = GetAllTrackSnapshotsForMap();
                if (existingSnapshots?.Count > 0)
                {
                    _trackingVisible = true;
                    foreach (var snap in existingSnapshots)
                    {
                        _completedTrackVisibility[snap.Id] = true;
                        await JSRuntime.InvokeVoidAsync("CollarTracking.addCompletedTrack",
                            "einsatzMap", snap.Id, snap.Points, snap.Color,
                            snap.TeamName, snap.DisplayLabel, snap.TrackType.ToString());
                    }
                    StateHasChanged();
                }

                // Bestehende Koordinaten-Marker auf der Karte wiederherstellen
                foreach (var marker in _mapMarkers)
                {
                    await JSRuntime.InvokeVoidAsync("LeafletMap.setCoordinateMarker",
                        "einsatzMap", marker.Id, marker.Latitude, marker.Longitude,
                        marker.Label, marker.Description, marker.Color);
                }

                // Embed-Modus: Sidebar und Topbar ausblenden, danach Karte neu messen
                if (_embedMode)
                {
                    await JSRuntime.InvokeVoidAsync("layoutTools.setEmbedMode", true);

                    // Embed-Controls aus Query parsen und als Body-Klassen setzen
                    _embedControls = (_embedControlsRaw ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var token in _embedControls)
                    {
                        await JSRuntime.InvokeVoidAsync(
                            "eval",
                            $"document.body.classList.add('embed-controls-{token.ToLowerInvariant()}')");
                    }

                    await Task.Delay(50); // CSS-Transition abwarten
                    await JSRuntime.InvokeVoidAsync("LeafletMap.invalidateSize", "einsatzMap");
                }

                _mapInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Fehler beim Initialisieren der Karte");
            }
        }

        await ApplyCollarFocusFromQueryAsync();
        await ApplyAreaFocusFromQueryAsync();
    }

    private async Task SearchAddress()
    {
        if (string.IsNullOrWhiteSpace(_addressSearch)) return;

        try
        {
            SetSearchMessage("Suche...");
            StateHasChanged();

            var result = await JSRuntime.InvokeAsync<GeocodeResult>("LeafletMap.geocodeAddress", _addressSearch);

            if (result.Success)
            {
                _mapCenterLat = result.Lat;
                _mapCenterLng = result.Lng;
                SetSearchMessage($"Gefunden: {result.DisplayName}");

                await JSRuntime.InvokeVoidAsync("LeafletMap.centerMap",
                    "einsatzMap", result.Lat, result.Lng, 15);

                await JSRuntime.InvokeVoidAsync("LeafletMap.setMarker",
                    "einsatzMap", "einsatzort", result.Lat, result.Lng, "Einsatzort", "#FF0000");

                // Adresse im Einsatz speichern
                EinsatzService.CurrentEinsatz.MapAddress = _addressSearch;
            }
            else
            {
                SetSearchMessage("Adresse nicht gefunden");
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            _searchMessage = $"Fehler: {ex.Message}";
            Logger.LogWarning(ex, "Fehler beim Geocoding");
        }
    }

    private void ShowAddAreaDialog()
    {
        _editingArea = null;
        _currentArea = new SearchArea
        {
            Name = $"Suchgebiet {_searchAreas.Count + 1}",
            Color = GetRandomColor(),
            GeoJsonData = _lastDrawnGeoJson // Verwende das zuletzt gezeichnete Shape
        };

        // Extrahiere Koordinaten aus GeoJSON wenn vorhanden
        if (!string.IsNullOrWhiteSpace(_lastDrawnGeoJson))
        {
            ExtractCoordinatesFromGeoJson(_currentArea);
        }

        _selectedTeamId = "";
        _showDialog = true;
    }

    private void EditSearchArea(SearchArea area)
    {
        _editingArea = area;
        _currentArea = new SearchArea
        {
            Id = area.Id,
            Name = area.Name,
            Color = area.Color,
            Notes = area.Notes,
            IsCompleted = area.IsCompleted,
            GeoJsonData = area.GeoJsonData,
            Coordinates = new List<(double, double)>(area.Coordinates)
        };
        _selectedTeamId = area.AssignedTeamId;
        _showDialog = true;
    }

    private async Task StartPolygonEditMode()
    {
        if (_editingArea == null) return;

        _editingAreaForPolygon = _editingArea;
        _showDialog = false;
        _polygonEditMode = true;
        StateHasChanged();

        await JSRuntime.InvokeVoidAsync("LeafletMap.startPolygonEdit", "einsatzMap", _editingArea.Id);
    }

    private async Task SavePolygonEdit()
    {
        await JSRuntime.InvokeVoidAsync("LeafletMap.savePolygonEdit", "einsatzMap");
        _polygonEditMode = false;
        _showDialog = false;
        _editingArea = null;
        _editingAreaForPolygon = null;
        StateHasChanged();
    }

    private async Task CancelPolygonEdit()
    {
        await JSRuntime.InvokeVoidAsync("LeafletMap.cancelPolygonEdit", "einsatzMap");
        _polygonEditMode = false;

        // Dialog erneut öffnen, damit der Nutzer weiterarbeiten kann
        if (_editingAreaForPolygon != null)
        {
            EditSearchArea(_editingAreaForPolygon);
        }

        _editingAreaForPolygon = null;
        StateHasChanged();
    }

    private async Task SaveArea()
    {
        var selectedTeamId = _selectedTeamId?.Trim() ?? string.Empty;

        // Hält beide Geometriedarstellungen konsistent (GeoJsonData <-> Coordinates).
        if (!string.IsNullOrWhiteSpace(_currentArea.GeoJsonData))
        {
            ExtractCoordinatesFromGeoJson(_currentArea);
        }

        // Auswahl immer explizit auf das Suchgebiet anwenden, auch beim Entfernen der Teamzuweisung.
        if (!string.IsNullOrWhiteSpace(selectedTeamId))
        {
            var team = _teams.FirstOrDefault(t => t.TeamId == selectedTeamId);
            _currentArea.AssignedTeamId = team?.TeamId ?? string.Empty;
            _currentArea.AssignedTeamName = team?.TeamName ?? string.Empty;
        }
        else
        {
            _currentArea.AssignedTeamId = string.Empty;
            _currentArea.AssignedTeamName = string.Empty;
        }

        if (_editingArea != null)
        {
            // Aktualisieren
            await EinsatzService.UpdateSearchAreaAsync(_currentArea);

            // Auf Karte aktualisieren
            await JSRuntime.InvokeVoidAsync("LeafletMap.removeSearchArea", "einsatzMap", _currentArea.Id);
            if (!string.IsNullOrWhiteSpace(_currentArea.GeoJsonData))
            {
                await JSRuntime.InvokeVoidAsync("LeafletMap.addSearchArea",
                    "einsatzMap", _currentArea.Id, _currentArea.GeoJsonData, _currentArea.Color, _currentArea.Name);
            }
        }
        else
        {
            // Neu erstellen
            await EinsatzService.AddSearchAreaAsync(_currentArea);

            if (!string.IsNullOrWhiteSpace(_currentArea.GeoJsonData))
            {
                await JSRuntime.InvokeVoidAsync("LeafletMap.addSearchArea",
                    "einsatzMap", _currentArea.Id, _currentArea.GeoJsonData, _currentArea.Color, _currentArea.Name);
            }
        }

        // Persistiert die Team<->Suchgebiet-Relation beidseitig (SearchArea + Team).
        await EinsatzService.AssignTeamToSearchAreaAsync(_currentArea.Id, selectedTeamId);

        _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();

        // Alte, ungespeicherte Zeichnung löschen
        await JSRuntime.InvokeVoidAsync("LeafletMap.clearAllDrawings", "einsatzMap");

        _lastDrawnGeoJson = "";
        _drawingSaved = true; // Flag setzen, damit CloseDialog nicht erneut löscht
        await CloseDialog();
    }

    private void DeleteSearchArea(SearchArea area)
    {
        _deleteCandidate = area;
    }

    private async Task ConfirmDeleteSearchAreaAsync()
    {
        if (_deleteCandidate == null) return;
        var area = _deleteCandidate;
        _deleteCandidate = null;
        await EinsatzService.DeleteSearchAreaAsync(area.Id);
        await JSRuntime.InvokeVoidAsync("LeafletMap.removeSearchArea", "einsatzMap", area.Id);
        _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();
    }

    private async Task DownloadGpx(SearchArea area)
    {
        if (area.Coordinates == null || area.Coordinates.Count < 2)
        {
            SetSearchMessage("Suchgebiet hat nicht genügend Koordinaten für einen GPX-Track.");
            StateHasChanged();
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("downloadFile",
                GpxBuilder.SearchAreaFileName(area),
                GpxBuilder.BuildSearchAreaGpx(area),
                "application/gpx+xml");
        }
        catch (Exception ex)
        {
            _searchMessage = $"Fehler beim GPX-Export: {ex.Message}";
            Logger.LogWarning(ex, "GPX-Export Fehler");
            StateHasChanged();
        }
    }

    private async Task ZoomToArea(SearchArea area)
    {
        if (!string.IsNullOrWhiteSpace(area.Id))
        {
            var zoomed = await JSRuntime.InvokeAsync<bool>("LeafletMap.zoomToSearchArea", "einsatzMap", area.Id);
            if (zoomed)
            {
                return;
            }
        }

        if (area.Coordinates.Any())
        {
            var avgLat = area.Coordinates.Average(c => c.Latitude);
            var avgLng = area.Coordinates.Average(c => c.Longitude);
            await JSRuntime.InvokeVoidAsync("LeafletMap.centerMap",
                "einsatzMap", avgLat, avgLng, 15);
        }
    }

    private async Task ToggleSidebar()
    {
        _sidebarMinimized = !_sidebarMinimized;
        // CSS-Transition dauert 250ms – danach Kartengröße aktualisieren
        await Task.Delay(300);
        await JSRuntime.InvokeVoidAsync("LeafletMap.invalidateSize", "einsatzMap");
    }

    private async Task ExpandAndZoom(SearchArea area)
    {
        _sidebarMinimized = false;
        await Task.Delay(300);
        await JSRuntime.InvokeVoidAsync("LeafletMap.invalidateSize", "einsatzMap");
        await ZoomToArea(area);
    }

    private async Task CloseDialog()
    {
        _showDialog = false;
        _editingArea = null;

        // Wenn Dialog geschlossen wird UND die Zeichnung wurde NICHT gespeichert:
        // Ungespeicherte Zeichnung entfernen (damit nicht mehrere Zeichnungen auf der Karte bleiben)
        if (!_drawingSaved && !string.IsNullOrWhiteSpace(_lastDrawnGeoJson))
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.clearAllDrawings", "einsatzMap");
            _lastDrawnGeoJson = "";
            SetSearchMessage("Zeichnung verworfen. Sie können ein neues Suchgebiet zeichnen.");
            StateHasChanged();
        }

        // Flag zurücksetzen für nächste Zeichnung
        _drawingSaved = false;
    }

    private void GoBack()
    {
        Navigation.NavigateTo("/einsatz-monitor");
    }

    private void NavigateToTeamInMonitor(string teamId)
    {
        Navigation.NavigateTo($"/einsatz-monitor?scrollToTeam={teamId}");
    }

    private async Task SetElwPosition()
    {
        try
        {
            // Hole aktuelle Karten-Mitte
            var center = await JSRuntime.InvokeAsync<MapCenter>("LeafletMap.getMapCenter", "einsatzMap");

            // Verwende aktuelle Karten-Mitte
            var result = await JSRuntime.InvokeAsync<bool>("confirm",
                $"ELW-Position auf aktuelle Karten-Mitte setzen?\n(Lat: {center.Lat:F5}, Lng: {center.Lng:F5})");

            if (result)
            {
                // Setze ELW-Marker (draggable)
                var success = await JSRuntime.InvokeAsync<bool>("LeafletMap.setMarker",
                    "einsatzMap", "elw", center.Lat, center.Lng, "ELW (Einsatzleitwagen)", "#FF0000");

                if (success)
                {
                    // Speichere Position im Einsatz (löst EinsatzChanged aus)
                    await EinsatzService.SetElwPositionAsync(center.Lat, center.Lng);
                    _hasElwPosition = true;
                    SetSearchMessage($"? ELW-Position wurde gesetzt und gespeichert (Lat: {center.Lat:F5}, Lng: {center.Lng:F5})");
                    StateHasChanged();
                }
                else
                {
                    SetSearchMessage("? Fehler beim Setzen der ELW-Position");
                }
            }
        }
        catch (Exception ex)
        {
            _searchMessage = $"Fehler: {ex.Message}";
            Logger.LogWarning(ex, "ELW-Marker Fehler");
        }
    }

    private string BuildKarteUrl()
    {
        var teamParam = string.IsNullOrWhiteSpace(_karteTeamFilter) ? "" : $"&teamId={Uri.EscapeDataString(_karteTeamFilter)}";
        var showAreas = _karteShowSearchAreas ? "" : "&showSearchAreas=false";
        var showPoints = _karteShowPoints ? "" : "&showPoints=false";
        var showGps = _karteShowGps ? "&showGps=true" : "";
        var showPhone = _karteShowPhone ? "&showPhone=true" : "";
        var gridType = _karteGridType != "none" ? $"&gridType={_karteGridType}" : "";
        var zoomMode = $"&zoomMode={_karteZoomMode}";
        var viewportParams = "";
        if (_karteZoomMode == "viewport" && _liveMapCenterLat.HasValue && _liveMapCenterLng.HasValue && _liveMapZoom.HasValue)
        {
            viewportParams = $"&centerLat={_liveMapCenterLat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&centerLng={_liveMapCenterLng.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&zoom={_liveMapZoom.Value}";
        }
        return $"/downloads/einsatz-karte.pdf?mapType={_karteTileType}{teamParam}{showAreas}{showPoints}{showGps}{showPhone}{gridType}{zoomMode}{viewportParams}";
    }

    private async Task OpenPrintDialogAsync()
    {
        try
        {
            var viewport = await JSRuntime.InvokeAsync<MapViewport>("LeafletMap.getMapViewport", "einsatzMap");
            _liveMapCenterLat = viewport.Lat;
            _liveMapCenterLng = viewport.Lng;
            _liveMapZoom = viewport.Zoom;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Fehler beim Abrufen des Viewports");
        }
        _showKarteDialog = true;
    }

    private void SyncPrintDialogFromMap()
    {
        _karteTileType = _mapBaseLayerType;
        _karteGridType = _gridLayerType;
        _karteShowSearchAreas = _searchAreasVisible;
        _karteShowPoints = _pointMarkersVisible;
        _karteShowGps = _trackingVisible;
        _karteShowPhone = _phoneLayerVisible;
        _karteZoomMode = "viewport";
    }

    private void HandleTeamFilterChanged(ChangeEventArgs e)
    {
        _karteTeamFilter = e.Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_karteTeamFilter) && _karteZoomMode == "team")
        {
            _karteZoomMode = "all";
        }
    }

    // Draw-Modi aktivieren
    private async Task ActivateDrawPolygon()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.activateDrawMode", "einsatzMap", "polygon");
            SetSearchMessage("Zeichenmodus aktiviert: Klicken Sie auf die Karte, um Punkte zu setzen. Doppelklick zum Beenden.");
            _drawingActive = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _searchMessage = $"Fehler: {ex.Message}";
            Logger.LogWarning(ex, "Unbekannter Kartenfehler");
        }
    }

    private async Task CancelDrawPolygon()
    {
        _drawingActive = false;
        try { await JSRuntime.InvokeVoidAsync("LeafletMap.cancelDrawMode", "einsatzMap"); }
        catch { }
        StateHasChanged();
    }

    [JSInvokable]
    public Task OnDrawCanceled()
    {
        _drawingActive = false;
        return InvokeAsync(StateHasChanged);
    }

    private static readonly string[] ColorPalette = [
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#FFA07A", "#98D8C8",
        "#F7DC6F", "#BB8FCE", "#85C1E2", "#FF8800", "#44FF88"
    ];

    private static string GetRandomColor() => ColorPalette[Random.Shared.Next(ColorPalette.Length)];

    // --- Koordinaten-Marker Methoden ---

    private async Task ToggleMarkerPanel()
    {
        await SetSidebarTabAsync("points");
    }

    private async Task SetSidebarTabAsync(string tab)
    {
        // Aktiven Tab erneut klicken → Sidebar ein-/ausklappen
        if (tab == _activeSidebarTab && !_sidebarMinimized)
        {
            _sidebarMinimized = true;
            await Task.Delay(300);
            await JSRuntime.InvokeVoidAsync("LeafletMap.invalidateSize", "einsatzMap");
            return;
        }

        _activeSidebarTab = tab;
        if (_sidebarMinimized)
        {
            _sidebarMinimized = false;
            await Task.Delay(300);
            await JSRuntime.InvokeVoidAsync("LeafletMap.invalidateSize", "einsatzMap");
        }
        if (tab == "gps" && !_trackingVisible)
        {
            await ToggleTrackingLayer();
        }
        if (tab != "points" && _clickToPlaceActive)
        {
            await DeactivateClickToPlaceMode();
        }
    }

    // --- Callbacks für KartePunkteTab ---

    private void OnCoordInputModeChanged(string mode)
    {
        _coordInputMode = mode;
    }

    private void OnPunkteMarkersChanged()
    {
        _mapMarkers = EinsatzService.CurrentEinsatz.MapMarkers.ToList();
        StateHasChanged();
    }

    // --- Callbacks für KarteGpsTab ---

    private async Task HandleToggleTrackVisibility((string Id, bool Visible) args)
    {
        await ToggleCompletedTrackAsync(args.Id, args.Visible);
    }

    private async Task HandleGpxImportedAsync(KarteGpsTab.GpxImportRequest request)
    {
        if (request.CompletedSearchId != null)
        {
            // Track in bestehende Suche importieren
            await EinsatzService.AddTrackToCompletedSearchAsync(request.CompletedSearchId, request.Snapshot);
        }
        else
        {
            // Neue Suche anlegen und Track hinzufügen
            var newSearch = await EinsatzService.CreateCompletedSearchAsync(
                request.Snapshot.TeamId,
                request.SearchStart!.Value,
                request.SearchEnd!.Value,
                request.SearchAreaId);
            await EinsatzService.AddTrackToCompletedSearchAsync(newSearch.Id, request.Snapshot);
        }
        // Das TrackSnapshotAdded-Event löst OnTrackSnapshotSaved aus, welches die Karte aktualisiert.
        // Sidebar-Tab GPS aktivieren damit der neue Track sichtbar wird.
        await SetSidebarTabAsync("gps");
    }

    private async Task ActivateClickToPlaceMode()
    {
        _coordInputMode = "click";
        _clickToPlaceActive = true;

        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.enableCoordinateClickMode", "einsatzMap");
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Aktivieren des Klick-Modus: {ex.Message}");
            _clickToPlaceActive = false;
        }
    }

    private async Task DeactivateClickToPlaceMode()
    {
        _clickToPlaceActive = false;
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.disableCoordinateClickMode", "einsatzMap");
        }
        catch { /* ignore */ }
    }

    [JSInvokable]
    public async Task OnCoordinateMarkerDragCompleted(string markerId, double lat, double lng)
    {
        await InvokeAsync(() =>
        {
            _punkteTab?.OpenEditDialogFromDrag(markerId, lat, lng);
            StateHasChanged();
        });
    }

    [JSInvokable]
    public async Task OnCoordinateMarkerClicked(double lat, double lng)
    {
        _clickToPlaceActive = false;
        if (_punkteTab != null)
        {
            var label = _punkteTab.InputLabel;
            _punkteTab.ClearInputLabel();
            await InvokeAsync(async () =>
            {
                await _punkteTab.PlaceMarkerAtPositionAsync(lat, lng, label);
                _mapMarkers = EinsatzService.CurrentEinsatz.MapMarkers.ToList();
                StateHasChanged();
            });
        }
        else
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    // --- Embed-Control-Handler (für /el-Karte-Tab) ---

    private async Task SetEmbedTileType(string type)
    {
        _mapBaseLayerType = type;
        try { await JSRuntime.InvokeVoidAsync("LeafletMap.changeBaseLayer", "einsatzMap", type); }
        catch (Exception ex) { Logger.LogWarning(ex, "changeBaseLayer fehlgeschlagen"); }
    }

    private Task OnTopbarSearchTextChanged(string value)
    {
        _addressSearch = value;
        return Task.CompletedTask;
    }

    private void ConfigureMissionTopbarContent()
    {
        MissionTopbar.SetContent(this, builder =>
        {
            builder.OpenComponent<KarteTopbarSearch>(0);
            builder.AddAttribute(1, nameof(KarteTopbarSearch.SearchText), _addressSearch);
            builder.AddAttribute(2, nameof(KarteTopbarSearch.SearchTextChanged), EventCallback.Factory.Create<string>(this, OnTopbarSearchTextChanged));
            builder.AddAttribute(3, nameof(KarteTopbarSearch.OnSearch), EventCallback.Factory.Create(this, SearchAddress));
            builder.CloseComponent();
        });
    }

    private async Task ChangeMapBaseLayerAsync(string type)
    {
        await SetEmbedTileType(type);
    }

    private async Task ChangeGridLayerAsync(string type)
    {
        _gridLayerType = type;
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.changeGridLayer", "einsatzMap", type);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "changeGridLayer fehlgeschlagen");
        }
    }

    private async Task ToggleSearchAreasVisibility()
    {
        _searchAreasVisible = !_searchAreasVisible;
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.toggleSearchAreas", "einsatzMap", _searchAreasVisible);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "toggleSearchAreas fehlgeschlagen");
        }
    }

    private async Task TogglePointMarkersVisibility()
    {
        _pointMarkersVisible = !_pointMarkersVisible;
        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.toggleCoordinateMarkers", "einsatzMap", _pointMarkersVisible);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "toggleCoordinateMarkers fehlgeschlagen");
        }
    }

    private void ToggleTileMenu()
    {
        _mapTileMenuExpanded = !_mapTileMenuExpanded;
        if (_mapTileMenuExpanded)
        {
            _mapGridMenuExpanded = false;
            _mapContentMenuExpanded = false;
        }
    }

    private void ToggleGridMenu()
    {
        _mapGridMenuExpanded = !_mapGridMenuExpanded;
        if (_mapGridMenuExpanded)
        {
            _mapTileMenuExpanded = false;
            _mapContentMenuExpanded = false;
        }
    }

    private void ToggleMapContentMenu()
    {
        _mapContentMenuExpanded = !_mapContentMenuExpanded;
        if (_mapContentMenuExpanded)
        {
            _mapTileMenuExpanded = false;
            _mapGridMenuExpanded = false;
        }
    }

    private async Task RecenterMap()
    {
        try { await JSRuntime.InvokeVoidAsync("LeafletMap.fitAllElements", "einsatzMap", 40); }
        catch (Exception ex) { Logger.LogWarning(ex, "fitAllElements fehlgeschlagen"); }
    }

    // --- Collar-Tracking Methoden ---

    private async Task ToggleTrackingLayer()
    {
        _trackingVisible = !_trackingVisible;
        _collars = CollarTrackingService.Collars.ToList();
        await JSRuntime.InvokeVoidAsync("CollarTracking.toggleVisibility", "einsatzMap", _trackingVisible);

        // Bestehende Pfade laden fuer alle Halsbaender
        if (_trackingVisible)
        {
            foreach (var collar in _collars)
            {
                var history = CollarTrackingService.GetLocationHistory(collar.Id);
                if (history.Count > 0)
                {
                    var color = GetCollarColor(collar.Id);
                    var dogLabel = GetDogLabelForCollar(collar.Id);
                    await JSRuntime.InvokeVoidAsync("CollarTracking.loadHistory",
                        "einsatzMap", collar.Id, history, color, dogLabel);
                }
            }

            // Abgeschlossene Tracks einblenden/synchronisieren
            var snapshots = GetAllTrackSnapshotsForMap();
            if (snapshots != null)
            {
                foreach (var snap in snapshots)
                {
                    if (!_completedTrackVisibility.ContainsKey(snap.Id))
                        _completedTrackVisibility[snap.Id] = true;

                    if (_completedTrackVisibility[snap.Id])
                    {
                        await JSRuntime.InvokeVoidAsync("CollarTracking.addCompletedTrack",
                            "einsatzMap", snap.Id, snap.Points, snap.Color,
                            snap.TeamName, snap.DisplayLabel, snap.TrackType.ToString());
                    }
                }
            }
        }
    }

    private List<TeamTrackSnapshot> GetAllTrackSnapshotsForMap()
    {
        var result = new List<TeamTrackSnapshot>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var search in EinsatzService.CurrentEinsatz.CompletedSearches ?? Enumerable.Empty<CompletedSearch>())
        {
            foreach (var track in search.Tracks)
            {
                if (string.IsNullOrWhiteSpace(track.Id))
                {
                    result.Add(track);
                    continue;
                }

                if (seenIds.Add(track.Id))
                    result.Add(track);
            }
        }

        foreach (var track in EinsatzService.CurrentEinsatz.TrackSnapshots ?? Enumerable.Empty<TeamTrackSnapshot>())
        {
            if (string.IsNullOrWhiteSpace(track.Id))
            {
                result.Add(track);
                continue;
            }

            if (seenIds.Add(track.Id))
                result.Add(track);
        }

        return result;
    }

    // Legacy alias (called from ToggleTracking keyboard shortcut etc.)
    private async Task ToggleTracking() => await SetSidebarTabAsync("gps");

    private async Task TogglePhoneLayer()
    {
        _phoneLayerVisible = !_phoneLayerVisible;
        await JSRuntime.InvokeVoidAsync("PhoneTracking.toggleVisibility", "einsatzMap", _phoneLayerVisible);

        if (_phoneLayerVisible)
        {
            foreach (var (teamId, loc) in EinsatzService.PhoneLocations)
            {
                var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
                if (team == null) continue;
                await JSRuntime.InvokeVoidAsync("PhoneTracking.updateMarker",
                    "einsatzMap", teamId, team.TeamName, loc.Latitude, loc.Longitude, loc.Timestamp);
            }

            await LoadRunningTeamPhoneTracksAsync("einsatzMap");
        }
    }

    private async Task LoadRunningTeamPhoneTracksAsync(string mapId)
    {
        foreach (var runningTeam in _teams.Where(t => t.IsRunning))
        {
            var phoneHistory = EinsatzService.GetPhoneTrackHistory(runningTeam.TeamId);
            if (phoneHistory.Count >= 2)
            {
                var teamColor = GetTeamPhoneTrackColor(runningTeam);
                var pts = phoneHistory.Select(p => new { lat = p.Latitude, lng = p.Longitude }).ToArray();
                await JSRuntime.InvokeVoidAsync("PhoneTracking.loadTrack", mapId, runningTeam.TeamId, pts, teamColor);
            }
        }
    }

    private void OnTeamPhoneLocationChanged(string teamId, string teamName, TeamPhoneLocation location)
    {
        // Immer aktualisieren: Daten werden auch während Layer inaktiv ist gepuffert
        _ = InvokeAsync(async () =>
        {
            try
            {
                // Falls die PhoneLayer noch nicht initialisiert ist, initialisieren
                if (!_mapInitialized) return;
                await JSRuntime.InvokeVoidAsync("PhoneTracking.updateMarker",
                    "einsatzMap", teamId, teamName, location.Latitude, location.Longitude, location.Timestamp);
                StateHasChanged(); // Handy-Tab-Anzeige (letzte Position, Alter) aktualisieren
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void OnTeamPhoneTrackPointAdded(string teamId, string teamName, TeamPhoneLocation location)
    {
        // Immer aktualisieren: Live-Track sollte auch angezeigt werden wenn bereits sichtbar
        _ = InvokeAsync(async () =>
        {
            try
            {
                // Falls die PhoneLayer noch nicht initialisiert ist, initialisieren
                if (!_mapInitialized) return;
                await JSRuntime.InvokeVoidAsync("PhoneTracking.appendTrackPoint", "einsatzMap", teamId, location.Latitude, location.Longitude);
            }
            catch (ObjectDisposedException) { }
        });
    }

    private string GetTeamPhoneTrackColor(Team team)
    {
        var area = _searchAreas.FirstOrDefault(a => a.AssignedTeamId == team.TeamId);
        return area?.Color ?? "#1976D2";
    }

    private string GetCollarColor(string collarId)
    {
        // Halsband → Team → Suchgebiet → Suchgebiet-Farbe
        var collar = _collars.FirstOrDefault(c => c.Id == collarId);
        if (collar is { IsAssigned: true, AssignedTeamId: not null })
        {
            var area = _searchAreas.FirstOrDefault(a => a.AssignedTeamId == collar.AssignedTeamId);
            if (area != null)
                return area.Color;
        }

        // Ersten 10: feste Farbe nach Listenposition (deterministisch, keine Kollisionen)
        var idx = _collars.FindIndex(c => c.Id == collarId);
        if (idx >= 0 && idx < ColorPalette.Length)
            return ColorPalette[idx];

        return ColorPalette[Math.Abs(collarId.GetHashCode()) % ColorPalette.Length];
    }

    private async Task ZoomToCollarAsync(Collar collar)
    {
        // Passt die Kartenansicht so an, dass der gesamte aufgezeichnete Live-Track sichtbar ist
        await JSRuntime.InvokeVoidAsync("CollarTracking.zoomToCollar", "einsatzMap", collar.Id);
    }

    private async Task ZoomToPhoneTeamAsync(string teamId)
    {
        // Layer einblenden falls noch nicht aktiv
        if (!_phoneLayerVisible)
        {
            _phoneLayerVisible = true;
            await JSRuntime.InvokeVoidAsync("PhoneTracking.toggleVisibility", "einsatzMap", true);
            var teamLookup = _teams.ToDictionary(t => t.TeamId);
            foreach (var (tid, loc) in EinsatzService.PhoneLocations)
            {
                if (!teamLookup.TryGetValue(tid, out var team)) continue;
                await JSRuntime.InvokeVoidAsync("PhoneTracking.updateMarker",
                    "einsatzMap", tid, team.TeamName, loc.Latitude, loc.Longitude, loc.Timestamp);
            }
            await LoadRunningTeamPhoneTracksAsync("einsatzMap");
        }
        await JSRuntime.InvokeVoidAsync("PhoneTracking.zoomToTeam", "einsatzMap", teamId);
    }

    private async Task ZoomToCompletedTrackAsync(string snapshotId)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("CollarTracking.zoomToCompletedTrack", "einsatzMap", snapshotId);
        }
        catch (Exception) { }
    }

    private async Task ApplyCollarFocusFromQueryAsync()
    {
        if (!_mapInitialized || _collarFocusApplyRunning || string.IsNullOrWhiteSpace(_pendingFocusCollarId))
        {
            return;
        }

        var focusCollarId = _pendingFocusCollarId;

        _collarFocusApplyRunning = true;
        await SetSidebarTabAsync("gps");

        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(120 + (attempt * 80));

                var focused = await JSRuntime.InvokeAsync<bool>("CollarTracking.zoomToCollar", "einsatzMap", focusCollarId);
                if (focused)
                {
                    _lastAppliedFocusCollarId = focusCollarId;
                    _pendingFocusCollarId = null;
                    return;
                }
            }
        }
        finally
        {
            _collarFocusApplyRunning = false;
        }
    }

    private async Task ApplyAreaFocusFromQueryAsync()
    {
        if (!_mapInitialized || _areaFocusApplyRunning || string.IsNullOrWhiteSpace(_pendingFocusAreaId))
            return;

        var focusAreaId = _pendingFocusAreaId;

        _areaFocusApplyRunning = true;
        try
        {
            var area = _searchAreas.FirstOrDefault(a => a.Id == focusAreaId);
            if (area != null)
            {
                await SetSidebarTabAsync("areas");
                await ZoomToArea(area);
                _lastAppliedFocusAreaId = focusAreaId;
                _pendingFocusAreaId = null;
            }
        }
        finally
        {
            _areaFocusApplyRunning = false;
        }
    }

    /// <summary>Gibt "Hundename (Teamname)" für einen Collar zurück, oder collarId als Fallback.</summary>
    private string GetDogLabelForCollar(string collarId)
    {
        var collar = _collars.FirstOrDefault(c => c.Id == collarId);
        if (collar is { IsAssigned: true, AssignedTeamId: not null })
        {
            var team = _teams.FirstOrDefault(t => t.TeamId == collar.AssignedTeamId);
            if (team != null)
            {
                var dog = string.IsNullOrWhiteSpace(team.DogName) ? null : team.DogName;
                return dog != null ? $"{dog} ({team.TeamName})" : team.TeamName;
            }
        }
        return collarId;
    }

    // Domain-Event Handler für GPS-Positionen
    private void OnCollarLocationReceived(string collarId, CollarLocation location)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                _collars = CollarTrackingService.Collars.ToList();
                var color = GetCollarColor(collarId);
                var dogLabel = GetDogLabelForCollar(collarId);
                await JSRuntime.InvokeVoidAsync("CollarTracking.updatePosition",
                    "einsatzMap", collarId, location.Latitude, location.Longitude, location.Timestamp, color, dogLabel);
                _collarLastLocations[collarId] = location;

                // Direkte Geometrie-Prüfung: ist der Hund (wieder) im Suchgebiet?
                if (_oobWarnings.ContainsKey(collarId) &&
                    IsCollarInsideAssignedArea(collarId, location.Latitude, location.Longitude))
                {
                    _oobWarnings.Remove(collarId);
                    await JSRuntime.InvokeVoidAsync("CollarTracking.clearOobWarning",
                        "einsatzMap", collarId);
                }

                StateHasChanged();
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void OnOutOfBoundsDetected(string teamId, string collarId, CollarLocation location)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
                _oobWarnings[collarId] = $"⚠ {team?.TeamName ?? teamId}: Hund hat Suchgebiet verlassen!";

                if (_trackingVisible)
                {
                    await JSRuntime.InvokeVoidAsync("CollarTracking.showOutOfBoundsWarning",
                        "einsatzMap", collarId, teamId, location.Latitude, location.Longitude);
                }

                StateHasChanged();
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void OnCollarHistoryCleared(string collarId)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("CollarTracking.clearTrack", "einsatzMap", collarId);
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void OnTrackSnapshotSaved(TeamTrackSnapshot snapshot)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                _completedTrackVisibility[snapshot.Id] = true;
                if (_trackingVisible)
                {
                    await JSRuntime.InvokeVoidAsync("CollarTracking.addCompletedTrack",
                        "einsatzMap", snapshot.Id, snapshot.Points, snapshot.Color,
                        snapshot.TeamName, snapshot.DisplayLabel, snapshot.TrackType.ToString());
                }
                StateHasChanged();
            }
            catch (ObjectDisposedException) { }
        });
    }

    private async Task ToggleCompletedTrackAsync(string snapshotId, bool visible)
    {
        _completedTrackVisibility[snapshotId] = visible;
        await JSRuntime.InvokeVoidAsync("CollarTracking.toggleCompletedTrack", "einsatzMap", snapshotId, visible);
    }

    [Microsoft.JSInterop.JSInvokable]
    public async Task OnCompletedTrackClicked(string snapshotId)
    {
        // GPS-Tab öffnen (aktiviert Tracking-Layer falls noch aus) und Snapshot ausklappen
        await SetSidebarTabAsync("gps");
        _expandedSnapshotId = snapshotId;
        // Karte auf den Track zoomen
        try
        {
            await JSRuntime.InvokeVoidAsync("CollarTracking.zoomToCompletedTrack", "einsatzMap", snapshotId);
        }
        catch (Exception) { }
        await InvokeAsync(StateHasChanged);
    }

    private void ExtractCoordinatesFromGeoJson(SearchArea area)
    {
        try
        {
            // Einfache Extraktion der Koordinaten aus GeoJSON
            // Format: {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[lng,lat],[lng,lat],...]]}}
            var geoJson = System.Text.Json.JsonDocument.Parse(area.GeoJsonData);

            if (geoJson.RootElement.TryGetProperty("geometry", out var geometry))
            {
                if (geometry.TryGetProperty("coordinates", out var coordinates))
                {
                    area.Coordinates = new List<(double, double)>();

                    // Bei Polygonen: erste Array-Ebene sind die Ringe, zweite Ebene die Koordinaten
                    var firstRing = coordinates[0];
                    foreach (var coord in firstRing.EnumerateArray())
                    {
                        var lng = coord[0].GetDouble();
                        var lat = coord[1].GetDouble();
                        area.Coordinates.Add((lat, lng));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Fehler beim Extrahieren der Koordinaten");
        }
    }

    // Prüft ob das Halsband innerhalb seines zugewiesenen Suchgebiets ist
    private bool IsCollarInsideAssignedArea(string collarId, double lat, double lng)
    {
        var collar = _collars.FirstOrDefault(c => c.Id == collarId);
        if (collar is not { IsAssigned: true, AssignedTeamId: not null })
            return true; // kein Team = kann nicht OOB sein

        var team = _teams.FirstOrDefault(t => t.TeamId == collar.AssignedTeamId);
        if (team == null || string.IsNullOrWhiteSpace(team.SearchAreaId))
            return true; // kein Suchgebiet zugewiesen

        var area = _searchAreas.FirstOrDefault(a => a.Id == team.SearchAreaId);
        if (area?.Coordinates == null || area.Coordinates.Count < 3)
            return true; // kein gültiges Polygon

        return IsPointInPolygon(lat, lng, area.Coordinates);
    }

    // Ray-Casting Algorithmus (identisch mit CollarTrackingService)
    private static bool IsPointInPolygon(double lat, double lng, List<(double Latitude, double Longitude)> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Longitude > lng) != (pj.Longitude > lng) &&
                lat < (pj.Latitude - pi.Latitude) * (lng - pi.Longitude) / (pj.Longitude - pi.Longitude) + pi.Latitude)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    // Callbacks von JavaScript (jetzt als Instanz-Methoden)
    [JSInvokable]
    public async Task OnShapeCreated(string geoJson)
    {
        _drawingActive = false;
        Logger.LogDebug("Shape created: {GeoJson}", geoJson);
        _lastDrawnGeoJson = geoJson;
        _drawingSaved = false; // Neue Zeichnung wurde noch nicht gespeichert

        // Dialog automatisch öffnen mit Erfolgsbestätigung
        await InvokeAsync(() =>
        {
            SetSearchMessage("✓ Suchgebiet erfolgreich gezeichnet! Bitte geben Sie die Details ein.");
            ShowAddAreaDialog();
            StateHasChanged();
        });
    }

    [JSInvokable]
    public async Task OnElwMarkerMoved(double lat, double lng)
    {
        Logger.LogDebug("ELW moved to: {Lat}, {Lng}", lat, lng);

        // Speichere neue Position (löst EinsatzChanged aus → Persistierung)
        await EinsatzService.SetElwPositionAsync(lat, lng);
        await InvokeAsync(() =>
        {
            SetSearchMessage($"? ELW verschoben zu (Lat: {lat:F5}, Lng: {lng:F5})");
            StateHasChanged();
        });
    }

    [JSInvokable]
    public async Task OnShapeEdited(string areaId, string geoJson)
    {
        if (string.IsNullOrWhiteSpace(areaId))
        {
            return;
        }

        var area = EinsatzService.CurrentEinsatz.SearchAreas.FirstOrDefault(a => a.Id == areaId);
        if (area is null)
        {
            return;
        }

        area.GeoJsonData = geoJson;
        ExtractCoordinatesFromGeoJson(area);
        await EinsatzService.UpdateSearchAreaAsync(area);
        _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();

        // Polygon auf der Karte mit neuem GeoJSON und Originalfarbe/-name neu rendern
        await JSRuntime.InvokeVoidAsync("LeafletMap.removeSearchArea", "einsatzMap", areaId);
        await JSRuntime.InvokeVoidAsync("LeafletMap.addSearchArea",
            "einsatzMap", areaId, geoJson, area.Color, area.Name);

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnShapeDeleted(string areaId, string geoJson)
    {
        if (string.IsNullOrWhiteSpace(areaId))
        {
            return;
        }

        await EinsatzService.DeleteSearchAreaAsync(areaId);
        _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();

        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            MissionTopbar.ClearContent(this);
            CollarTrackingService.CollarLocationReceived -= OnCollarLocationReceived;
            CollarTrackingService.OutOfBoundsDetected -= OnOutOfBoundsDetected;
            CollarTrackingService.CollarHistoryCleared -= OnCollarHistoryCleared;
            CollarTrackingService.TrackSnapshotSaved -= OnTrackSnapshotSaved;
            EinsatzService.TeamPhoneLocationChanged -= OnTeamPhoneLocationChanged;
            EinsatzService.TeamPhoneTrackPointAdded -= OnTeamPhoneTrackPointAdded;
            EinsatzService.TrackSnapshotAdded -= OnTrackSnapshotSaved;
            await JSRuntime.InvokeVoidAsync("LeafletMap.dispose", "einsatzMap");
            _dotNetReference?.Dispose();
        }
        catch
        {
            // Ignoriere Fehler beim Dispose
        }
    }

    // Helper-Klasse für Geocoding-Result
    private class GeocodeResult
    {
        public bool Success { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string DisplayName { get; set; } = "";
        public string Message { get; set; } = "";
    }

    // Helper-Klasse für Map-Center
    private class MapCenter
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    // Helper-Klasse für Map-Viewport
    private class MapViewport
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int Zoom { get; set; }
    }
}
