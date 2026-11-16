using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Merge;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzImportExport
{
    [Inject] IEinsatzMergeService MergeService { get; set; } = default!;
    [Inject] IEinsatzExportService ExportService { get; set; } = default!;
    [Inject] IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] IArchivService ArchivService { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;

    [Parameter] public string? ArchivedEinsatzId { get; set; }

    [SupplyParameterFromQuery(Name = "tab")]
    public string? InitialTab { get; set; }

    [SupplyParameterFromQuery(Name = "exportArchivId")]
    public string? ExportArchivId { get; set; }

    // ── Tab-Zustand ──
    private string _activeTab = "import";

    private void SetActiveTab(string tab) => _activeTab = tab;
    private string GetTabButtonClass(string tab) => _activeTab == tab ? "btn-primary" : "btn-outline-secondary";
    private string GetTabPaneClass(string tab) => _activeTab == tab ? string.Empty : "d-none";

    // ── Navigation ──
    private void NavigateBack()
    {
        if (!string.IsNullOrEmpty(ArchivedEinsatzId) || !string.IsNullOrEmpty(ExportArchivId))
            Navigation.NavigateTo("/einsatz-archiv");
        else
            Navigation.NavigateTo("/einsatz-monitor");
    }

    protected override async Task OnParametersSetAsync()
    {
        // Tab per Query-Parameter vorwählen
        if (!string.IsNullOrEmpty(InitialTab))
            _activeTab = InitialTab;

        // Archiv-Export per Query-Parameter vorwählen
        if (!string.IsNullOrEmpty(ExportArchivId) && _exportArchiveList.Count == 0 && !_exportArchiveLoading)
        {
            _activeTab = "export";
            _exportSource = "archive";
            await LoadArchiveListAsync();
            await SelectArchiveAsync(ExportArchivId);
        }
    }

    // ════════════════════════════════════════════════════════════
    // IMPORT — Zustand & Logik
    // ════════════════════════════════════════════════════════════

    // ── Modus-Auswahl ──
    private string? _importMode = null;  // null = noch nicht gewählt | "merge" | "new-einsatz"

    // Merge-Ziel: null = laufender Einsatz, non-null = archivierter Einsatz ID
    private string? _mergeTargetId;
    private List<ArchivedEinsatz> _mergeTargetArchiveList = new();
    private bool _mergeTargetArchiveLoading;

    private void SelectImportMode(string mode)
    {
        _importMode = mode;
        _mergeTargetId = ArchivedEinsatzId;  // pre-fill from route param if present
        _mergeTargetArchiveList.Clear();
        _session = null;
        _currentStep = MergeWizardStep.Upload;
        _errorMessage = string.Empty;
        _einsatzNrMismatch = false;
        _revertSuccess = false;
        _mergeLabel = string.Empty;
        _newEinsatzPacket = null;
        _newEinsatzMergeSession = null;
        _newEinsatzStep = 1;
        _newEinsatzErrorMessage = string.Empty;
        _newEinsatzResult = null;
        _newEinsatzEinsatzort = string.Empty;
        _newEinsatzErgebnis = string.Empty;
        _newEinsatzBemerkungen = string.Empty;
    }

    private async Task OnMergeTargetSourceChangedAsync(ChangeEventArgs e)
    {
        var source = e.Value?.ToString();
        _mergeTargetId = source == "archive" ? string.Empty : null;
        _session = null;
        _errorMessage = string.Empty;
        _einsatzNrMismatch = false;

        if (source == "archive" && _mergeTargetArchiveList.Count == 0 && !_mergeTargetArchiveLoading)
        {
            _mergeTargetArchiveLoading = true;
            StateHasChanged();
            _mergeTargetArchiveList = await ArchivService.GetAllArchivedAsync();
            _mergeTargetArchiveLoading = false;
        }
    }

    private void OnMergeTargetArchiveIdChanged(ChangeEventArgs e)
    {
        _mergeTargetId = e.Value?.ToString();
        _session = null;
        _errorMessage = string.Empty;
        _einsatzNrMismatch = false;
    }

    /// <summary>Effektive Merge-Ziel-ID: entweder explizit gewählt, oder aus Route-Param.</summary>
    private string? EffectiveMergeTargetId => _mergeTargetId;

    private void ResetImportMode() => SelectImportMode(null!);

    /// <summary>Maximale Upload-Dateigröße (10 MB).</summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private EinsatzMergeSession? _session;
    private MergeWizardStep _currentStep = MergeWizardStep.Upload;
    private bool _isLoading = false;
    private bool _einsatzNrMismatch = false;
    private string _errorMessage = string.Empty;
    private bool _revertSuccess = false;
    private string _mergeLabel = string.Empty;

    // ── Import-Historie ──
    private List<MergeHistoryEntry> _mergeHistory = new();
    private bool _mergeHistoryLoading = false;
    private readonly HashSet<string> _revertingHistoryIds = new();
    private readonly Dictionary<string, string> _historyRevertErrors = new();

    private readonly List<(int Number, string Label)> _steps = new()
    {
        (1, "Upload"),
        (2, "Stammdaten"),
        (3, "Daten prüfen"),
        (4, "Bestätigen"),
        (5, "Ergebnis")
    };

    private async Task HandleFileUploadAsync(InputFileChangeEventArgs e)
    {
        _errorMessage = string.Empty;
        _isLoading = true;
        StateHasChanged();

        try
        {
            using var ms = new System.IO.MemoryStream();
            await e.File.OpenReadStream(maxAllowedSize: MaxUploadBytes).CopyToAsync(ms);
            var bytes = ms.ToArray();

            var packet = MergeService.ParseExportPacket(bytes);
            if (packet == null)
            {
                _errorMessage = "Die Datei konnte nicht gelesen werden. Bitte eine gültige .einsatz-export.json Datei hochladen.";
                return;
            }

            _session = await MergeService.CreateSessionAsync(packet, EffectiveMergeTargetId);

            // Pre-fill label from packet if available
            if (!string.IsNullOrWhiteSpace(packet.Label))
                _mergeLabel = packet.Label;

            var currentNr = string.IsNullOrEmpty(EffectiveMergeTargetId)
                ? EinsatzService.CurrentEinsatz.EinsatzNummer
                : (await ArchivService.GetByIdAsync(EffectiveMergeTargetId))?.EinsatzNummer ?? string.Empty;

            _einsatzNrMismatch = !string.IsNullOrEmpty(packet.EinsatzNummer) &&
                                  !string.IsNullOrEmpty(currentNr) &&
                                  !string.Equals(packet.EinsatzNummer, currentNr, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _errorMessage = $"Fehler beim Einlesen der Datei: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void AcknowledgeMismatch()
    {
        if (_session != null)
            _session.EinsatzNrMismatchAcknowledged = true;
    }

    private bool CanProceedFromUpload =>
        _session?.Packet != null &&
        (!_einsatzNrMismatch || _session.EinsatzNrMismatchAcknowledged);

    private void ProceedToMasterData()
    {
        if (!CanProceedFromUpload) return;
        GoToStep(MergeWizardStep.MasterDataResolution);
    }

    private void OnMasterDataDecisionChanged()
    {
        if (_session != null)
            MergeService.RebuildIdRemapping(_session);
        StateHasChanged();
    }

    private void ProceedToOperationalData()
    {
        if (_session?.AllMasterDataResolved != true) return;
        GoToStep(MergeWizardStep.OperationalDataReview);
    }

    private void BulkSelectNotes(bool select)
    {
        if (_session == null) return;
        foreach (var n in _session.NoteItems.Where(x => !x.IsAlreadyPresent))
            n.ShouldImport = select;
    }

    private async Task ExecuteMergeAsync()
    {
        if (_session == null) return;
        _isLoading = true;
        _errorMessage = string.Empty;

        // Apply the user-entered label to the packet for the merge history
        _session.Packet.Label = _mergeLabel.Trim();

        try
        {
            await MergeService.ApplyMergeAsync(_session);
            _currentStep = MergeWizardStep.Result;
            await LoadMergeHistoryAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Zusammenführung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task RevertCurrentMergeAsync()
    {
        if (_session?.AppliedMerge == null) return;
        _isLoading = true;

        try
        {
            await MergeService.RevertMergeAsync(
                _session.AppliedMerge.MergeId,
                EffectiveMergeTargetId);
            _revertSuccess = true;
            await LoadMergeHistoryAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Rückgängig fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadMergeHistoryAsync()
    {
        if (!string.IsNullOrEmpty(ArchivedEinsatzId)) return;
        _mergeHistoryLoading = true;
        _mergeHistory = await MergeService.GetMergeHistoryAsync(null);
        _mergeHistoryLoading = false;
    }

    private async Task RevertHistoryMergeAsync(string mergeId)
    {
        _revertingHistoryIds.Add(mergeId);
        _historyRevertErrors.Remove(mergeId);
        try
        {
            await MergeService.RevertMergeAsync(mergeId, null);
            await LoadMergeHistoryAsync();
        }
        catch (Exception ex)
        {
            _historyRevertErrors[mergeId] = $"Rückgängig fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _revertingHistoryIds.Remove(mergeId);
        }
    }

    private void GoToStep(MergeWizardStep step)
    {
        _currentStep = step;
        if (_session != null)
            _session.CurrentStep = step;
    }

    private string GetStepClass(int stepNumber)
    {
        var current = (int)_currentStep;
        if (stepNumber == current) return "active";
        if (stepNumber < current) return "completed";
        return "";
    }

    private string GetConnectorClass(int stepNumber)
    {
        var current = (int)_currentStep;
        return stepNumber < current ? "completed" : "";
    }

    // ── Stepper-Helpers für Neuer-Einsatz-Modus ──

    /// <summary>
    /// Zeigt 4 Schritte wenn Stammdaten vorhanden, sonst 3 (Schritt 2 entfällt).
    /// Verhindert, dass ein übersprungener Schritt im Stepper als „abgeschlossen" erscheint.
    /// </summary>
    private List<(int Number, string Label)> NewEinsatzEffectiveSteps =>
        _newEinsatzMergeSession?.TotalMasterDataCount > 0
            ? new() { (1, "Upload"), (2, "Stammdaten"), (3, "Details"), (4, "Ergebnis") }
            : new() { (1, "Upload"), (2, "Details"), (3, "Ergebnis") };

    /// <summary>
    /// Konvertiert die interne Schritt-Nummer in die Stepper-Anzeigeposition.
    /// Wenn kein Stammdaten-Schritt: intern 3→Anzeige 2, intern 4→Anzeige 3; sonst 1:1.
    /// </summary>
    private int NewEinsatzDisplayStep =>
        _newEinsatzMergeSession?.TotalMasterDataCount > 0
            ? _newEinsatzStep
            : _newEinsatzStep switch
            {
                3 => 2,
                4 => 3,
                _ => _newEinsatzStep
            };

    private string GetNewEinsatzStepClass(int stepNumber)
    {
        var display = NewEinsatzDisplayStep;
        if (stepNumber == display) return "active";
        if (stepNumber < display) return "completed";
        return "";
    }

    private string GetNewEinsatzConnectorClass(int stepNumber) =>
        stepNumber < NewEinsatzDisplayStep ? "completed" : "";

    private void AdvanceFromNewEinsatzUpload()
    {
        // Schritt 2 (Stammdaten) überspringen, wenn das Paket keine Stammdaten enthält
        if (_newEinsatzMergeSession?.TotalMasterDataCount > 0)
            _newEinsatzStep = 2;
        else
            _newEinsatzStep = 3;
    }

    private void BackFromNewEinsatzDetails()
    {
        // Wenn Stammdaten vorhanden waren, zurück zu Schritt 2, sonst zu Schritt 1
        if (_newEinsatzMergeSession?.TotalMasterDataCount > 0)
            _newEinsatzStep = 2;
        else
            _newEinsatzStep = 1;
    }

    private void OnNewEinsatzMasterDataDecisionChanged()
    {
        if (_newEinsatzMergeSession != null)
            MergeService.RebuildIdRemapping(_newEinsatzMergeSession);
        StateHasChanged();
    }

    private static string SafeId(string? id) =>
        id is { Length: >= 8 } ? $"{id[..8]}…" : (id ?? "?");

    private static string GetDecisionIcon(MergeDecision d) => d switch
    {
        MergeDecision.LinkToExisting => "bi-link-45deg",
        MergeDecision.CreateNew => "bi-plus-circle",
        MergeDecision.Skip => "bi-x-circle",
        _ => "bi-question-circle"
    };

    private static string GetDecisionColor(MergeDecision d) => d switch
    {
        MergeDecision.LinkToExisting => "text-success",
        MergeDecision.CreateNew => "text-primary",
        MergeDecision.Skip => "text-secondary",
        _ => ""
    };

    private static string GetDecisionLabel(MasterDataMergeItem item) => item.Decision switch
    {
        MergeDecision.LinkToExisting => $"Verknüpft mit {SafeId(item.SelectedLocalId)}",
        MergeDecision.CreateNew => "Neu anlegen",
        MergeDecision.Skip => "Überspringen",
        _ => "Offen"
    };

    // ════════════════════════════════════════════════════════════
    // IMPORT NEUER EINSATZ — Zustand & Logik
    // ════════════════════════════════════════════════════════════

    private EinsatzExportPacket? _newEinsatzPacket;
    private EinsatzMergeSession? _newEinsatzMergeSession;
    private int _newEinsatzStep = 1;  // 1=Upload, 2=Stammdaten, 3=Details, 4=Ergebnis
    private bool _newEinsatzIsLoading;
    private string _newEinsatzErrorMessage = string.Empty;
    private string _newEinsatzEinsatzort = string.Empty;
    private string _newEinsatzErgebnis = string.Empty;
    private string _newEinsatzBemerkungen = string.Empty;
    private ArchivedEinsatz? _newEinsatzResult;

    // ── Stepper-Helpers für Neuer-Einsatz-Modus ──

    private async Task HandleNewEinsatzFileUploadAsync(InputFileChangeEventArgs e)
    {
        _newEinsatzErrorMessage = string.Empty;
        _newEinsatzIsLoading = true;
        StateHasChanged();

        try
        {
            using var ms = new System.IO.MemoryStream();
            await e.File.OpenReadStream(maxAllowedSize: MaxUploadBytes).CopyToAsync(ms);
            var bytes = ms.ToArray();

            var packet = MergeService.ParseExportPacket(bytes);
            if (packet == null)
            {
                _newEinsatzErrorMessage = "Die Datei konnte nicht gelesen werden. Bitte eine gültige .einsatz-export.json Datei hochladen.";
                return;
            }

            _newEinsatzPacket = packet;
            _newEinsatzEinsatzort = string.Empty; // cleared so placeholder shows EinsatzNummer

            // Stammdaten-Merge-Items mit Vorschlägen befüllen (nur für Schritt 2 genutzt)
            _newEinsatzMergeSession = await MergeService.CreateSessionAsync(packet, null);
        }
        catch (Exception ex)
        {
            _newEinsatzErrorMessage = $"Fehler beim Einlesen der Datei: {ex.Message}";
        }
        finally
        {
            _newEinsatzIsLoading = false;
        }
    }

    private async Task ExecuteNewEinsatzImportAsync()
    {
        if (_newEinsatzPacket == null) return;
        _newEinsatzIsLoading = true;
        _newEinsatzErrorMessage = string.Empty;

        try
        {
            // Stammdaten-Entscheidungen aus Schritt 2 anwenden (falls vorhanden)
            if (_newEinsatzMergeSession != null && _newEinsatzMergeSession.TotalMasterDataCount > 0)
                await ApplyNewEinsatzMasterDataAsync(_newEinsatzMergeSession);

            _newEinsatzResult = await ArchivService.ImportPacketAsNewEinsatzAsync(
                _newEinsatzPacket,
                _newEinsatzEinsatzort,
                _newEinsatzErgebnis,
                _newEinsatzBemerkungen);
            _newEinsatzStep = 4;
        }
        catch (Exception ex)
        {
            _newEinsatzErrorMessage = $"Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _newEinsatzIsLoading = false;
        }
    }

    /// <summary>
    /// Wendet die Stammdaten-Entscheidungen aus Schritt 2 des „Neuer Einsatz"-Wizards an.
    /// Legt neue Einträge an (CreateNew) und überschreibt ggf. lokale Felder (LinkToExisting + OverwriteLocalFields).
    /// </summary>
    private async Task ApplyNewEinsatzMasterDataAsync(EinsatzMergeSession session)
    {
        // ── Personal ──
        foreach (var item in session.PersonalItems.Where(i => i.Decision == MergeDecision.CreateNew))
        {
            var src = (PersonalEntry)item.ImportedEntry;
            await MasterDataService.AddPersonalAsync(new PersonalEntry
            {
                Id = Guid.NewGuid().ToString(),
                Vorname = src.Vorname,
                Nachname = src.Nachname,
                Organisation = src.Organisation,
                Skills = src.Skills,
                Notizen = src.Notizen,
                IsActive = src.IsActive,
                DiveraUserId = src.DiveraUserId
            });
        }

        foreach (var item in session.PersonalItems.Where(i =>
            i.Decision == MergeDecision.LinkToExisting &&
            i.OverwriteLocalFields &&
            !string.IsNullOrEmpty(i.SelectedLocalId)))
        {
            var local = await MasterDataService.GetPersonalByIdAsync(item.SelectedLocalId!);
            if (local == null) continue;
            var imported = (PersonalEntry)item.ImportedEntry;
            local.Vorname = imported.Vorname;
            local.Nachname = imported.Nachname;
            local.Skills = imported.Skills;
            if (!string.IsNullOrWhiteSpace(imported.Notizen))
                local.Notizen = imported.Notizen;
            await MasterDataService.UpdatePersonalAsync(local);
        }

        // ── Hunde ──
        foreach (var item in session.DogItems.Where(i => i.Decision == MergeDecision.CreateNew))
        {
            var src = (DogEntry)item.ImportedEntry;
            await MasterDataService.AddDogAsync(new DogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Name = src.Name,
                Rasse = src.Rasse,
                Alter = src.Alter,
                Specializations = src.Specializations,
                HundefuehrerIds = new System.Collections.Generic.List<string>(src.HundefuehrerIds),
                Notizen = src.Notizen,
                IsActive = src.IsActive
            });
        }

        foreach (var item in session.DogItems.Where(i =>
            i.Decision == MergeDecision.LinkToExisting &&
            i.OverwriteLocalFields &&
            !string.IsNullOrEmpty(i.SelectedLocalId)))
        {
            var local = await MasterDataService.GetDogByIdAsync(item.SelectedLocalId!);
            if (local == null) continue;
            var imported = (DogEntry)item.ImportedEntry;
            local.Name = imported.Name;
            local.Rasse = imported.Rasse;
            local.Specializations = imported.Specializations;
            if (!string.IsNullOrWhiteSpace(imported.Notizen))
                local.Notizen = imported.Notizen;
            await MasterDataService.UpdateDogAsync(local);
        }

        // ── Drohnen ──
        foreach (var item in session.DroneItems.Where(i => i.Decision == MergeDecision.CreateNew))
        {
            var src = (DroneEntry)item.ImportedEntry;
            await MasterDataService.AddDroneAsync(new DroneEntry
            {
                Id = Guid.NewGuid().ToString(),
                Name = src.Name,
                Modell = src.Modell,
                Hersteller = src.Hersteller,
                Seriennummer = src.Seriennummer,
                DrohnenpilotId = src.DrohnenpilotId,
                Notizen = src.Notizen,
                IsActive = src.IsActive
            });
        }

        foreach (var item in session.DroneItems.Where(i =>
            i.Decision == MergeDecision.LinkToExisting &&
            i.OverwriteLocalFields &&
            !string.IsNullOrEmpty(i.SelectedLocalId)))
        {
            var local = await MasterDataService.GetDroneByIdAsync(item.SelectedLocalId!);
            if (local == null) continue;
            var imported = (DroneEntry)item.ImportedEntry;
            local.Name = imported.Name;
            local.Modell = imported.Modell;
            local.Hersteller = imported.Hersteller;
            if (!string.IsNullOrWhiteSpace(imported.Notizen))
                local.Notizen = imported.Notizen;
            await MasterDataService.UpdateDroneAsync(local);
        }
    }

    // ════════════════════════════════════════════════════════════
    // EXPORT — Zustand & Logik
    // ════════════════════════════════════════════════════════════

    private string _exportSource = "current"; // "current" | "archive"
    private List<ArchivedEinsatz> _exportArchiveList = new();
    private ArchivedEinsatz? _exportSelectedArchive;
    private bool _exportArchiveLoading;

    private readonly HashSet<string> _selectedTeamIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedArchiveTeamIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly EinsatzExportOptions _exportOptions = new();

    private EinsatzExportPacket? _exportPreview;
    private long _exportPreviewSizeKb;
    private bool _exportIsWorking;
    private bool _exportIsDownloading;
    private bool _exportValidationAttempted;
    private string _exportStatusMessage = string.Empty;
    private bool _exportStatusIsError;

    protected override async Task OnInitializedAsync()
    {
        // Vorauswahl: alle Teams des laufenden Einsatzes
        foreach (var team in EinsatzService.Teams)
            _selectedTeamIds.Add(team.TeamId);

        await LoadMergeHistoryAsync();
    }

    private async Task OnExportSourceChangedAsync(ChangeEventArgs e)
    {
        _exportSource = e.Value?.ToString() ?? "current";
        _exportPreview = null;
        _exportValidationAttempted = false;

        if (_exportSource == "archive" && _exportArchiveList.Count == 0)
            await LoadArchiveListAsync();
    }

    private async Task LoadArchiveListAsync()
    {
        _exportArchiveLoading = true;
        StateHasChanged();
        _exportArchiveList = await ArchivService.GetAllArchivedAsync();
        _exportArchiveLoading = false;
    }

    private async Task SelectArchiveAsync(string id)
    {
        _exportSelectedArchive = _exportArchiveList.FirstOrDefault(a => a.Id == id)
                                 ?? await ArchivService.GetByIdAsync(id);
        _selectedArchiveTeamIds.Clear();
        if (_exportSelectedArchive != null)
        {
            foreach (var t in _exportSelectedArchive.Teams)
                _selectedArchiveTeamIds.Add(t.TeamId);
        }
        _exportPreview = null;
    }

    private async Task OnArchiveSelectionChangedAsync(ChangeEventArgs e)
    {
        var id = e.Value?.ToString();
        if (!string.IsNullOrEmpty(id))
            await SelectArchiveAsync(id);
        else
        {
            _exportSelectedArchive = null;
            _selectedArchiveTeamIds.Clear();
            _exportPreview = null;
        }
    }

    private void ToggleTeam(string teamId)
    {
        if (_selectedTeamIds.Contains(teamId))
            _selectedTeamIds.Remove(teamId);
        else
            _selectedTeamIds.Add(teamId);

        _exportPreview = null;
    }

    private void ToggleArchiveTeam(string teamId)
    {
        if (_selectedArchiveTeamIds.Contains(teamId))
            _selectedArchiveTeamIds.Remove(teamId);
        else
            _selectedArchiveTeamIds.Add(teamId);

        _exportPreview = null;
    }

    private void SelectAllTeams()
    {
        foreach (var t in EinsatzService.Teams)
            _selectedTeamIds.Add(t.TeamId);
        _exportPreview = null;
    }

    private void DeselectAllTeams()
    {
        _selectedTeamIds.Clear();
        _exportPreview = null;
    }

    private void SelectAllArchiveTeams()
    {
        if (_exportSelectedArchive == null) return;
        foreach (var t in _exportSelectedArchive.Teams)
            _selectedArchiveTeamIds.Add(t.TeamId);
        _exportPreview = null;
    }

    private void DeselectAllArchiveTeams()
    {
        _selectedArchiveTeamIds.Clear();
        _exportPreview = null;
    }

    private bool ValidateExport()
    {
        _exportValidationAttempted = true;
        if (_exportSource == "archive")
            return _exportSelectedArchive != null && _selectedArchiveTeamIds.Any();
        return _selectedTeamIds.Any();
    }

    private async Task RefreshExportPreviewAsync()
    {
        if (!ValidateExport()) return;

        _exportIsWorking = true;
        StateHasChanged();

        try
        {
            EinsatzExportPacket packet;
            if (_exportSource == "archive" && _exportSelectedArchive != null)
            {
                packet = await ExportService.BuildExportPacketFromArchiveAsync(
                    _exportSelectedArchive,
                    _selectedArchiveTeamIds,
                    _exportOptions);
            }
            else
            {
                packet = await ExportService.BuildExportPacketAsync(
                    _selectedTeamIds,
                    string.Empty,
                    _exportOptions);
            }

            var bytes = ExportService.Serialize(packet);
            _exportPreview = packet;
            _exportPreviewSizeKb = bytes.Length / 1024 + 1;
        }
        catch (Exception ex)
        {
            _exportStatusMessage = $"Fehler bei der Vorschau: {ex.Message}";
            _exportStatusIsError = true;
        }
        finally
        {
            _exportIsWorking = false;
        }
    }

    private async Task ExportAndDownloadAsync()
    {
        if (!ValidateExport()) return;

        _exportIsWorking = true;
        _exportIsDownloading = true;
        _exportStatusMessage = string.Empty;
        StateHasChanged();

        try
        {
            EinsatzExportPacket packet;
            if (_exportSource == "archive" && _exportSelectedArchive != null)
            {
                packet = await ExportService.BuildExportPacketFromArchiveAsync(
                    _exportSelectedArchive,
                    _selectedArchiveTeamIds,
                    _exportOptions);
            }
            else
            {
                packet = await ExportService.BuildExportPacketAsync(
                    _selectedTeamIds,
                    string.Empty,
                    _exportOptions);
            }

            var bytes = ExportService.Serialize(packet);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var fileName = ExportService.GetFileName(packet);

            _exportPreview = packet;
            _exportPreviewSizeKb = bytes.Length / 1024 + 1;

            // JS-Download: window.downloadFile ist in wwwroot/js/layout-tools.js definiert
            await JS.InvokeVoidAsync("downloadFile", fileName, json, "application/json");

            _exportStatusMessage = $"Export erfolgreich: {fileName}";
            _exportStatusIsError = false;
        }
        catch (Exception ex)
        {
            _exportStatusMessage = $"Export fehlgeschlagen: {ex.Message}";
            _exportStatusIsError = true;
        }
        finally
        {
            _exportIsWorking = false;
            _exportIsDownloading = false;
        }
    }
}
