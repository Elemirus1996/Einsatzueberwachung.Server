using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Native Einsatzleitung, Stufe 3: Team-Aktionen, Notizen, Funk, Mitteilung (docs/APP_API.md Abschnitte 6 und 7)</summary>
public sealed partial class ElAppController
{
    /// <summary>Koerper aller Aktionen; je Endpunkt werden nur die passenden Felder gelesen</summary>
    public sealed record ElAktion(string? Id, string? HalsbandId = null, string? Text = null, string? TeamId = null, string? AntwortAuf = null);

    public const int MaxTextLaenge = 1000;

    [HttpPost("teams/{teamId}/start")]
    public Task<IActionResult> TeamStart(string teamId, [FromBody] ElAktion? aktion) => TeamAktion(teamId, aktion, async team =>
    {
        if (team.IsRunning || team.IsManuallyPaused) return Konflikt("Die Suche läuft bereits.");
        var halsband = string.IsNullOrWhiteSpace(aktion!.HalsbandId) ? null : aktion.HalsbandId.Trim();
        if (halsband is not null && halsband != team.CollarId
            && halsbaender?.GetAvailableCollars().Any(c => c.Id == halsband) != true)
            return Konflikt("Das Halsband ist schon einem anderen Team zugewiesen oder unbekannt.");
        await suche!.StartWithCollarAsync(team.TeamId, halsband);
        return null;
    }, "Suche gestartet");

    [HttpPost("teams/{teamId}/pause")]
    public Task<IActionResult> TeamPause(string teamId, [FromBody] ElAktion? aktion) => TeamAktion(teamId, aktion, async team =>
    {
        if (!team.IsRunning) return Konflikt(team.IsManuallyPaused ? "Die Suche ist bereits pausiert." : "Es läuft keine Suche.");
        await einsatz!.PauseTeamTimerAsync(team.TeamId);
        return null;
    }, "Suche pausiert");

    [HttpPost("teams/{teamId}/weiter")]
    public Task<IActionResult> TeamWeiter(string teamId, [FromBody] ElAktion? aktion) => TeamAktion(teamId, aktion, async team =>
    {
        if (!team.IsManuallyPaused) return Konflikt(team.IsRunning ? "Die Suche läuft bereits." : "Die Suche ist nicht pausiert.");
        await suche!.StartAsync(team.TeamId);
        return null;
    }, "Suche fortgesetzt");

    [HttpPost("teams/{teamId}/stopp")]
    public Task<IActionResult> TeamStopp(string teamId, [FromBody] ElAktion? aktion) => TeamAktion(teamId, aktion, async team =>
    {
        if (!team.IsRunning && !team.IsManuallyPaused) return Konflikt("Es läuft keine Suche.");
        await suche!.StopAsync(team.TeamId);
        return null;
    }, "Suche beendet");

    [HttpPost("teams/{teamId}/zuruecksetzen")]
    public Task<IActionResult> TeamZuruecksetzen(string teamId, [FromBody] ElAktion? aktion) => TeamAktion(teamId, aktion, async team =>
    {
        if (team.IsRunning) return Konflikt("Die Suche läuft – bitte zuerst beenden.");
        await einsatz!.ResetTeamTimerAsync(team.TeamId);
        return null;
    }, "Suchzeit zurückgesetzt");

    [HttpPost("teams/{teamId}/mitteilung")]
    public Task<IActionResult> TeamMitteilen(string teamId, [FromBody] ElAktion? aktion) => Aktion(aktion, () =>
    {
        if (TextFehler(aktion!.Text) is { } fehler) return Task.FromResult<IActionResult>(fehler);
        if (einsatz!.Teams.FirstOrDefault(t => t.TeamId == teamId) is not { } team) return Task.FromResult<IActionResult>(TeamFehlt());
        var text = aktion.Text!.Trim();
        if (teamNachrichten is not null) teamNachrichten.BroadcastTeamMessage(team.TeamId, text);
        else einsatz.TeamMitteilungSpeichern(team.TeamId, text);
        audit?.Log("Mitteilung an Team (EL-App)", $"{Geraet(null)}: {text}", team.TeamName);
        return Task.FromResult<IActionResult>(
            Ok(new { doppelt = false, mitteilungId = einsatz.TeamMitteilungen(team.TeamId).LastOrDefault()?.Id }));
    });

