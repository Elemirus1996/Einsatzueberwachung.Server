using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Native Einsatzleitung (#125, docs/APP_API.md „Rolle Einsatzleitung – native App“), Stufe 2: Anmeldung und Lage</summary>
public sealed partial class ElAppController
{
    public sealed record ElLoginRequest(string? Passwort, string? Geraet);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] ElLoginRequest? anfrage)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (leitung is null || sperre is null) return NotFound();

        var client = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";
        var jetzt = DateTime.UtcNow;
        // Gleicher Zaehler wie die Anmeldeseite im Browser – die App ist kein zweiter Weg zum Raten
        if (sperre.IsLocked(client, jetzt))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Zu viele Fehlversuche, bitte später erneut.", wiederholenNachSec = (int)LoginLockout.LockTime.TotalSeconds });
        if (!leitung.IsSetUp)
            return Conflict(new { error = "Das Staffel-Passwort ist noch nicht eingerichtet (Anmeldeseite im Browser)." });
        if (string.IsNullOrEmpty(anfrage?.Passwort) || !leitung.Verify(anfrage.Passwort))
        {
            sperre.Fail(client, jetzt);
            return Unauthorized(new { error = "Passwort falsch." });
        }
        sperre.Success(client);

        var ablaufUtc = jetzt + ElAppTokenService.Laufzeit;
        audit?.Log("Anmeldung Einsatzleitung (App)", Geraet(anfrage.Geraet));
        var staffel = einstellungen is null ? "" : (await einstellungen.GetStaffelSettingsAsync())?.StaffelName ?? "";
        return Ok(new
        {
            apiVersion = TeamMobileAppController.ApiVersion,
            rolle = "el",
            token = tokens.Ausstellen(ablaufUtc),
            expiresAt = ablaufUtc,
            server = new
            {
                kennung = kennung?.Kennung,
                staffel,
                logoVersion = logo is null ? null : (await logo.HolenAsync())?.Version
            }
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        var token = Bearer();
        if (!tokens.TryPruefen(token, DateTime.UtcNow, out var ablaufUtc)) return Gone();
        tokens.Abmelden(token!, ablaufUtc);
        audit?.Log("Abmeldung Einsatzleitung (App)", Geraet(null));
        return NoContent();
    }

    [HttpGet("lage")]
    public IActionResult GetLage()
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) || einsatz is null) return Gone();
        return Ok(ElLage.Erstellen(einsatz, halsbaender, positionen.Modus));
    }

    /// <summary>Name fuer das Audit-Log: VPN-Geraet, sonst Angabe der App, sonst „Einsatzleitung“</summary>
    private string Geraet(string? angabe)
    {
        var vpn = geraete.NameFuer(HttpContext.Connection.RemoteIpAddress);
        if (vpn is not null) return vpn;
        var eigen = (angabe ?? "").Trim();
        return eigen.Length is > 0 and <= 40 ? eigen : LeitungsGeraetNamen.Einsatzleitung;
    }
}

/// <summary>Aufbau von GET /lage der Einsatzleitung (testbar ohne Controller)</summary>
public static class ElLage
{
    public static object Erstellen(Einsatzueberwachung.Domain.Interfaces.IEinsatzService einsatzService,
        Einsatzueberwachung.Domain.Interfaces.ICollarTrackingService? halsbaender, OrtungsModus modus)
    {
        var e = einsatzService.CurrentEinsatz;
        var aktiv = ElPositionService.Laeuft(e);
        return new
        {
            mode = OrtungsSteuerung.AlsText(modus),
            serverZeit = DateTime.UtcNow,
            einsatz = new
            {
                aktiv,
                text = string.Join(" · ", new[] { e.Stichwort, e.Einsatzort }.Where(s => !string.IsNullOrWhiteSpace(s))),
                stichwort = e.Stichwort ?? "",
                ort = e.Einsatzort ?? "",
                szenario = e.Szenario.ToString(),
                uebung = !e.IstEinsatz,
                beginn = aktiv ? AppZeit.Utc(e.AlarmierungsZeit ?? e.EinsatzDatum) : (DateTime?)null,
                einsatzleiter = e.Einsatzleiter ?? "",
                fuehrungsassistent = e.Fuehrungsassistent ?? ""
            },
            teams = !aktiv ? [] : einsatzService.Teams.OrderBy(t => t.TeamName).Select(t => Team(t, e, einsatzService, halsbaender)).ToList(),
            wetter = (object?)null // folgt (Wetterdienst noch nicht angebunden)
        };
    }

    private static object Team(Team t, EinsatzData e, Einsatzueberwachung.Domain.Interfaces.IEinsatzService einsatzService,
        Einsatzueberwachung.Domain.Interfaces.ICollarTrackingService? halsbaender)
    {
        var status = AppLage.SucheStatus(t);
        var minuten = t.ElapsedTime.TotalMinutes;
        var gebiet = e.SearchAreas?.FirstOrDefault(a => a.Id == t.SearchAreaId);
        var hb = string.IsNullOrWhiteSpace(t.CollarId) || halsbaender is null ? null : halsbaender.GetLatestLocation(t.CollarId);
        einsatzService.PhoneLocations.TryGetValue(t.TeamId, out var handy);
        return new
        {
            teamId = t.TeamId,
            teamName = t.TeamName,
            dogName = t.DogName ?? "",
            hundefuehrerName = t.HundefuehrerName ?? "",
            helfer = t.HelferNames.Where(h => !string.IsNullOrWhiteSpace(h)).ToList(),
            suche = new { status, seit = status == "bereit" ? (DateTime?)null : AppZeit.Utc(t.StartTime), dauerSec = (long)t.ElapsedTime.TotalSeconds },
            warnstufe = t.SecondWarningMinutes > 0 && minuten >= t.SecondWarningMinutes ? 2
                : t.FirstWarningMinutes > 0 && minuten >= t.FirstWarningMinutes ? 1 : 0,
            warnung1Min = t.FirstWarningMinutes,
            warnung2Min = t.SecondWarningMinutes,
            suchgebiet = gebiet is null ? null : new { id = gebiet.Id, name = gebiet.Name, farbe = gebiet.Color },
            halsband = string.IsNullOrWhiteSpace(t.CollarId) ? null : new
            {
                id = t.CollarId,
                name = t.CollarName ?? t.CollarId,
                zeit = hb is null ? (DateTime?)null : AppZeit.Utc(hb.Timestamp),
                akku = hb?.BatteryLevel
            },
            handy = handy is null ? null : new { zeit = AppZeit.Utc(handy.Timestamp), akku = handy.BatteryPercent },
            pause = t.IsPausing && t.PauseStartTime is { } start
                ? new { erforderlichBis = AppZeit.Utc(start.AddMinutes(t.RequiredPauseMinutes)) }
                : null
        };
    }
}
