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

    /// <summary>Person waehlen: neues Token mit Person, gleicher Ablauf und gleiche Anmeldung; das alte wird gesperrt</summary>
    [HttpPost("ich")]
    public async Task<IActionResult> Ich([FromBody] ElIchRequest? anfrage)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        var alt = Bearer();
        if (!tokens.TryPruefen(alt, DateTime.UtcNow, out var ablauf, out var anmeldung, out _) || einsatz is null) return Gone();
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

    /// <summary>Person und Rolle zum Token; ohne Person (aeltere Apps) Rolle Einsatzleiter wie bisher</summary>
    private async Task<ElIch> IchAsync(string? personId)
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
