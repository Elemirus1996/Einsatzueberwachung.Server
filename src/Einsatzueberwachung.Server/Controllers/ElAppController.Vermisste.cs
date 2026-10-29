using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Native Einsatzleitung, Stufe 4a: Vermisste und Szenario (docs/APP_API.md Abschnitte 6 und 7)</summary>
public sealed partial class ElAppController
{
    [HttpGet("vermisste")]
    public IActionResult GetVermisste()
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) || einsatz is null) return Gone();
        var e = einsatz.CurrentEinsatz;
        return Ok(new
        {
            szenario = e.Szenario.ToString(),
            mehrereErlaubt = e.Szenario.AllowsMultipleVermisste(),
            profile = (e.Vermisste ?? []).Select(ElVermisste.AlsJson).ToList()
        });
    }

    /// <summary>Anlegen (ohne profilId) oder Aendern (mit profilId und version); nur mitgeschickte Felder werden uebernommen</summary>
    [HttpPost("vermisste")]
    public Task<IActionResult> VermissteSpeichern([FromBody] JsonObject? koerper) => Aktion(Text(koerper, "id"), async () =>
    {
        var profilId = Text(koerper, "profilId");
        VermisstenInfo profil;
        if (profilId is null)
        {
            profil = new VermisstenInfo();
        }
        else
        {
            if (Vermisste(profilId) is not { } aktuell) return NotFound(new { error = "Profil nicht gefunden." });
            if (Text(koerper, "version") is not { } version) return BadRequest(new { error = "version fehlt." });
            if (version != ElVermisste.Version(aktuell))
                return Conflict(new { error = "Das Profil wurde inzwischen geändert.", profil = ElVermisste.AlsJson(aktuell) });
            profil = ElVermisste.Kopie(aktuell);
        }

        if (ElVermisste.Uebernehmen(profil, koerper!) is { } fehler) return BadRequest(new { error = fehler });
        try { await einsatz!.UpsertVermisstenAsync(profil); }
        catch (InvalidOperationException ex) { return Konflikt(ex.Message); } // z. B. nur eine Person im Szenario
        audit?.Log(profilId is null ? "Vermisste Person angelegt (EL-App)" : "Vermisste Person geändert (EL-App)", Geraet(null));
        return Ok(new { doppelt = false, profilId = profil.Id, version = ElVermisste.Version(profil) });
    });

    [HttpPost("vermisste/{profilId}/entfernen")]
    public Task<IActionResult> VermissteEntfernen(string profilId, [FromBody] ElAktion? aktion) => Aktion(aktion, async () =>
    {
        if (Vermisste(profilId) is not { } profil) return NotFound(new { error = "Profil nicht gefunden." });
        await einsatz!.RemoveVermisstenAsync(profil.Id);
        audit?.Log("Vermisste Person entfernt (EL-App)", Geraet(null));
        return Ok(new { doppelt = false });
    });

    public sealed record ElChecklistenPunkt(string? Id, string? PunktId, bool? Erledigt = null, string? Wert = null);

    [HttpPost("vermisste/{profilId}/checkliste")]
    public Task<IActionResult> VermissteCheckliste(string profilId, [FromBody] ElChecklistenPunkt? punkt) => Aktion(punkt?.Id, async () =>
    {
        if (Vermisste(profilId) is not { } aktuell) return NotFound(new { error = "Profil nicht gefunden." });
        var definition = aktuell.Checkliste?.Items.FirstOrDefault(i => i.Id.ToString() == punkt!.PunktId);
        if (definition is null) return NotFound(new { error = "Checklistenpunkt nicht gefunden." });
        string wert;
        if (definition.Type == ChecklistItemType.Bool)
        {
            if (punkt!.Erledigt is not { } erledigt) return BadRequest(new { error = "erledigt fehlt." });
            wert = erledigt ? "true" : "false";
        }
        else
        {
            wert = (punkt!.Wert ?? "").Trim();
            if (definition.Type == ChecklistItemType.Choice && wert.Length > 0 && !definition.Choices.Contains(wert))
                return BadRequest(new { error = "Unbekannte Auswahl.", auswahl = definition.Choices });
        }

        // Nur dieser Punkt – ohne version, damit zwei Geraete gleichzeitig abhaken koennen
        var profil = ElVermisste.Kopie(aktuell);
        profil.Checkliste!.Values[definition.Id.ToString()] = wert;
        await einsatz!.UpsertVermisstenAsync(profil);
        audit?.Log("Checkliste Vermisste (EL-App)", $"{Geraet(null)}: {definition.Label}");
        return Ok(new { doppelt = false, version = ElVermisste.Version(profil) });
    });

    public sealed record ElSzenario(string? Id, string? Szenario);

    [HttpPost("szenario")]
    public Task<IActionResult> SzenarioSetzen([FromBody] ElSzenario? anfrage) => Aktion(anfrage?.Id, async () =>
    {
        if (!Enum.TryParse<EinsatzSzenarioType>(anfrage!.Szenario, ignoreCase: false, out var szenario)
            || !Enum.IsDefined(szenario) || int.TryParse(anfrage.Szenario, out _))
            return BadRequest(new { error = "Unbekanntes Szenario.", szenarien = Enum.GetNames<EinsatzSzenarioType>() });
        await einsatz!.UpdateSzenarioAsync(szenario);
        audit?.Log("Szenario geändert (EL-App)", $"{Geraet(null)}: {szenario}");
        return Ok(new { doppelt = false, szenario = szenario.ToString() });
    });

    private VermisstenInfo? Vermisste(string profilId) =>
        einsatz!.CurrentEinsatz.Vermisste?.FirstOrDefault(v => v.Id.ToString() == profilId);

    private static string? Text(JsonObject? koerper, string feld) =>
        koerper?[feld] is JsonValue wert && wert.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}