    [HttpPost("notizen")]
    public Task<IActionResult> NotizAnlegen([FromBody] ElAktion? aktion) => Aktion(aktion, async () =>
    {
        if (TextFehler(aktion!.Text) is { } fehler) return fehler;
        var text = aktion.Text!.Trim();
        if (!string.IsNullOrWhiteSpace(aktion.TeamId))
        {
            if (einsatz!.Teams.FirstOrDefault(t => t.TeamId == aktion.TeamId) is not { } team) return TeamFehlt();
            text = $"[{team.TeamName}] {text}";
        }
        await einsatz!.AddElNotizAsync(text);
        var notiz = einsatz.CurrentEinsatz.ElNotizen?.FirstOrDefault();
        audit?.Log("Notiz der Einsatzleitung (EL-App)", Geraet(null));
        return Ok(new { doppelt = false, notizId = notiz?.Id });
    });

    [HttpPost("notizen/{notizId}/loeschen")]
    public Task<IActionResult> NotizLoeschen(string notizId, [FromBody] ElAktion? aktion) => Aktion(aktion, async () =>
    {
        // Loeschbar sind nur Notizen der Einsatzleitung, wie in /el – Funk und Team-Meldungen bleiben im Protokoll
        if (einsatz!.CurrentEinsatz.ElNotizen?.Any(n => n.Id == notizId) != true)
            return NotFound(new { error = "Notiz nicht gefunden oder nicht löschbar." });
        await einsatz.DeleteElNotizAsync(notizId);
        ereignisse?.Senden("notiz", new { id = notizId });
        audit?.Log("Notiz gelöscht (EL-App)", Geraet(null));
        return Ok(new { doppelt = false });
    });

    [HttpPost("funk")]
    public Task<IActionResult> Funk([FromBody] ElAktion? aktion) => Aktion(aktion, async () =>
    {
        if (TextFehler(aktion!.Text) is { } fehler) return fehler;
        if (funk is null) return NotFound();
        var text = aktion.Text!.Trim();
        var absender = LeitungsGeraetNamen.Absender(geraete.NameFuer(HttpContext.Connection.RemoteIpAddress));

        if (!string.IsNullOrWhiteSpace(aktion.AntwortAuf))
        {
            if (einsatz!.GlobalNotes.FirstOrDefault(n => n.Id == aktion.AntwortAuf) is not { } bezug)
                return NotFound(new { error = "Funkspruch nicht gefunden." });
            var antwort = await einsatz.AddReplyToNoteAsync(bezug.Id, text, "einsatzleitung", absender, absender);
            audit?.Log("Funk-Antwort (EL-App)", Geraet(null));
            return Ok(new { doppelt = false, notizId = bezug.Id, antwortId = antwort.Id });
        }

        if (!string.IsNullOrWhiteSpace(aktion.TeamId))
        {
            if (einsatz!.Teams.FirstOrDefault(t => t.TeamId == aktion.TeamId) is not { } team) return TeamFehlt();
            text = $"@{team.TeamName} {text}"; // wie der Empfaenger-Knopf in /el
        }
        var spruch = await funk.AddMessageAsync(new CreateRadioMessageRequest(text, "einsatzleitung", absender, absender));
        audit?.Log("Funk (EL-App)", Geraet(null));
        return Ok(new { doppelt = false, notizId = spruch.NotizId });
    });

