using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Lage : IAsyncDisposable
{
    [Inject] IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] ICollarTrackingService CollarTrackingService { get; set; } = default!;
    [Inject] ISettingsService SettingsService { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] ILogger<Lage> Logger { get; set; } = default!;

    private List<SearchArea> _searchAreas = new();
    private List<Team> _teams = new();
    private List<Collar> _collars = new();

    private DotNetObjectReference<Lage>? _dotNetReference;
    private System.Threading.Timer? _clockTimer;
    private DateTime _now = DateTime.Now;
    private bool _mapInitialized;

    // Standardposition (Speyer) als Fallback
    private double _mapCenterLat = 49.3188;
    private double _mapCenterLng = 8.4312;
    private int _mapZoom = 13;
    private bool _hasElwPosition;

    // Karten-Marker Konfiguration (aus AppSettings geladen)
    private string _collarMarkerIcon = "paw";
    private string _humanMarkerIcon = "phone";
    private string _collarTrackColorMode = "area";
    private string _collarMarkerColorMode = "area";

    private string _einsatzTitle = "—";
    private string _vermisstName = "—";
    private string _einsatzDauer = "—";
    private int _activeTeamCount;
    private int _droneCount;

    protected override async Task OnInitializedAsync()
    {
        var einsatz = EinsatzService.CurrentEinsatz;
        _searchAreas = einsatz.SearchAreas.ToList();
        _teams = EinsatzService.Teams;
        _collars = CollarTrackingService.Collars.ToList();
        _dotNetReference = DotNetObjectReference.Create(this);

        var settings = await SettingsService.GetAppSettingsAsync();
        _mapCenterLat = settings.MapDefaultLat;
        _mapCenterLng = settings.MapDefaultLng;
        _mapZoom = settings.MapDefaultZoom;
        _collarMarkerIcon = settings.CollarMarkerIconOrDefault;
        _humanMarkerIcon = settings.HumanMarkerIconOrDefault;
        _collarTrackColorMode = settings.CollarTrackColorModeOrDefault;
        _collarMarkerColorMode = settings.CollarMarkerColorModeOrDefault;

        if (einsatz.ElwPosition.HasValue)
        {
            _hasElwPosition = true;
            _mapCenterLat = einsatz.ElwPosition.Value.Latitude;
            _mapCenterLng = einsatz.ElwPosition.Value.Longitude;
        }

        RecomputeHeader();

        EinsatzService.EinsatzChanged += OnEinsatzChanged;
        EinsatzService.TeamAdded += OnTeamCollectionChanged;
        EinsatzService.TeamRemoved += OnTeamCollectionChanged;
        EinsatzService.TeamUpdated += OnTeamCollectionChanged;
        EinsatzService.VermisstenInfoChanged += OnEinsatzChanged;
        CollarTrackingService.CollarLocationReceived += OnCollarLocationReceived;
        EinsatzService.TeamPhoneLocationChanged += OnTeamPhoneLocationChanged;
        EinsatzService.TeamPhoneTrackPointAdded += OnTeamPhoneTrackPointAdded;

        _clockTimer = new System.Threading.Timer(_ =>
        {
            _now = DateTime.Now;
            RecomputeHeader();
            _ = InvokeAsync(StateHasChanged);
        }, null, 1000, 1000);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            await JSRuntime.InvokeVoidAsync("LeafletMap.initialize",
                "lageMap", _mapCenterLat, _mapCenterLng, _mapZoom, _dotNetReference);

            // Topo-Layer als Standard fuer das Lagebild
            try
            {
                await JSRuntime.InvokeVoidAsync("LeafletMap.changeBaseLayer", "lageMap", "topo");
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Topo-Layer nicht verfuegbar, bleibe bei Default");
            }

            // Suchgebiete einzeichnen
            var renderedAreas = 0;
            foreach (var area in _searchAreas.Where(a => !string.IsNullOrWhiteSpace(a.GeoJsonData)))
            {
                await JSRuntime.InvokeVoidAsync("LeafletMap.addSearchArea",
                    "lageMap", area.Id, area.GeoJsonData, area.Color, area.Name);
                renderedAreas++;
            }

            // Karte so zoomen, dass alle Suchgebiete sichtbar sind
            if (renderedAreas > 0)
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("LeafletMap.fitAllElements", "lageMap", 40);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "fitAllElements fehlgeschlagen");
                }
            }

            // ELW-Marker
            if (_hasElwPosition && EinsatzService.CurrentEinsatz.ElwPosition.HasValue)
            {
                var elw = EinsatzService.CurrentEinsatz.ElwPosition.Value;
                await JSRuntime.InvokeVoidAsync("LeafletMap.setMarker",
                    "lageMap", "elw", elw.Latitude, elw.Longitude, "ELW", "#FF0000");
            }

            // Live-Tracking aktivieren
            await JSRuntime.InvokeVoidAsync("CollarTracking.initialize", "lageMap", _dotNetReference);
            await JSRuntime.InvokeVoidAsync("CollarTracking.setOptions", new
            {
                collarIcon = _collarMarkerIcon,
                trackColorMode = _collarTrackColorMode,
                markerColorMode = _collarMarkerColorMode
            });
            await JSRuntime.InvokeVoidAsync("CollarTracking.toggleVisibility", "lageMap", true);
            await JSRuntime.InvokeVoidAsync("PhoneTracking.initialize", "lageMap");
            await JSRuntime.InvokeVoidAsync("PhoneTracking.setOptions", new { humanIcon = _humanMarkerIcon });
            await JSRuntime.InvokeVoidAsync("PhoneTracking.toggleVisibility", "lageMap", true);

            // Bestehende Halsband-Tracks nachladen
            foreach (var collar in _collars)
            {
                var history = CollarTrackingService.GetLocationHistory(collar.Id);
                if (history.Count == 0) continue;
                var color = GetCollarColor(collar.Id);
                var label = GetDogLabelForCollar(collar.Id);
                await JSRuntime.InvokeVoidAsync("CollarTracking.loadHistory",
                    "lageMap", collar.Id, history, color, label);
            }

            // Bestehende Phone-Marker setzen
            foreach (var (teamId, loc) in EinsatzService.PhoneLocations)
            {
                var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
                if (team is null) continue;
                await JSRuntime.InvokeVoidAsync("PhoneTracking.updateMarker",
                    "lageMap", teamId, team.TeamName, loc.Latitude, loc.Longitude, loc.Timestamp);
            }

            // Bestehende Telefon-Tracks laufender Teams laden
            foreach (var runningTeam in _teams.Where(t => t.IsRunning))
            {
                var phoneHistory = EinsatzService.GetPhoneTrackHistory(runningTeam.TeamId);
                if (phoneHistory.Count >= 2)
                {
                    var teamColor = GetTeamPhoneTrackColor(runningTeam);
                    var pts = phoneHistory.Select(p => new { lat = p.Latitude, lng = p.Longitude }).ToArray();
                    await JSRuntime.InvokeVoidAsync("PhoneTracking.loadTrack", "lageMap", runningTeam.TeamId, pts, teamColor);
                }
            }

            _mapInitialized = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Lagebildschirm: Karten-Initialisierung fehlgeschlagen");
        }
    }

    private void RecomputeHeader()
    {
        var e = EinsatzService.CurrentEinsatz;
        var nr = string.IsNullOrWhiteSpace(e.EinsatzNummer) ? "" : $"#{e.EinsatzNummer}";
        var stichwort = string.IsNullOrWhiteSpace(e.Stichwort) ? "Einsatz" : e.Stichwort;
        _einsatzTitle = string.IsNullOrWhiteSpace(nr) ? stichwort : $"{stichwort} · {nr}";

        if (e.VermisstenInfo is { } vi)
        {
            var name = $"{vi.Vorname} {vi.Nachname}".Trim();
            _vermisstName = string.IsNullOrWhiteSpace(name) ? "—" : name;
        }
        else
        {
            _vermisstName = "—";
        }

        _einsatzDauer = e.DauerFormatiert ?? "—";
        _activeTeamCount = _teams.Count(t => t.IsRunning);
        _droneCount = _teams.Count(t => t.IsDroneTeam && !string.IsNullOrWhiteSpace(t.DroneId));
    }

    private void OnEinsatzChanged()
    {
        _ = InvokeAsync(() =>
        {
            _searchAreas = EinsatzService.CurrentEinsatz.SearchAreas.ToList();
            RecomputeHeader();
            StateHasChanged();
        });
    }

    private void OnTeamCollectionChanged(Team team)
    {
        _ = InvokeAsync(() =>
        {
            _teams = EinsatzService.Teams;
            RecomputeHeader();
            StateHasChanged();
        });
    }

    private void OnCollarLocationReceived(string collarId, CollarLocation location)
    {
        if (!_mapInitialized) return;
        _ = InvokeAsync(async () =>
        {
            try
            {
                _collars = CollarTrackingService.Collars.ToList();
                var color = GetCollarColor(collarId);
                var label = GetDogLabelForCollar(collarId);
                await JSRuntime.InvokeVoidAsync("CollarTracking.updatePosition",
                    "lageMap", collarId, location.Latitude, location.Longitude, location.Timestamp, color, label);
            }
            catch (ObjectDisposedException) { }
            catch (JSDisconnectedException) { }
        });
    }

    private void OnTeamPhoneLocationChanged(string teamId, string teamName, TeamPhoneLocation location)
    {
        if (!_mapInitialized) return;
        _ = InvokeAsync(async () =>
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("PhoneTracking.updateMarker",
                    "lageMap", teamId, teamName, location.Latitude, location.Longitude, location.Timestamp);
            }
            catch (ObjectDisposedException) { }
            catch (JSDisconnectedException) { }
        });
    }

    private void OnTeamPhoneTrackPointAdded(string teamId, string teamName, TeamPhoneLocation location)
    {
        if (!_mapInitialized) return;
        _ = InvokeAsync(async () =>
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("PhoneTracking.appendTrackPoint", "lageMap", teamId, location.Latitude, location.Longitude);
            }
            catch (ObjectDisposedException) { }
            catch (JSDisconnectedException) { }
        });
    }

    private string GetTeamPhoneTrackColor(Team team)
    {
        var area = _searchAreas.FirstOrDefault(a => a.AssignedTeamId == team.TeamId);
        return area?.Color ?? "#1976D2";
    }

    // Farbe analog EinsatzKarte: Halsband -> Team -> Suchgebiet -> Suchgebiet-Farbe
    private static readonly string[] FallbackPalette =
    {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#FFA07A", "#98D8C8",
        "#F7DC6F", "#BB8FCE", "#85C1E2", "#FF8800", "#44FF88"
    };

    private string GetCollarColor(string collarId)
    {
        var collar = _collars.FirstOrDefault(c => c.Id == collarId);
        if (collar is { IsAssigned: true, AssignedTeamId: not null })
        {
            var area = _searchAreas.FirstOrDefault(a => a.AssignedTeamId == collar.AssignedTeamId);
            if (area != null) return area.Color;
        }
        var idx = _collars.FindIndex(c => c.Id == collarId);
        if (idx >= 0 && idx < FallbackPalette.Length) return FallbackPalette[idx];
        return FallbackPalette[Math.Abs(collarId.GetHashCode()) % FallbackPalette.Length];
    }

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

    public async ValueTask DisposeAsync()
    {
        try
        {
            EinsatzService.EinsatzChanged -= OnEinsatzChanged;
            EinsatzService.TeamAdded -= OnTeamCollectionChanged;
            EinsatzService.TeamRemoved -= OnTeamCollectionChanged;
            EinsatzService.TeamUpdated -= OnTeamCollectionChanged;
            EinsatzService.VermisstenInfoChanged -= OnEinsatzChanged;
            CollarTrackingService.CollarLocationReceived -= OnCollarLocationReceived;
            EinsatzService.TeamPhoneLocationChanged -= OnTeamPhoneLocationChanged;
            EinsatzService.TeamPhoneTrackPointAdded -= OnTeamPhoneTrackPointAdded;

            _clockTimer?.Dispose();
            _clockTimer = null;

            try { await JSRuntime.InvokeVoidAsync("LeafletMap.dispose", "lageMap"); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }

            _dotNetReference?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Lagebildschirm: Dispose-Fehler ignoriert");
        }
    }
}
