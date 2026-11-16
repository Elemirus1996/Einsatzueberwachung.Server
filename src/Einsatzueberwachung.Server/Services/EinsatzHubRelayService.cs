using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Einsatzueberwachung.Server.Services;

public sealed class EinsatzHubRelayService : IHostedService
{
    private readonly IEinsatzService _einsatzService;
    private readonly IHubContext<EinsatzHub> _hubContext;

    public EinsatzHubRelayService(IEinsatzService einsatzService, IHubContext<EinsatzHub> hubContext)
    {
        _einsatzService = einsatzService;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _einsatzService.EinsatzChanged += OnEinsatzChanged;
        _einsatzService.TeamAdded += OnTeamAdded;
        _einsatzService.TeamRemoved += OnTeamRemoved;
        _einsatzService.TeamUpdated += OnTeamUpdated;
        _einsatzService.NoteAdded += OnNoteAdded;
        _einsatzService.SzenarioChanged += OnSzenarioChanged;
        _einsatzService.VermisstenAdded += OnVermisstenUpserted;
        _einsatzService.VermisstenUpdated += OnVermisstenUpserted;
        _einsatzService.VermisstenRemoved += OnVermisstenRemoved;
        _einsatzService.TruemmerKarteAdded += OnTruemmerKarteAdded;
        _einsatzService.TruemmerKarteRemoved += OnTruemmerKarteRemoved;
        _einsatzService.TruemmerAreaUpserted += OnTruemmerAreaUpserted;
        _einsatzService.TruemmerAreaRemoved += OnTruemmerAreaRemoved;
        _einsatzService.TeamPhoneTrackPointAdded += OnTeamPhoneTrackPointAdded;
        _einsatzService.ResourcesChanged += OnResourcesChanged;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _einsatzService.EinsatzChanged -= OnEinsatzChanged;
        _einsatzService.TeamAdded -= OnTeamAdded;
        _einsatzService.TeamRemoved -= OnTeamRemoved;
        _einsatzService.TeamUpdated -= OnTeamUpdated;
        _einsatzService.NoteAdded -= OnNoteAdded;
        _einsatzService.SzenarioChanged -= OnSzenarioChanged;
        _einsatzService.VermisstenAdded -= OnVermisstenUpserted;
        _einsatzService.VermisstenUpdated -= OnVermisstenUpserted;
        _einsatzService.VermisstenRemoved -= OnVermisstenRemoved;
        _einsatzService.TruemmerKarteAdded -= OnTruemmerKarteAdded;
        _einsatzService.TruemmerKarteRemoved -= OnTruemmerKarteRemoved;
        _einsatzService.TruemmerAreaUpserted -= OnTruemmerAreaUpserted;
        _einsatzService.TruemmerAreaRemoved -= OnTruemmerAreaRemoved;
        _einsatzService.TeamPhoneTrackPointAdded -= OnTeamPhoneTrackPointAdded;
        _einsatzService.ResourcesChanged -= OnResourcesChanged;

        return Task.CompletedTask;
    }

    private void OnEinsatzChanged()
    {
        _ = PublishAsync("einsatz.changed", new
        {
            einsatz = _einsatzService.CurrentEinsatz,
            teams = _einsatzService.Teams.Count,
            notes = _einsatzService.GlobalNotes.Count
        });
    }

    private void OnSzenarioChanged()
    {
        _ = PublishAsync("szenario.changed", new
        {
            szenario = _einsatzService.CurrentEinsatz.Szenario.ToString(),
            szenarioValue = (int)_einsatzService.CurrentEinsatz.Szenario
        });
    }

    private void OnVermisstenUpserted(VermisstenInfo info)
        => _ = PublishAsync("vermissten.upserted", info);

    private void OnVermisstenRemoved(Guid id)
        => _ = PublishAsync("vermissten.removed", new { id });

    private void OnTruemmerKarteAdded(TruemmerKarte karte)
        => _ = PublishAsync("truemmer.karte.added", karte);

    private void OnTruemmerKarteRemoved(Guid id)
        => _ = PublishAsync("truemmer.karte.removed", new { id });

    private void OnTruemmerAreaUpserted(TruemmerArea area)
        => _ = PublishAsync("truemmer.area.upserted", area);

    private void OnTruemmerAreaRemoved(Guid id)
        => _ = PublishAsync("truemmer.area.removed", new { id });

    private void OnTeamAdded(Team team) => _ = PublishAsync("team.added", team);

    private void OnTeamRemoved(Team team) => _ = PublishAsync("team.removed", team);

    private void OnTeamUpdated(Team team) => _ = PublishAsync("team.updated", team);

    private void OnNoteAdded(GlobalNotesEntry note) => _ = PublishAsync("note.added", note);

    private void OnTeamPhoneTrackPointAdded(string teamId, string teamName, TeamPhoneLocation location)
    {
        _ = PublishAsync("phone.track.point", new
        {
            teamId,
            teamName,
            lat = location.Latitude,
            lng = location.Longitude,
            timestamp = location.Timestamp
        });
    }

    private void OnResourcesChanged()
        => _ = PublishAsync("resources.changed", _einsatzService.GetEinsatzResources());

    private Task PublishAsync(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return _hubContext.Clients.All.SendAsync("einsatz:update", eventName, json);
    }
}