/// <summary>Vermisstenprofil fuer die App: Felder wie VermisstenInfo (camelCase, Auswahlen als Text), dazu version und Checkliste</summary>
public static class ElVermisste
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Felder, die die App setzen darf: Texte, Ja/Nein und Auswahlen – nicht Id, Checkliste, Zeitstempel</summary>
    private static readonly Dictionary<string, PropertyInfo> Felder = typeof(VermisstenInfo).GetProperties()
        .Where(p => p.CanWrite && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool) || p.PropertyType.IsEnum))
        .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.Ordinal);

    private static readonly HashSet<string> Steuerfelder = ["id", "profilId", "version"];

    public const int MaxTextLaenge = 2000;

    public static VermisstenInfo Kopie(VermisstenInfo v) => JsonSerializer.Deserialize<VermisstenInfo>(JsonSerializer.Serialize(v, Json), Json)!;

    /// <summary>Aendert sich bei jeder Speicherung (auch durch /el), weil der Zeitstempel mit eingeht</summary>
    public static string Version(VermisstenInfo v) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(v, Json))))[..16].ToLowerInvariant();

    /// <summary>Uebernimmt die mitgeschickten Felder; Fehlertext bei unbekanntem Feld oder falschem Wert</summary>
    public static string? Uebernehmen(VermisstenInfo profil, JsonObject koerper)
    {
        foreach (var (name, wert) in koerper)
        {
            if (Steuerfelder.Contains(name)) continue;
            if (!Felder.TryGetValue(name, out var feld)) return $"Unbekanntes Feld „{name}“.";
            try
            {
                var neu = wert is null ? null : wert.Deserialize(feld.PropertyType, Json);
                if (feld.PropertyType == typeof(string))
                {
                    var text = ((string?)neu ?? "").Trim();
                    if (text.Length > MaxTextLaenge) return $"„{name}“ ist zu lang (höchstens {MaxTextLaenge} Zeichen).";
                    feld.SetValue(profil, text);
                }
                else
                {
                    if (neu is null || (feld.PropertyType.IsEnum && !Enum.IsDefined(feld.PropertyType, neu)))
                        return $"Ungültiger Wert für „{name}“.";
                    feld.SetValue(profil, neu);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return $"Ungültiger Wert für „{name}“.";
            }
        }
        return null;
    }

    public static JsonObject AlsJson(VermisstenInfo v)
    {
        var json = new JsonObject
        {
            ["profilId"] = v.Id.ToString(),
            ["version"] = Version(v),
            ["vollerName"] = v.VollerName,
            ["zuletztAktualisiert"] = v.ZuletztAktualisiert is { } zeit ? AppZeit.Utc(zeit) : null
        };
        foreach (var (name, feld) in Felder)
            json[name] = JsonSerializer.SerializeToNode(feld.GetValue(v), feld.PropertyType, Json);
        json["checkliste"] = v.Checkliste is not { } liste ? null : new JsonObject
        {
            ["name"] = liste.TemplateName,
            ["punkte"] = new JsonArray(liste.Items.Select(i =>
            {
                var wert = liste.Values.TryGetValue(i.Id.ToString(), out var w) ? w : null;
                return (JsonNode)new JsonObject
                {
                    ["id"] = i.Id.ToString(),
                    ["text"] = i.Label,
                    ["typ"] = i.Type switch { ChecklistItemType.Bool => "jaNein", ChecklistItemType.Choice => "auswahl", _ => "text" },
                    ["pflicht"] = i.Required,
                    ["auswahl"] = new JsonArray(i.Choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                    ["wert"] = wert,
                    ["erledigt"] = i.Type == ChecklistItemType.Bool ? wert == "true" : !string.IsNullOrWhiteSpace(wert)
                };
            }).ToArray())
        };
        return json;
    }
}
