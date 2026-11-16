using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Packliste der Staffel in den Antworten der App-Schnittstelle (docs/APP_API.md Team-Rolle Abschnitt 14)</summary>
public static class AppPacklisteAntwort
{
    /// <summary>Ganzes Objekt oder null, wenn die Staffel keine Packliste hinterlegt hat</summary>
    public static async Task<object?> GanzAsync(ISettingsService? einstellungen) =>
        einstellungen is null ? null : Ganz(await einstellungen.GetAppSettingsAsync());

    public static object? Ganz(AppSettings? s)
    {
        if (s is null || AppPackliste.Version(s) is not { } version) return null;
        return new
        {
            version,
            stand = s.AppPacklisteStandUtc is { } stand ? DateTime.SpecifyKind(stand, DateTimeKind.Utc) : (DateTime?)null,
            punkte = AppPackliste.Punkte(s.AppPacklisteText).Select(p => new { id = p.Id, text = p.Text, gruppe = p.Gruppe })
        };
    }

    /// <summary>Nur die Version fuer das Objekt „server“, damit die App eine neue Fassung bemerkt</summary>
    public static async Task<string?> VersionAsync(ISettingsService? einstellungen) =>
        einstellungen is null ? null : AppPackliste.Version(await einstellungen.GetAppSettingsAsync());
}
