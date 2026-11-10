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
{    private async Task AddGlobalNoteAsync(MonitorNoteSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Text))
            return;

        var type = submission.NoteType == "Funk" ? "Funk" : "Notiz";
        var (sourceId, sourceName, createdBy) = ResolveSelectedSource(submission.SourceId);
        await EinsatzService.AddGlobalNoteWithSourceAsync(
            submission.Text.Trim(),
            sourceId,
            sourceName,
            type,
            GlobalNotesEntryType.Manual,
            createdBy);
    }

    private async Task AddQuickNoteAsync(MonitorQuickNoteSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Text))
            return;

        var selectedSource = string.IsNullOrWhiteSpace(submission.SourceId) ? "einsatzleitung" : submission.SourceId;
        var (sourceId, sourceName, createdBy) = ResolveSelectedSource(selectedSource);
        var noteText = $"[{Ortszeit.Jetzt:HH:mm}] {submission.Text.Trim()}";

        await EinsatzService.AddGlobalNoteWithSourceAsync(
            noteText,
            sourceId,
            sourceName,
            "Notiz",
            GlobalNotesEntryType.Manual,
            createdBy);
    }

    private async Task AddReplyAsync(MonitorReplySubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Text))
            return;

        var (resolvedSourceId, sourceName, createdBy) = ResolveSelectedSource(submission.SourceId);

        await EinsatzService.AddReplyToNoteAsync(
            submission.NoteId,
            submission.Text.Trim(),
            resolvedSourceId,
            sourceName,
            createdBy);
    }

    private (string SourceId, string SourceName, string CreatedBy) ResolveSelectedSource(string sourceId)
    {
        var leitung = LeitungsGeraetNamen.Absender(Geraet);
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId == "einsatzleitung")
        {
            return ("einsatzleitung", leitung, leitung);
        }

        var selectedTeam = EinsatzService.Teams.FirstOrDefault(team => team.TeamId == sourceId);
        if (selectedTeam is null)
        {
            return ("einsatzleitung", leitung, leitung);
        }

        return (selectedTeam.TeamId, selectedTeam.TeamName, selectedTeam.TeamName);
    }


}
