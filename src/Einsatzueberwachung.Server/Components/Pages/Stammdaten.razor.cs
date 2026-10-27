using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Stammdaten
{
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private IExcelExportService ExcelExportService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? InitialTab { get; set; }

    [SupplyParameterFromQuery(Name = "highlight")]
    public string? HighlightId { get; set; }

    private static readonly PersonalSkills[] _personalSkillOptions = Enum.GetValues<PersonalSkills>().Where(value => value != PersonalSkills.None).ToArray();
    private static readonly DogSpecialization[] _dogSpecializationOptions = Enum.GetValues<DogSpecialization>().Where(value => value != DogSpecialization.None).ToArray();

    private int _personalCount;
    private int _dogCount;
    private int _droneCount;
    private bool _isImporting;
    private string _status = string.Empty;
    private string _activeTab = "personal";
    private List<PersonalEntry> _personal = new();
    private List<DogEntry> _dogs = new();
    private List<DroneEntry> _drones = new();
    private PersonalEntry _personalDraft = new();
    private DogEntry _dogDraft = new();
    private DroneEntry _droneDraft = new();
    private string? _editingPersonalId;
    private string? _editingDogId;
    private string? _editingDroneId;
    private bool _showCreatePersonalModal;
    private bool _showCreateDogModal;
    private bool _showCreateDroneModal;
    private string? _pendingHighlightScrollId;
    private string? _lastScrolledHighlightId;

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(InitialTab))
            _activeTab = InitialTab;

        if (!string.IsNullOrWhiteSpace(HighlightId)
            && IsValidEntityId(HighlightId)
            && !string.Equals(HighlightId, _lastScrolledHighlightId, StringComparison.Ordinal))
        {
            _pendingHighlightScrollId = HighlightId;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!string.IsNullOrWhiteSpace(_pendingHighlightScrollId))
        {
            await JS.InvokeVoidAsync("layoutTools.scrollToElement", $"entry-{_pendingHighlightScrollId}");
            _lastScrolledHighlightId = _pendingHighlightScrollId;
            _pendingHighlightScrollId = null;
        }
    }

    /// <summary>
    /// Sanity-checks that an ID coming from a URL query parameter only contains characters
    /// that appear in GUIDs (hex digits and hyphens) before it is forwarded to JavaScript.
    /// </summary>
    private static bool IsValidEntityId(string id) =>
        id.Length <= 64 && id.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF-".AsSpan()) == -1;

    private async Task RefreshAsync()
    {
        _personal = await MasterDataService.GetPersonalListAsync();
        _dogs = await MasterDataService.GetDogListAsync();
        _drones = await MasterDataService.GetDroneListAsync();
        await RefreshChecklistTemplatesAsync();

        _personalCount = _personal.Count;
        _dogCount = _dogs.Count;
        _droneCount = _drones.Count;
    }

    private async Task HandleImportAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            _status = "Keine Datei ausgewählt.";
            return;
        }

        if (!file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _status = "Bitte eine .xlsx Datei auswählen.";
            return;
        }

        _isImporting = true;
        _status = "Import läuft …";

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            var importResult = await ExcelExportService.ImportStammdatenAsync(memory.ToArray());
            if (!importResult.Success)
            {
                _status = importResult.Message;
                return;
            }

            var warningInfo = importResult.Warnings.Count > 0
                ? $" Warnungen: {importResult.Warnings.Count}."
                : string.Empty;

            _status = $"Import abgeschlossen: {importResult.TotalImported} importiert, {importResult.TotalSkipped} übersprungen.{warningInfo}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status = $"Fehler beim Import: {ex.Message}";
        }
        finally
        {
            _isImporting = false;
        }
    }

    private string GetPersonalName(string personalId)
    {
        if (string.IsNullOrWhiteSpace(personalId))
        {
            return "-";
        }

        return _personal.FirstOrDefault(entry => entry.Id == personalId)?.FullName ?? "-";
    }

    private string GetHundefuehrerNames(List<string> ids)
    {
        if (ids == null || ids.Count == 0)
            return "-";

        var names = ids
            .Select(id => _personal.FirstOrDefault(p => p.Id == id)?.FullName)
            .Where(n => n != null);
        var result = string.Join(", ", names);
        return string.IsNullOrWhiteSpace(result) ? "-" : result;
    }

    private string GetDownloadUrl(string relativePath)
    {
        return new Uri(new Uri(Navigation.BaseUri), relativePath.TrimStart('/')).ToString();
    }

    private void SetActiveTab(string tab)
    {
        _activeTab = tab;
    }

    private string GetTabButtonClass(string tab)
    {
        return _activeTab == tab ? "btn-primary" : "btn-outline-secondary";
    }

    private string GetTabPaneClass(string tab)
    {
        return _activeTab == tab ? string.Empty : "d-none";
    }
}
