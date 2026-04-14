using Microsoft.AspNetCore.SignalR;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Hubs;

public class EinsatzHub : Hub
{
    private readonly IEinsatzService _einsatzService;
    private readonly ICollarTrackingService _collarTrackingService;

    public EinsatzHub(IEinsatzService einsatzService, ICollarTrackingService collarTrackingService)
    {
        _einsatzService = einsatzService;
        _collarTrackingService = collarTrackingService;
    }

    public Task JoinChannel(string channel)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, channel);
    }

    public Task LeaveChannel(string channel)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, channel);
    }

    public Task BroadcastUpdate(string channel, string eventName, string payload)
    {
        return Clients.Group(channel).SendAsync("einsatz:update", eventName, payload);
    }

    public EinsatzData GetCurrentEinsatz()
    {
        return _einsatzService.CurrentEinsatz;
    }

    public List<Team> GetTeamsSnapshot()
    {
        return _einsatzService.Teams
            .OrderBy(t => t.TeamName)
            .ToList();
    }

    public List<GlobalNotesEntry> GetNotesSnapshot(string filter = "alle")
    {
        IEnumerable<GlobalNotesEntry> query = _einsatzService.GlobalNotes
            .OrderByDescending(n => n.Timestamp);

        if (filter == "funk")
        {
            query = query.Where(n => n.SourceType == "Funk");
        }
        else if (filter == "notiz")
        {
            query = query.Where(n => n.SourceType != "Funk");
        }

        return query.Take(200).ToList();
    }

    public async Task StartEinsatzFromMobile(EinsatzData einsatzData, string? initialNote)
    {
        await _einsatzService.StartEinsatzAsync(einsatzData);

        if (!string.IsNullOrWhiteSpace(initialNote))
        {
            await _einsatzService.AddGlobalNoteWithSourceAsync(
                initialNote,
                "mobile",
                "Mobile",
                "Notiz",
                GlobalNotesEntryType.Manual,
                "Mobile");
        }
    }

    public Task AddGlobalNoteFromMobile(string text, string sourceType)
    {
        return _einsatzService.AddGlobalNoteWithSourceAsync(
            text,
            "mobile",
            "Mobile",
            sourceType,
            GlobalNotesEntryType.Manual,
            "Mobile");
    }

    public Task AddReplyFromMobile(string noteId, string text)
    {
        return _einsatzService.AddReplyToNoteAsync(
            noteId,
            text,
            "mobile",
            "Mobile",
            "Mobile");
    }

    // --- Collar Tracking ---

    public List<Collar> GetCollarsSnapshot()
    {
        return _collarTrackingService.Collars.ToList();
    }

    public List<Collar> GetAvailableCollars()
    {
        return _collarTrackingService.GetAvailableCollars().ToList();
    }

    public List<CollarLocation> GetCollarHistory(string collarId)
    {
        return _collarTrackingService.GetLocationHistory(collarId).ToList();
    }

    public Task AssignCollarToTeam(string collarId, string teamId)
    {
        return _collarTrackingService.AssignCollarToTeamAsync(collarId, teamId);
    }

    public Task UnassignCollar(string collarId)
    {
        return _collarTrackingService.UnassignCollarAsync(collarId);
    }
}
