using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzArchiv
{
    [Inject] private IArchivService ArchivService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IEinsatzMergeService MergeService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _loading = true;
    private List<ArchivedEinsatz> _entries = new();
    private int _totalCount;
    private ArchivStatistics? _stats;
    private string _search = string.Empty;
    private string _einsatzleiterFilter = string.Empty;
    private string _typFilter = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string _status = string.Empty;
    private string _trendView = "monat";
    private ArchivedEinsatz? _selected;

    // PDF mit Auswahl (Kartenart, Teams, Tracks)
    private bool _pdfAuswahlOffen;
    private Einsatzueberwachung.Domain.Models.Enums.MapTileType _pdfKartenart = Einsatzueberwachung.Domain.Models.Enums.MapTileType.Streets;
    private bool _pdfMitTracks = true;
    private readonly HashSet<string> _pdfTeamsAus = new(StringComparer.Ordinal);

    private List<(string TeamId, string TeamName)> TeamsMitTracks => _selected?.TrackSnapshots
        .Where(t => t.Points.Count >= 2 && !string.IsNullOrEmpty(t.TeamId))
        .GroupBy(t => t.TeamId)
        .Select(g => (g.Key, g.First().TeamName))
        .OrderBy(t => t.Item2)
        .ToList() ?? new();

    private void SetPdfTeam(string teamId, bool drin)
    {
        if (drin) _pdfTeamsAus.Remove(teamId);
        else _pdfTeamsAus.Add(teamId);
    }

    private string PdfAuswahlUrl
    {
        get
        {
            if (_selected is null) return "#";
            var url = $"{GetArchivedPdfDownloadUrl(_selected.Id)}?karte={_pdfKartenart}";
            if (!_pdfMitTracks) return url + "&tracks=0";
            if (_pdfTeamsAus.Count > 0)
                url += "&teams=" + Uri.EscapeDataString(string.Join(',', TeamsMitTracks.Select(t => t.TeamId).Where(id => !_pdfTeamsAus.Contains(id))));
            return url;
        }
    }
    private bool _scrollToDetails;
    private ArchivedEinsatz? _deleteCandidate;
    private TeamTrackSnapshot? _archiveTrackPopup;
    private readonly HashSet<string> _revertingMergeId = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _entries = await ArchivService.GetAllArchivedAsync();
        _totalCount = _entries.Count;
        _stats = await ArchivService.GetStatisticsAsync();
        _loading = false;
    }

    private async Task SearchAsync()
    {
        _loading = true;
        var criteria = new ArchivSearchCriteria
        {
            Suchtext = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim(),
            VonDatum = _fromDate,
            BisDatum = _toDate,
            NurEinsaetze = _typFilter == "einsatz" ? true : _typFilter == "uebung" ? false : null,
            Einsatzleiter = string.IsNullOrWhiteSpace(_einsatzleiterFilter) ? null : _einsatzleiterFilter.Trim()
        };

        _entries = await ArchivService.SearchAsync(criteria);
        _loading = false;
    }

    private async Task ToggleDetailsAsync(string id)
    {
        if (_selected?.Id == id)
        {
            _selected = null;
            return;
        }

        _selected = await ArchivService.GetByIdAsync(id);
        _pdfAuswahlOffen = false;
        _pdfTeamsAus.Clear();
        _scrollToDetails = _selected is not null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToDetails)
        {
            _scrollToDetails = false;
            // Details sichtbar machen, ohne den gewaehlten Einsatz aus dem Blick zu schieben
            await JSRuntime.InvokeVoidAsync("layoutTools.scrollIntoViewNearest", "archiv-details");
        }
    }

    private void CloseDetails()
    {
        _selected = null;
    }

    private void PromptDelete(ArchivedEinsatz item)
    {
        _deleteCandidate = item;
        _status = string.Empty;
    }

    private void CancelDelete()
    {
        _deleteCandidate = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteCandidate is null)
        {
            return;
        }

        var success = await ArchivService.DeleteAsync(_deleteCandidate.Id);
        if (!success)
        {
            _status = "Der Archiv-Eintrag konnte nicht geloescht werden.";
            _deleteCandidate = null;
            return;
        }

        if (_selected?.Id == _deleteCandidate.Id)
        {
            _selected = null;
        }

        _status = $"Archiv-Eintrag {_deleteCandidate.EinsatzNummer} wurde geloescht.";
        _deleteCandidate = null;
        await LoadAsync();
    }

    private static string GetArchivedPdfDownloadUrl(string id)
    {
        return $"/downloads/einsatz-archiv/{id}.pdf";
    }

    private void NavigateToMerge(string archivedEinsatzId)
    {
        Navigation.NavigateTo($"/einsatz-import-export/{archivedEinsatzId}");
    }

    private void NavigateToExport(string archivedEinsatzId)
    {
        Navigation.NavigateTo($"/einsatz-import-export?tab=export&exportArchivId={archivedEinsatzId}");
    }

    private async Task RevertArchivedMergeAsync(string archivedEinsatzId, string mergeId)
    {
        _revertingMergeId.Add(mergeId);
        StateHasChanged();
        try
        {
            await MergeService.RevertMergeAsync(mergeId, archivedEinsatzId);
            _selected = await ArchivService.GetByIdAsync(archivedEinsatzId);
            _status = "Zusammenführung erfolgreich rückgängig gemacht.";
        }
        catch (Exception ex)
        {
            _status = $"Fehler beim Rückgängigmachen: {ex.Message}";
        }
        finally
        {
            _revertingMergeId.Remove(mergeId);
            StateHasChanged();
        }
    }

    private async Task DownloadArchiveTrackGpxAsync()
    {
        if (_archiveTrackPopup == null || _archiveTrackPopup.Points.Count == 0) return;
        await JSRuntime.InvokeVoidAsync("downloadFile",
            GpxBuilder.TrackSnapshotFileName(_archiveTrackPopup),
            GpxBuilder.BuildTrackSnapshotGpx(_archiveTrackPopup),
            "application/gpx+xml");
    }

    private async Task DownloadAreaGpxAsync(SearchArea area)
    {
        if (area.Coordinates == null || area.Coordinates.Count < 2) return;
        await JSRuntime.InvokeVoidAsync("downloadFile",
            GpxBuilder.SearchAreaFileName(area),
            GpxBuilder.BuildSearchAreaGpx(area),
            "application/gpx+xml");
    }
}
