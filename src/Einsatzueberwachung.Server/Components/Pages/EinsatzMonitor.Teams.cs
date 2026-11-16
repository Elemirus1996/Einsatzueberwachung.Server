using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{    private void SelectTeamFilter(TeamStatusFilter filter)
    {
        _teamStatusFilter = filter;
        _showTeamFilter = false;
    }

    private void ClearTeamStatusMessage()
    {
        _teamStatusMessage = string.Empty;
    }

    private static string TeamFilterLabel(TeamStatusFilter filter) => filter switch
    {
        TeamStatusFilter.Running => "Aktiv",
        TeamStatusFilter.Pausing => "Pause",
        TeamStatusFilter.Ready => "Bereit",
        TeamStatusFilter.Critical => "Kritisch",
        _ => "Alle"
    };

    private static string TeamFilterDotClass(TeamStatusFilter filter) => filter switch
    {
        TeamStatusFilter.Running => "teams-filter-dot--running",
        TeamStatusFilter.Pausing => "teams-filter-dot--pausing",
        TeamStatusFilter.Ready => "teams-filter-dot--ready",
        TeamStatusFilter.Critical => "teams-filter-dot--critical",
        _ => string.Empty
    };

    internal static IOrderedEnumerable<Team> OrderTeamsForMonitor(IEnumerable<Team> teams) =>
        teams.OrderBy(team => team.ManualOrderIndex ?? int.MaxValue)
            .ThenBy(team => team.IsPausing ? 3 : team.IsRunning || team.IsManuallyPaused ? 0 : 1)
            .ThenBy(team => team.TeamName);

    private IEnumerable<Team> GetFilteredTeams()
    {
        var teams = EinsatzService.Teams.AsEnumerable();
        return _teamStatusFilter switch
        {
            TeamStatusFilter.Running => teams.Where(team => team.IsRunning),
            TeamStatusFilter.Pausing => teams.Where(team => team.IsPausing),
            TeamStatusFilter.Ready => teams.Where(team => !team.IsRunning && !team.IsPausing),
            TeamStatusFilter.Critical => teams.Where(team => team.IsSecondWarning),
            _ => teams
        };
    }

    private IReadOnlyList<MonitorTeamCardItem> GetMonitorTeamCardItems() =>
        OrderTeamsForMonitor(GetFilteredTeams())
            .Select(team =>
            {
                var isDogConflict = !team.IsRunning && !team.IsPausing && !team.IsManuallyPaused
                    && !string.IsNullOrWhiteSpace(team.DogId)
                    && EinsatzService.IsDogRunning(team.DogId);
                var conflictingTeamName = isDogConflict
                    ? EinsatzService.Teams.FirstOrDefault(other =>
                        other.TeamId != team.TeamId && other.IsRunning && other.DogId == team.DogId)?.TeamName
                    : null;

                return new MonitorTeamCardItem(
                    team,
                    isDogConflict,
                    conflictingTeamName,
                    _expandedTeamIds.Contains(team.TeamId),
                    GetLastCollarLocation(team),
                    EinsatzService.PhoneLocations.GetValueOrDefault(team.TeamId),
                    GetLatestTeamNoteEntries(team, 3),
                    GetCompletedSearches(team, 5));
            })
            .ToArray();

    private IReadOnlyList<MonitorSearchAreaItem> GetMonitorSearchAreaItems() =>
        EinsatzService.CurrentEinsatz.SearchAreas
            .OrderBy(area => area.Name)
            .Select(area =>
            {
                var assignedTeam = EinsatzService.Teams.FirstOrDefault(team => team.SearchAreaId == area.Id);
                return new MonitorSearchAreaItem(
                    area,
                    assignedTeam,
                    assignedTeam is null ? null : GetTeamColor(assignedTeam));
            })
            .ToArray();

    [JSInvokable]
    public Task ReorderTeamsFromMonitor(string[] teamIds) =>
        EinsatzService.ReorderTeamsAsync(teamIds);

    private int GetTeamCountForFilter(TeamStatusFilter filter)
    {
        return filter switch
        {
            TeamStatusFilter.Running => EinsatzService.Teams.Count(team => team.IsRunning),
            TeamStatusFilter.Pausing => EinsatzService.Teams.Count(team => team.IsPausing),
            TeamStatusFilter.Ready => EinsatzService.Teams.Count(team => !team.IsRunning && !team.IsPausing),
            TeamStatusFilter.Critical => EinsatzService.Teams.Count(team => team.IsSecondWarning),
            _ => EinsatzService.Teams.Count
        };
    }

    private void ToggleTeamExpand(string teamId)
    {
        if (!_expandedTeamIds.Remove(teamId))
            _expandedTeamIds.Add(teamId);
    }

    private CollarLocation? GetLastCollarLocation(Team team)
    {
        if (string.IsNullOrWhiteSpace(team.CollarId)) return null;

        var latest = CollarTrackingService.GetLatestLocation(team.CollarId);
        if (latest is not null)
        {
            return latest;
        }

        var history = CollarTrackingService.GetLocationHistory(team.CollarId);
        return history.Count > 0 ? history[^1] : null;
    }

    private List<GlobalNotesEntry> GetLatestTeamNoteEntries(Team team, int take)
    {
        var teamEntries = EinsatzService.GlobalNotes
            .Where(note =>
                string.Equals(note.SourceTeamId, team.TeamId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(note.SourceTeamName, team.TeamName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.Timestamp)
            .Take(take)
            .ToList();
        return teamEntries;
    }

    private SearchArea? GetTeamSearchArea(Team team)
    {
        if (string.IsNullOrWhiteSpace(team.SearchAreaId))
        {
            return null;
        }

        return EinsatzService.CurrentEinsatz.SearchAreas.FirstOrDefault(area => area.Id == team.SearchAreaId);
    }

    private List<TeamTrackSnapshot> GetCompletedSearches(Team team, int take)
    {
        return EinsatzService.CurrentEinsatz.TrackSnapshots
            .Where(snapshot => string.Equals(snapshot.TeamId, team.TeamId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .Take(take)
            .ToList();
    }

    private string BuildTeamMapLink(Team team)
    {
        if (!string.IsNullOrWhiteSpace(team.CollarId))
        {
            var encoded = Uri.EscapeDataString(team.CollarId);
            return $"/einsatz-karte?focusCollarId={encoded}";
        }

        return "/einsatz-karte";
    }

    private static string GetTeamColor(Team team)
    {
        if (team.IsDroneTeam)
        {
            return "#0dcaf0";
        }

        if (team.IsSupportTeam)
        {
            return "#6c757d";
        }

        return team.DogSpecialization switch
        {
            DogSpecialization.Flaechensuche => "#4CAF50",
            DogSpecialization.Truemmersuche => "#FF9800",
            DogSpecialization.Wasserortung => "#2196F3",
            DogSpecialization.Mantrailing => "#9C27B0",
            _ => "#1e88e5"
        };
    }

    private string GetAlarmDisplay()
    {
        if (EinsatzService.CurrentEinsatz.AlarmierungsZeit.HasValue)
        {
            return EinsatzService.CurrentEinsatz.AlarmierungsZeit.Value.ToString("dd.MM.yyyy HH:mm");
        }

        if (!string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Alarmiert))
        {
            return EinsatzService.CurrentEinsatz.Alarmiert;
        }

        return EinsatzService.CurrentEinsatz.EinsatzDatum.ToString("dd.MM.yyyy HH:mm");
    }

    private void OpenCloseMissionModal()
    {
        _showCloseMissionModal = true;
        _closeMissionModalError = string.Empty;
        _closeMissionChecklistConfirmed = false;
        _closeMissionIncludeTracks = false;
        _closeMissionIncludePunkte = false;
        _closeMissionPunktCount = EinsatzService.CurrentEinsatz.MapMarkers?.Count ?? 0;
        _closeMissionResult = string.Empty;
        _closeMissionRemarks = string.Empty;
        _closeMissionEndTime = TimeOnly.FromDateTime(TimeService.Now);
        _closeMissionStatus = string.Empty;
        _closeMissionStatusIsError = false;

        _selectedPersonnelIds.Clear();
        _selectedDogIds.Clear();
        PreselectVorOrtChecklists();
    }

    private void CloseCloseMissionModal()
    {
        if (_closingMission)
        {
            return;
        }

        _showCloseMissionModal = false;
        _closeMissionModalError = string.Empty;
    }

    private void TogglePersonnelSelection(MonitorSelectionChange change)
    {
        if (change.IsSelected)
        {
            _selectedPersonnelIds.Add(change.Id);
            return;
        }

        _selectedPersonnelIds.Remove(change.Id);
    }

    private void ToggleDogSelection(MonitorSelectionChange change)
    {
        if (change.IsSelected)
        {
            _selectedDogIds.Add(change.Id);
            return;
        }

        _selectedDogIds.Remove(change.Id);
    }

    private void PreselectVorOrtChecklists()
    {
        var (personalIds, hundeIds) = EinsatzAbschlussService.PreselectVorOrt(EinsatzService.Teams, ActivePersonnel, ActiveDogs);
        _selectedPersonnelIds.UnionWith(personalIds);
        _selectedDogIds.UnionWith(hundeIds);
    }

    private async Task ConfirmCloseMissionAsync()
    {
        _closeMissionModalError = EinsatzAbschlussService.Validate(_closeMissionResult, _closeMissionChecklistConfirmed) ?? string.Empty;
        if (_closeMissionModalError.Length > 0)
        {
            return;
        }

        _closingMission = true;

        try
        {
            var (archived, pdfExport) = await EinsatzAbschluss.AbschliessenAsync(new EinsatzAbschlussAnfrage(
                EinsatzService.CurrentEinsatz.EinsatzDatum,
                _closeMissionResult,
                _closeMissionRemarks,
                _closeMissionEndTime,
                _closeMissionIncludeTracks,
                SelectedPersonnelNames,
                SelectedDogNames,
                _closeMissionKartenart,
                PunkteImBericht: _closeMissionIncludePunkte));

            _showCloseMissionModal = false;

            _closeMissionStatusIsError = !pdfExport.Success;
            _closeMissionStatus = pdfExport.Success
                ? $"Einsatz abgeschlossen. Archiviert als {archived.EinsatzNummer}. PDF erzeugt: {pdfExport.FilePath}"
                : $"Einsatz abgeschlossen und archiviert. PDF konnte nicht erzeugt werden: {pdfExport.ErrorMessage}";

            Navigation.NavigateTo("/einsatz-start", forceLoad: false);
        }
        catch (Exception ex)
        {
            _closeMissionModalError = $"Abschluss fehlgeschlagen: {ex.Message}";
            _closeMissionStatusIsError = true;
            _closeMissionStatus = "Einsatz konnte nicht abgeschlossen werden.";
        }
        finally
        {
            _closingMission = false;
        }
    }

    private async Task StartTeamAsync(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);

        // "Weiter" nach manueller Kurzpause: Track/Halsband-Historie darf nicht angetastet werden,
        // die Aufzeichnung läuft nahtlos weiter.
        var resumingFromManualPause = team?.IsManuallyPaused == true;

        // Kein Halsband zugewiesen → Dialog zur Auswahl einblenden (nur für Hundeteams)
        if (!resumingFromManualPause && team != null && !team.IsDroneTeam && !team.IsSupportTeam && string.IsNullOrEmpty(team.CollarId))
        {
            _collarSelectTeamId = teamId;
            // Zuletzt von diesem Team benutztes Halsband vorauswaehlen, wenn es frei ist
            _collarSelectCollarId = TeamSuche.CollarOptions(teamId).FirstOrDefault(c =>
                c.Id == TeamSucheService.LastCollarId(EinsatzService.CurrentEinsatz.TrackSnapshots ?? [], teamId))?.Id ?? string.Empty;
            _showCollarSelectModal = true;
            return;
        }

        try
        {
            await TeamSuche.StartAsync(teamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }
    }

    private async Task PauseTeamAsync(string teamId)
    {
        await EinsatzService.PauseTeamTimerAsync(teamId);
    }

    private async Task StopTeamAsync(string teamId)
    {
        await TeamSuche.StopAsync(teamId);
    }

    private async Task ResetTeamAsync(string teamId)
    {
        try
        {
            await EinsatzService.ResetTeamTimerAsync(teamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }
    }

    // --- Halsband-Auswahl Modal (wird gezeigt wenn beim Start kein Halsband zugewiesen ist) ---

    private async Task CollarSelectConfirmAsync()
    {
        if (_collarSelectTeamId == null) return;

        if (!string.IsNullOrEmpty(_collarSelectCollarId))
        {
            await CollarTrackingService.AssignCollarToTeamAsync(_collarSelectCollarId, _collarSelectTeamId);
        }

        _showCollarSelectModal = false;

        // Jetzt Start durchführen (Halsband ist jetzt zugewiesen oder bewusst ausgelassen)
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == _collarSelectTeamId);
        if (team != null && !string.IsNullOrEmpty(team.CollarId))
        {
            CollarTrackingService.ClearCollarHistory(team.CollarId);
        }

        try
        {
            await EinsatzService.StartTeamTimerAsync(_collarSelectTeamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }

        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private async Task CollarSelectStartWithoutAsync()
    {
        if (_collarSelectTeamId == null) return;
        _showCollarSelectModal = false;

        try
        {
            await EinsatzService.StartTeamTimerAsync(_collarSelectTeamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }

        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private void CollarSelectCancel()
    {
        _showCollarSelectModal = false;
        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private async Task RequestResetTeamAsync(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team is null)
        {
            _teamStatusMessage = "Das Team ist nicht mehr vorhanden. Bitte aktualisieren Sie die Liste.";
            _teamStatusIsError = true;
            return;
        }

        if (team.IsRunning)
        {
            _teamStatusMessage = "Die Suche läuft – bitte zuerst beenden.";
            _teamStatusIsError = true;
            return;
        }

        if (team.IsPausing && !team.IsPauseComplete)
        {
            _pauseResetTeamId = teamId;
            _showPauseResetModal = true;
        }
        else
        {
            await ResetTeamAsync(teamId);
        }
    }

    private async Task ConfirmResetTeamAsync()
    {
        _showPauseResetModal = false;
        if (_pauseResetTeamId is not null)
        {
            await ResetTeamAsync(_pauseResetTeamId);
            _pauseResetTeamId = null;
        }
    }

    private void ClosePauseResetModal()
    {
        _showPauseResetModal = false;
        _pauseResetTeamId = null;
    }

    private void EditTeam(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(entry => entry.TeamId == teamId);
        if (team == null)
        {
            return;
        }

        _editingTeamId = team.TeamId;
        _teamForm.TeamName = team.TeamName;
        _teamForm.TeamType = team.IsDroneTeam ? "drone" : team.IsSupportTeam ? "support" : "dog";
        _teamForm.DogId = team.DogId;
        _teamForm.DroneId = team.DroneId;
        _teamForm.HundefuehrerId = team.HundefuehrerId;
        _teamForm.HelferId = team.HelferIds.ElementAtOrDefault(0) ?? string.Empty;
        _teamForm.HelferId2 = team.HelferIds.ElementAtOrDefault(1) ?? string.Empty;
        _teamForm.HelferId3 = team.HelferIds.ElementAtOrDefault(2) ?? string.Empty;
        _teamForm.SearchAreaId = team.SearchAreaId;
        _teamForm.CollarId = team.CollarId ?? string.Empty;
        _teamForm.FirstWarningMinutes = team.FirstWarningMinutes;
        _teamForm.SecondWarningMinutes = team.SecondWarningMinutes;
        _teamForm.Notes = team.Notes;
        _showTeamModal = true;
        ClearTeamFormMessage();
    }

    private async Task SaveTeamAsync()
    {
        ClearTeamFormMessage();

        if (!ValidateTeamForm(out var validationMessage))
        {
            SetTeamFormMessage(validationMessage, true);
            return;
        }

        if (_editingTeamId is null)
        {
            var newTeam = BuildNewTeam();
            await EinsatzService.AddTeamAsync(newTeam);
            await SyncTeamSearchAreaAsync(newTeam, string.Empty);
            await SyncCollarAssignmentAsync(newTeam);

            SetTeamFormMessage("Team wurde gespeichert.", false);
            ResetTeamForm();
            _showTeamModal = false;
            return;
        }

        var existingTeam = await EinsatzService.GetTeamByIdAsync(_editingTeamId);
        if (existingTeam == null)
        {
            SetTeamFormMessage("Das zu bearbeitende Team wurde nicht gefunden.", true);
            return;
        }

        var previousAreaId = existingTeam.SearchAreaId;
        var previousCollarId = existingTeam.CollarId;
        ApplyFormToExistingTeam(existingTeam);
        await EinsatzService.UpdateTeamAsync(existingTeam);
        await SyncTeamSearchAreaAsync(existingTeam, previousAreaId);
        await SyncCollarAssignmentAsync(existingTeam, previousCollarId);
        await EinsatzService.AddGlobalNoteWithSourceAsync(
            $"{existingTeam.TeamName} wurde aktualisiert.",
            existingTeam.TeamId,
            existingTeam.TeamName,
            "Team",
            GlobalNotesEntryType.System,
            LeitungsGeraetNamen.Absender(Geraet));

        SetTeamFormMessage("Team wurde aktualisiert.", false);
        ResetTeamForm();
        _showTeamModal = false;
    }

    private async Task SyncTeamSearchAreaAsync(Team team, string previousAreaId)
    {
        if (!string.IsNullOrWhiteSpace(previousAreaId) && previousAreaId != _teamForm.SearchAreaId)
        {
            await EinsatzService.AssignTeamToSearchAreaAsync(previousAreaId, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(_teamForm.SearchAreaId))
        {
            await EinsatzService.AssignTeamToSearchAreaAsync(_teamForm.SearchAreaId, team.TeamId);
        }
        else
        {
            team.SearchAreaId = string.Empty;
            team.SearchAreaName = string.Empty;
            await EinsatzService.UpdateTeamAsync(team);
        }
    }

    private async Task SyncCollarAssignmentAsync(Team team, string? previousCollarId = null)
    {
        if (!string.IsNullOrWhiteSpace(previousCollarId) && previousCollarId != _teamForm.CollarId)
        {
            await CollarTrackingService.UnassignCollarAsync(previousCollarId);
        }

        if (!string.IsNullOrWhiteSpace(_teamForm.CollarId))
        {
            if (_teamForm.CollarId != previousCollarId)
            {
                await CollarTrackingService.AssignCollarToTeamAsync(_teamForm.CollarId, team.TeamId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(previousCollarId))
        {
            team.CollarId = null;
            team.CollarName = null;
            await EinsatzService.UpdateTeamAsync(team);
        }
    }

    private bool ValidateTeamForm(out string validationMessage)
    {
        if (string.IsNullOrWhiteSpace(_teamForm.TeamName))
        {
            validationMessage = "Bitte einen Teamnamen vergeben.";
            return false;
        }

        if (_teamForm.FirstWarningMinutes <= 0 || _teamForm.SecondWarningMinutes <= 0)
        {
            validationMessage = "Warnschwellen müssen größer als 0 sein.";
            return false;
        }

        if (_teamForm.SecondWarningMinutes <= _teamForm.FirstWarningMinutes)
        {
            validationMessage = "Warnung 2 muss größer als Warnung 1 sein.";
            return false;
        }

        if (_teamForm.TeamType == "dog")
        {
            if (string.IsNullOrWhiteSpace(_teamForm.DogId))
            {
                validationMessage = "Bitte einen Hund auswählen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
            {
                validationMessage = "Bitte einen Hundeführer auswählen.";
                return false;
            }
        }

        if (_teamForm.TeamType == "drone")
        {
            if (string.IsNullOrWhiteSpace(_teamForm.DroneId))
            {
                validationMessage = "Bitte eine Drohne auswählen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
            {
                validationMessage = "Bitte einen Piloten auswählen.";
                return false;
            }
        }

        if (_teamForm.TeamType == "support" && string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
        {
            validationMessage = "Bitte mindestens eine Person zuordnen.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private Team BuildNewTeam()
    {
        var team = new Team();
        ApplyFormValues(team);
        return team;
    }

    private void ApplyFormToExistingTeam(Team team)
    {
        ApplyFormValues(team);
    }

    private void ApplyFormValues(Team team)
    {
        var resolvedTeamName = ResolveTeamNameFromForm(_teamForm);
        team.TeamName = resolvedTeamName;
        team.HundefuehrerId = _teamForm.HundefuehrerId;
        ApplyHelfersToTeam(team);
        team.DogId = string.Empty;
        team.DogName = string.Empty;
        team.DogSpecialization = DogSpecialization.None;
        team.DroneId = string.Empty;
        team.DroneType = string.Empty;
        team.IsDroneTeam = _teamForm.TeamType == "drone";
        team.IsSupportTeam = _teamForm.TeamType == "support";
        team.FirstWarningMinutes = _teamForm.FirstWarningMinutes;
        team.SecondWarningMinutes = _teamForm.SecondWarningMinutes;
        team.Notes = _teamForm.Notes.Trim();

        var primaryPerson = _personalList.FirstOrDefault(person => person.Id == _teamForm.HundefuehrerId);
        team.HundefuehrerName = primaryPerson?.FullName ?? string.Empty;

        if (_teamForm.TeamType == "dog")
        {
            var dog = _dogList.FirstOrDefault(entry => entry.Id == _teamForm.DogId);
            team.DogId = dog?.Id ?? string.Empty;
            team.DogName = dog?.Name ?? string.Empty;
            team.DogSpecialization = dog?.Specializations ?? DogSpecialization.None;
        }
        else if (_teamForm.TeamType == "drone")
        {
            var drone = _droneList.FirstOrDefault(entry => entry.Id == _teamForm.DroneId);
            team.DroneId = drone?.Id ?? string.Empty;
            team.DroneType = drone?.DisplayName ?? string.Empty;
        }
    }

    private void ApplyHelfersToTeam(Team team)
    {
        var ids = new[] { _teamForm.HelferId, _teamForm.HelferId2, _teamForm.HelferId3 };
        team.HelferIds.Clear();
        team.HelferNames.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!seen.Add(id)) continue;
            var person = _personalList.FirstOrDefault(p => p.Id == id);
            team.HelferIds.Add(id);
            team.HelferNames.Add(person?.FullName ?? string.Empty);
        }
    }

    private async Task OnTeamTypeChangedAsync()
    {
        _teamForm.DogId = string.Empty;
        _teamForm.DroneId = string.Empty;
        _teamForm.HelferId = string.Empty;
        _teamForm.HelferId2 = string.Empty;
        _teamForm.HelferId3 = string.Empty;

        if (_teamForm.TeamType == "support")
        {
            _teamForm.SearchAreaId = string.Empty;
        }

        SyncTeamNameFromSelection();

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnDogChangedAsync(ChangeEventArgs args)
    {
        ApplyDogSelection(_teamForm, _dogList, args.Value?.ToString());
        await InvokeAsync(StateHasChanged);
    }

    internal static void ApplyDogSelection(TeamEditorModel form, IEnumerable<DogEntry> dogs, string? dogId)
    {
        form.DogId = dogId ?? string.Empty;
        var dog = dogs.FirstOrDefault(entry => entry.Id == form.DogId);
        if (dog != null)
        {
            if (string.IsNullOrWhiteSpace(form.HundefuehrerId)
                || !dog.HundefuehrerIds.Contains(form.HundefuehrerId))
            {
                form.HundefuehrerId = dog.PrimaryHundefuehrerId;
            }
        }

        if (string.IsNullOrWhiteSpace(form.TeamName))
        {
            form.TeamName = dog?.Name?.Trim() ?? string.Empty;
        }
    }

    private async Task OnDroneChangedAsync()
    {
        SyncTeamNameFromSelection();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnHandlerChangedAsync()
    {
        if (!string.IsNullOrWhiteSpace(_teamForm.DogId) && AvailableDogs.All(dog => dog.Id != _teamForm.DogId))
        {
            _teamForm.DogId = string.Empty;
            SyncTeamNameFromSelection();
        }

        await InvokeAsync(StateHasChanged);
    }

    private void SyncTeamNameFromSelection()
    {
        if (string.IsNullOrWhiteSpace(_teamForm.TeamName))
        {
            _teamForm.TeamName = ResolveDefaultTeamNameFromSelection();
        }
    }

    private string ResolveDefaultTeamNameFromSelection()
    {
        if (_teamForm.TeamType == "dog")
        {
            var dogName = _dogList.FirstOrDefault(entry => entry.Id == _teamForm.DogId)?.Name;
            return dogName?.Trim() ?? string.Empty;
        }

        if (_teamForm.TeamType == "drone")
        {
            var droneName = _droneList.FirstOrDefault(entry => entry.Id == _teamForm.DroneId)?.DisplayName;
            return droneName?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    internal static string ResolveTeamNameFromForm(TeamEditorModel form) =>
        form.TeamName.Trim();


}
