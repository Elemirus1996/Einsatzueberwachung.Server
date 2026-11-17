using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Wer bedient die App: Person aus den Stammdaten und Rolle je Einsatz (docs/APP_API.md, Einsatzleitung Abschnitt 4a)</summary>
public sealed partial class ElAppController
{
    public sealed record ElIch(string? PersonId, string? Name, string Rolle);

    public sealed record ElIchRequest(string? PersonId);

    [HttpGet("personen")]
    public async Task<IActionResult> GetPersonen()
    {
        if (!Lesen()) return Zugang();
        var personal = await AktivesPersonalAsync();
        return Ok(new
        {
            personen = personal.Select(p => new
            {
                personId = p.Id,
                name = p.FullName,
                einsatzleiter = p.Skills.HasFlag(Einsatzueberwachung.Domain.Models.Enums.PersonalSkills.Einsatzleiter),
                fuehrungsassistent = p.Skills.HasFlag(Einsatzueberwachung.Domain.Models.Enums.PersonalSkills.Fuehrungsassistent)
            })
        });
    }

    /// <summary>
    /// Person einmal waehlen: neues Token mit Person, gleicher Ablauf und gleiche Anmeldung; das alte wird gesperrt.
    /// Kein Wechsel (Entscheidung Paul 17.11.2026): Steht schon eine Person im Token oder am VPN-Geraet, 409.
    /// </summary>
    [HttpPost("ich")]
    public async Task<IActionResult> Ich([FromBody] ElIchRequest? anfrage)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        var alt = Bearer();
        if (!tokens.TryPruefen(alt, DateTime.UtcNow, out var ablauf, out var anmeldung, out var bisher) || einsatz is null) return Gone();
        if (!string.IsNullOrEmpty(bisher) || geraete.PersonFuer(HttpContext.Connection.RemoteIpAddress) is not null)
            return Conflict(new { error = "Die Person ist für diese Anmeldung schon festgelegt. Wechsel nur über Abmelden und neu Anmelden." });
        var person = (await AktivesPersonalAsync()).FirstOrDefault(p => p.Id == anfrage?.PersonId);
        if (person is null) return BadRequest(new { error = "Person nicht gefunden oder nicht aktiv." });

        var neu = tokens.Ausstellen(ablauf, anmeldung, person.Id);
        tokens.Abmelden(alt!, ablauf);
        audit?.Log("Person gewählt (EL-App)", $"{Geraet(null)}: {person.FullName}");
        var antwort = await AnmeldeAntwortAsync(neu, ablauf, anmeldung, mitAnmeldung: true);
        var ich = IchFuer(person);
        return Ok(new { anmeldung = antwort, ich = new { personId = ich.PersonId, name = ich.Name, rolle = ich.Rolle } });
    }

    private async Task<List<PersonalEntry>> AktivesPersonalAsync() =>
        stammdaten is null ? [] : Components.Pages.SelectionListOrdering
            .OrderPersonnelByFirstName((await stammdaten.GetPersonalListAsync()).Where(p => p.IsActive)).ToList();

    private ElIch IchFuer(PersonalEntry person) =>
        new(person.Id, person.FullName, ElRolle.Bestimmen(einsatz!.CurrentEinsatz, person.FullName, personGewaehlt: true));

    /// <summary>
    /// Person und Rolle: die feste Person des VPN-Geraets vor der im Token; ohne beide (aeltere Apps) Rolle
    /// Einsatzleiter wie bisher. Der Name geht ins Audit-Log (Geraet()).
    /// </summary>
    private async Task<ElIch> IchAsync(string? personId)
    {
        var ich = await IchOhneProtokollAsync(geraete.PersonFuer(HttpContext.Connection.RemoteIpAddress) ?? personId);
        if (ich.Name is not null) HttpContext.Items[PersonImProtokoll] = ich.Name;
        return ich;
    }

    private const string PersonImProtokoll = "el.person";

    private async Task<ElIch> IchOhneProtokollAsync(string? personId)
    {
        if (string.IsNullOrEmpty(personId) || einsatz is null)
            return new ElIch(null, null, ElRolle.Einsatzleiter);
        var person = stammdaten is null ? null : (await stammdaten.GetPersonalListAsync()).FirstOrDefault(p => p.Id == personId);
        // Inzwischen geloeschte Person: nur noch lesen
        return person is null ? new ElIch(personId, null, ElRolle.Lesen) : IchFuer(person);
    }

    /// <summary>Ortung je Rolle: nur der Einsatzleiter sendet; sonst pause (angemeldet bleiben, GPS aus)</summary>
    private static OrtungsModus ModusFuer(ElIch ich, OrtungsModus einsatzModus) =>
        ElRolle.SendetStandort(ich.Rolle) ? einsatzModus : OrtungsModus.Pause;
}