    [HttpGet("notizen")]
    public IActionResult GetNotizen([FromQuery] string? vor, [FromQuery] int anzahl = 50)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) || einsatz is null) return Gone();
        return Ok(ElNotizen.Liste(einsatz, vor, Math.Clamp(anzahl, 1, 200)));
    }

    // ── Gemeinsamer Rahmen ───────────────────────────────────────

    private Task<IActionResult> TeamAktion(string teamId, ElAktion? aktion, Func<Team, Task<IActionResult?>> wirkung, string protokoll) =>
        Aktion(aktion, async () =>
        {
            if (suche is null) return NotFound();
            if (einsatz!.Teams.FirstOrDefault(t => t.TeamId == teamId) is not { } team) return TeamFehlt();
            IActionResult? abgelehnt;
            try { abgelehnt = await wirkung(team); }
            catch (InvalidOperationException ex) { abgelehnt = Konflikt(ex.Message); } // z. B. Hund laeuft in einem anderen Team
            if (abgelehnt is not null) return abgelehnt;
            audit?.Log($"{protokoll} (EL-App)", Geraet(null), team.TeamName);
            return Ok(new { doppelt = false, suche = AppLage.Suche(team, true) });
        });

    /// <summary>Token, Einsatz und id pruefen, dann genau einmal ausfuehren</summary>
    private async Task<IActionResult> Aktion(ElAktion? aktion, Func<Task<IActionResult>> ausfuehren)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) || einsatz is null || aktionen is null) return Gone();
        if (string.IsNullOrWhiteSpace(aktion?.Id) || aktion.Id.Length > 64)
            return BadRequest(new { error = "id fehlt oder ist zu lang (höchstens 64 Zeichen)." });
        if (!ElPositionService.Laeuft(einsatz.CurrentEinsatz))
            return Konflikt("Kein laufender Einsatz.");
        return await aktionen.AusfuehrenAsync(aktion.Id, ausfuehren, DateTime.UtcNow);
    }

    private ConflictObjectResult Konflikt(string text) => Conflict(new { error = text });

    private NotFoundObjectResult TeamFehlt() => NotFound(new { error = "Team nicht gefunden." });

    private BadRequestObjectResult? TextFehler(string? text) =>
        string.IsNullOrWhiteSpace(text) ? BadRequest(new { error = "Text fehlt." })
        : text.Length > MaxTextLaenge ? BadRequest(new { error = $"Text zu lang (höchstens {MaxTextLaenge} Zeichen)." })
        : null;
}

/// <summary>GET /notizen der Einsatzleitung: EL-Notizen und Einsatz-Notizen (Funk, Team-Meldungen, System) wie in /el</summary>
public static class ElNotizen
{
    private sealed record Eintrag(string Id, string Art, string Text, DateTime Zeit, string Von, string? TeamId, bool Loeschbar, object Antworten);

    public static object Liste(Einsatzueberwachung.Domain.Interfaces.IEinsatzService einsatz, string? vor, int anzahl)
    {
        var alle = (einsatz.CurrentEinsatz.ElNotizen ?? [])
            .Select(n => new Eintrag(n.Id, "notiz", string.IsNullOrWhiteSpace(n.Prefix) ? n.Text : $"{n.Prefix}: {n.Text}",
                AppZeit.Utc(n.Timestamp), LeitungsGeraetNamen.Einsatzleitung, null, true, Array.Empty<object>()))
            .Concat(einsatz.GlobalNotes.ToList().Select(n => new Eintrag(n.Id, Art(n), n.Text, AppZeit.Utc(n.Timestamp),
                string.IsNullOrWhiteSpace(n.SourceTeamName) ? "System" : n.SourceTeamName,
                string.IsNullOrWhiteSpace(n.SourceTeamId) || n.SourceTeamId == "einsatzleitung" ? null : n.SourceTeamId, false,
                (n.Replies ?? []).OrderBy(a => a.Timestamp)
                    .Select(a => new { id = a.Id, text = a.Text, zeit = AppZeit.Utc(a.Timestamp), von = a.SourceTeamName }).ToList())))
            .OrderByDescending(e => e.Zeit)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        var start = 0;
        if (!string.IsNullOrWhiteSpace(vor))
        {
            var index = alle.FindIndex(e => e.Id == vor);
            start = index < 0 ? alle.Count : index + 1;
        }
        var seite = alle.Skip(start).Take(anzahl).ToList();
        return new
        {
            notizen = seite.Select(e => new
            {
                id = e.Id, art = e.Art, text = e.Text, zeit = e.Zeit, von = e.Von, teamId = e.TeamId,
                loeschbar = e.Loeschbar, antworten = e.Antworten
            }),
            weitere = start + seite.Count < alle.Count
        };
    }

    private static string Art(GlobalNotesEntry n) =>
        ElAppEreignisse.IstFunk(n) ? "funk"
        : n.Type is Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.System
            or Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.EinsatzUpdate ? "system"
        : n.Type == Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.Manual && string.IsNullOrWhiteSpace(n.SourceTeamId) ? "notiz"
        : "status";
}
