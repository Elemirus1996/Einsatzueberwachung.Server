using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Native Einsatzleitung, Stufe 4b: Karte, Tracks, Halsbaender (docs/APP_API.md Abschnitt 6)</summary>
public sealed partial class ElAppController
{
    [HttpGet("karte")]
    public IActionResult GetKarte()
    {
        if (!Lesen()) return Zugang();
        var e = einsatz!.CurrentEinsatz;
        if (!ElPositionService.Laeuft(e)) return Ok(new { suchgebiete = Array.Empty<object>(), positionen = Array.Empty<object>() });

        var abdeckung = abdeckungen?.Aktuell().ToDictionary(a => a.AreaId, a => Math.Round(a.Prozent, 1)) ?? [];
        var teams = einsatz.Teams.ToDictionary(t => t.TeamId, t => t.TeamName);
        var positionenListe = new List<object>();

        foreach (var hb in halsbaender?.Collars ?? [])
        {
            if (halsbaender!.GetLatestLocation(hb.Id) is not { } ort) continue;
            positionenListe.Add(new
            {
                art = "halsband", id = hb.Id, name = hb.CollarName ?? hb.Id,
                teamId = einsatz.Teams.FirstOrDefault(t => t.CollarId == hb.Id)?.TeamId,
                lat = ort.Latitude, lng = ort.Longitude, zeit = (DateTime?)AppZeit.Utc(ort.Timestamp), akku = ort.BatteryLevel
            });
        }
        foreach (var (teamId, ort) in einsatz.PhoneLocations)
            positionenListe.Add(new
            {
                art = "handy", id = teamId, name = teams.GetValueOrDefault(teamId, teamId), teamId = (string?)teamId,
                lat = ort.Latitude, lng = ort.Longitude, zeit = (DateTime?)AppZeit.Utc(ort.Timestamp), akku = ort.BatteryPercent
            });
        foreach (var p in positionen.Aktuell)
            positionenListe.Add(new
            {
                art = "el", id = p.Geraet, name = p.Geraet, teamId = (string?)null,
                lat = p.Latitude, lng = p.Longitude, zeit = (DateTime?)AppZeit.Utc(p.Zeit), akku = p.BatteryPercent
            });
        if (e.ElwPosition is { } elw)
            positionenListe.Add(new
            {
                art = "elw", id = "elw", name = "ELW", teamId = (string?)null,
                lat = elw.Latitude, lng = elw.Longitude, zeit = (DateTime?)null, akku = (int?)null
            });

        return Ok(new
        {
            suchgebiete = (e.SearchAreas ?? []).Select(a => new
            {
                id = a.Id,
                name = a.Name,
                farbe = a.Color,
                teamId = string.IsNullOrWhiteSpace(a.AssignedTeamId) ? null : a.AssignedTeamId,
                abgeschlossen = a.IsCompleted,
                abdeckungProzent = abdeckung.TryGetValue(a.Id, out var prozent) ? prozent : (double?)null,
                punkte = (a.Coordinates ?? []).Select(c => new { lat = c.Latitude, lng = c.Longitude }).ToList()
            }).ToList(),
            positionen = positionenListe
        });
    }

    /// <summary>Wie /tracks der Team-Rolle, aber fuer alle Teams; mit seit nur neue Punkte der laufenden Suchen</summary>
    [HttpGet("tracks")]
    public IActionResult GetTracks([FromQuery] long? seit)
    {
        if (!Lesen()) return Zugang();
        var e = einsatz!.CurrentEinsatz;
        object Punkt(double lat, double lng, DateTime t) => new { lat, lng, t = AppZeit.UnixMs(t) };
        List<object> Neue(IEnumerable<(double Lat, double Lng, DateTime Zeit)> punkte) => punkte
            .Select(p => (p.Lat, p.Lng, p.Zeit, T: AppZeit.UnixMs(p.Zeit)))
            .Where(p => seit is null || p.T > seit).OrderBy(p => p.T).Select(p => Punkt(p.Lat, p.Lng, p.Zeit)).ToList();

        var laufend = einsatz.Teams.Where(t => t.IsRunning || t.IsManuallyPaused).Select(t => new
        {
            teamId = t.TeamId,
            hund = Neue(string.IsNullOrWhiteSpace(t.CollarId) || halsbaender is null ? []
                : halsbaender.GetLocationHistory(t.CollarId).Select(p => (p.Latitude, p.Longitude, p.Timestamp))),
            mensch = Neue(einsatz.GetPhoneTrackHistory(t.TeamId).Select(p => (p.Latitude, p.Longitude, p.Timestamp)))
        }).ToList();

        return Ok(new
        {
            serverZeit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            laufend,
            abgeschlossen = seit is not null ? null : (e.CompletedSearches ?? []).OrderBy(s => s.SearchStart).Select(s => new
            {
                sucheId = s.Id,
                teamId = s.TeamId,
                beginn = AppZeit.Utc(s.SearchStart),
                ende = AppZeit.Utc(s.SearchEnd),
                tracks = s.Tracks.Select(t => new
                {
                    art = t.TrackType == TrackType.HumanTrack ? "mensch" : "hund",
                    farbe = t.Color,
                    punkte = t.Points.Select(p => Punkt(p.Latitude, p.Longitude, p.Timestamp)).ToList()
                }).ToList()
            }).ToList()
        });
    }

    /// <summary>Freie Halsbaender fuer die Rueckfrage beim Teamstart, dazu je Team das zuletzt benutzte</summary>
    [HttpGet("halsbaender")]
    public IActionResult GetHalsbaender()
    {
        if (!Lesen()) return Zugang();
        var snapshots = einsatz!.CurrentEinsatz.TrackSnapshots ?? [];
        return Ok(new
        {
            frei = (halsbaender?.GetAvailableCollars() ?? []).OrderBy(c => c.CollarName, StringComparer.OrdinalIgnoreCase).Select(c =>
            {
                var ort = halsbaender!.GetLatestLocation(c.Id);
                return new { id = c.Id, name = c.CollarName ?? c.Id, zeit = ort is null ? (DateTime?)null : AppZeit.Utc(ort.Timestamp), akku = ort?.BatteryLevel };
            }).ToList(),
            zuletzt = einsatz.Teams
                .Select(t => new { teamId = t.TeamId, halsbandId = TeamSucheService.LastCollarId(snapshots, t.TeamId) })
                .Where(z => z.halsbandId is not null)
                .ToList()
        });
    }

    private bool Lesen() =>
        !publicAccess.IsPublicRequest(HttpContext) && tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) && einsatz is not null;

    private IActionResult Zugang() => publicAccess.IsPublicRequest(HttpContext) ? NotFound() : Gone();
}
