using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Truemmer : IDisposable
{
    private const string MapElementId = "truemmer-map-host";

    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private TruemmerKartenSpeicher KartenSpeicher { get; set; } = default!;

    private List<TruemmerKarte> _karten = new();
    private Guid? _selectedKarteId;
    private bool _isUploading;
    private bool _isDrawing;
    private bool _mapInitialized;
    private string _status = string.Empty;
    private bool _isError;
    private DotNetObjectReference<Truemmer>? _selfRef;

    private List<TruemmerArea> SelectedAreas =>
        _selectedKarteId.HasValue
            ? (EinsatzService.CurrentEinsatz.TruemmerAreas ?? new()).Where(a => a.TruemmerKarteId == _selectedKarteId.Value).ToList()
            : new();

    protected override void OnInitialized()
    {
        EinsatzService.SzenarioChanged += OnReload;
        EinsatzService.TruemmerKarteAdded += OnKarteAdded;
        EinsatzService.TruemmerKarteRemoved += OnKarteRemoved;
        EinsatzService.TruemmerAreaUpserted += OnAreaChanged;
        EinsatzService.TruemmerAreaRemoved += OnAreaRemoved;

        ReloadKarten();
    }

    private void ReloadKarten()
    {
        _karten = (EinsatzService.CurrentEinsatz.TruemmerKarten ?? new()).OrderBy(k => k.UploadedAt).ToList();
        if (_selectedKarteId is null && _karten.Count > 0)
            _selectedKarteId = _karten[^1].Id;
        else if (_selectedKarteId.HasValue && _karten.All(k => k.Id != _selectedKarteId.Value))
            _selectedKarteId = _karten.Count > 0 ? _karten[^1].Id : null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (EinsatzService.CurrentEinsatz.Szenario != EinsatzSzenarioType.Truemmer)
            return;

        if (firstRender && _karten.Count > 0)
        {
            _selfRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("truemmerMap.init", MapElementId, _selfRef);
            _mapInitialized = true;
            await LoadCurrentKarteAsync();
        }
    }

    private async Task LoadCurrentKarteAsync()
    {
        if (!_mapInitialized || !_selectedKarteId.HasValue) return;
        var karte = _karten.FirstOrDefault(k => k.Id == _selectedKarteId.Value);
        if (karte is null) return;

        var imageUrl = $"/api/truemmer/karten/{karte.Id}/image";
        await JS.InvokeVoidAsync("truemmerMap.loadKarte", MapElementId, new
        {
            id = karte.Id,
            imageUrl = imageUrl,
            width = karte.ImageWidthPx,
            height = karte.ImageHeightPx
        });
        await PushAreasToMapAsync();
    }

    private async Task PushAreasToMapAsync()
    {
        if (!_mapInitialized) return;
        var payload = SelectedAreas.Select(a => new
        {
            id = a.Id,
            name = a.Name,
            color = a.Color,
            assignedTeamName = a.AssignedTeamName,
            points = a.Points.Select(p => new { x = p.X, y = p.Y })
        });
        await JS.InvokeVoidAsync("truemmerMap.renderAreas", MapElementId, payload);
    }

    private async Task SelectKarteAsync(Guid id)
    {
        _selectedKarteId = id;
        await LoadCurrentKarteAsync();
    }

    private async Task HandleUploadAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null) return;

        _isUploading = true;
        _status = "Lade hoch...";
        _isError = false;
        StateHasChanged();

        try
        {
            await using var stream = file.OpenReadStream(TruemmerKartenSpeicher.MaxImageBytes);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            // Direkt speichern statt die eigene API ueber die Browser-Adresse aufzurufen (scheiterte am Zertifikat)
            var (_, fehler) = await KartenSpeicher.SpeichernAsync(ms.ToArray(), file.Name, Path.GetFileNameWithoutExtension(file.Name));

            if (fehler is not null)
            {
                _status = $"Upload fehlgeschlagen: {fehler}";
                _isError = true;
            }
            else
            {
                _status = "Bild erfolgreich hochgeladen.";
                ReloadKarten();
                if (!_mapInitialized)
                {
                    StateHasChanged();
                    await Task.Yield();
                    _selfRef ??= DotNetObjectReference.Create(this);
                    await JS.InvokeVoidAsync("truemmerMap.init", MapElementId, _selfRef);
                    _mapInitialized = true;
                }
                await LoadCurrentKarteAsync();
            }
        }
        catch (Exception ex)
        {
            _status = $"Fehler beim Upload: {ex.Message}";
            _isError = true;
        }
        finally
        {
            _isUploading = false;
            StateHasChanged();
        }
    }

    private async Task DeleteKarteAsync(Guid id)
    {
        try
        {
            await KartenSpeicher.LoeschenAsync(id);
        }
        catch { /* swallow */ }
        ReloadKarten();
        await LoadCurrentKarteAsync();
    }

    private async Task StartDrawAsync()
    {
        if (!_mapInitialized) return;
        _isDrawing = true;
        await JS.InvokeVoidAsync("truemmerMap.startDraw", MapElementId);
    }

    private async Task CancelDrawAsync()
    {
        _isDrawing = false;
        await JS.InvokeVoidAsync("truemmerMap.cancelDraw", MapElementId);
    }

    [JSInvokable]
    public async Task OnPolygonCreated(List<TruemmerPoint> points)
    {
        _isDrawing = false;
        if (!_selectedKarteId.HasValue || points.Count < 3)
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        var nextNumber = SelectedAreas.Count + 1;
        var area = new TruemmerArea
        {
            TruemmerKarteId = _selectedKarteId.Value,
            Name = $"Suchgebiet {nextNumber}",
            Points = points,
            Color = "#FF9800"
        };
        await EinsatzService.UpsertTruemmerAreaAsync(area);
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateAreaName(TruemmerArea area, string? name)
    {
        area.Name = name ?? string.Empty;
        await EinsatzService.UpsertTruemmerAreaAsync(area);
    }

    private async Task UpdateAreaColor(TruemmerArea area, string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;
        area.Color = color;
        await EinsatzService.UpsertTruemmerAreaAsync(area);
    }

    private async Task UpdateAreaTeam(TruemmerArea area, string? teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
        {
            area.AssignedTeamId = null;
            area.AssignedTeamName = null;
        }
        else
        {
            var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
            area.AssignedTeamId = teamId;
            area.AssignedTeamName = team?.TeamName;
        }
        await EinsatzService.UpsertTruemmerAreaAsync(area);
    }

    private async Task DeleteAreaAsync(Guid id)
    {
        await EinsatzService.RemoveTruemmerAreaAsync(id);
    }

    private void OnReload() => InvokeAsync(() =>
    {
        ReloadKarten();
        StateHasChanged();
    });

    private void OnKarteAdded(TruemmerKarte _) => OnReload();
    private void OnKarteRemoved(Guid _) => OnReload();
    private void OnAreaChanged(TruemmerArea _) => InvokeAsync(async () =>
    {
        StateHasChanged();
        await PushAreasToMapAsync();
    });
    private void OnAreaRemoved(Guid _) => OnAreaChanged(null!);

    public void Dispose()
    {
        EinsatzService.SzenarioChanged -= OnReload;
        EinsatzService.TruemmerKarteAdded -= OnKarteAdded;
        EinsatzService.TruemmerKarteRemoved -= OnKarteRemoved;
        EinsatzService.TruemmerAreaUpserted -= OnAreaChanged;
        EinsatzService.TruemmerAreaRemoved -= OnAreaRemoved;

        if (_mapInitialized)
        {
            _ = JS.InvokeVoidAsync("truemmerMap.dispose", MapElementId).AsTask();
        }
        _selfRef?.Dispose();
    }
}
