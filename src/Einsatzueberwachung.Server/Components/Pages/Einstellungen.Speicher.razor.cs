using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

// Einstellungen → Wartung → Speicher & Server-Backups
public partial class Einstellungen
{
    [Inject] private SpeicherVerwaltungService SpeicherVerwaltung { get; set; } = default!;

    private SpeicherUebersicht? _speicher;
    private bool _speicherLaedt;
    private bool _speicherVersucht;
    private bool _speicherLoeschtBusy;
    private readonly HashSet<(SpeicherBereich Bereich, string Name)> _speicherAuswahl = [];
    private string? _speicherMeldung;
    private bool _speicherFehler;

    private static readonly (SpeicherBereich Bereich, string Titel, string Hilfe)[] SpeicherBereiche =
    [
        (SpeicherBereich.ServerBackups, "Server-Backups", "Nächtliche Sicherungen des Servers. Das neueste Backup ist geschützt."),
        (SpeicherBereich.UpdatePakete, "Update-Reste (Installation)", "Entpackte Updates und alte Rollback-Stände. Der letzte Rollback-Stand ist geschützt."),
        (SpeicherBereich.UpdateDownloads, "Heruntergeladene Updates", "Pakete im Datenverzeichnis – werden sonst bei jedem Backup mitgesichert."),
    ];

    /// <summary>Auflistung erst laden, wenn der Wartungs-Tab offen ist (Ordnergroessen zu berechnen dauert)</summary>
    private async Task LadeSpeicherBeiBedarfAsync()
    {
        if (_activeTab == SettingsTab.Wartung && !_speicherVersucht && !_speicherLaedt)
        {
            _speicherVersucht = true;
            await LadeSpeicherAsync();
            StateHasChanged();
        }
    }

    private async Task LadeSpeicherAsync()
    {
        _speicherLaedt = true;
        try
        {
            _speicher = await SpeicherVerwaltung.AuflistenAsync();
            _speicherAuswahl.RemoveWhere(a => !_speicher.Eintraege.Any(e => (e.Bereich, e.Name) == a && !e.Geschuetzt));
        }
        catch (Exception ex)
        {
            _speicherMeldung = $"Speicher konnte nicht gelesen werden: {ex.Message}";
            _speicherFehler = true;
        }
        finally
        {
            _speicherLaedt = false;
        }
    }

    private async Task AktualisiereSpeicherAsync()
    {
        _speicherMeldung = null;
        await LadeSpeicherAsync();
    }

    private IEnumerable<SpeicherEintrag> SpeicherEintraege(SpeicherBereich bereich) =>
        _speicher?.Eintraege.Where(e => e.Bereich == bereich).OrderByDescending(e => e.GeaendertUtc) ?? [];

    private bool SpeicherAusgewaehlt(SpeicherEintrag e) => _speicherAuswahl.Contains((e.Bereich, e.Name));

    private void SpeicherUmschalten(SpeicherEintrag e, bool an)
    {
        if (e.Geschuetzt) return;
        if (an) _speicherAuswahl.Add((e.Bereich, e.Name));
        else _speicherAuswahl.Remove((e.Bereich, e.Name));
    }

    /// <summary>Alle loeschbaren Eintraege eines Bereichs an- oder abwaehlen</summary>
    private void SpeicherAlleUmschalten(SpeicherBereich bereich)
    {
        var loeschbar = SpeicherEintraege(bereich).Where(e => !e.Geschuetzt).Select(e => (e.Bereich, e.Name)).ToList();
        if (loeschbar.All(_speicherAuswahl.Contains)) loeschbar.ForEach(a => _speicherAuswahl.Remove(a));
        else loeschbar.ForEach(a => _speicherAuswahl.Add(a));
    }

    private long SpeicherAuswahlBytes =>
        _speicher?.Eintraege.Where(e => _speicherAuswahl.Contains((e.Bereich, e.Name))).Sum(e => e.Bytes) ?? 0;

    private string SpeicherBelegungKlasse => _speicher?.BelegtProzent switch
    {
        >= SpeicherVerwaltungService.KritischProzent => "danger",
        >= SpeicherVerwaltungService.WarnProzent => "warning",
        null => "muted",
        _ => "ok",
    };

    private string SpeicherBalkenKlasse => SpeicherBelegungKlasse switch
    {
        "danger" => "bg-danger",
        "warning" => "bg-warning",
        _ => "bg-success",
    };

    private async Task LoescheSpeicherAuswahlAsync()
    {
        if (_speicherAuswahl.Count == 0 || _speicherLoeschtBusy) return;
        if (_updateStatus.IsInstalling)
        {
            _speicherMeldung = "Während ein Update installiert wird, kann nichts gelöscht werden.";
            _speicherFehler = true;
            return;
        }

        var anzahl = _speicherAuswahl.Count;
        var frage = $"{anzahl} {(anzahl == 1 ? "Eintrag" : "Einträge")} ({SpeicherVerwaltungService.FormatGroesse(SpeicherAuswahlBytes)}) endgültig löschen? Das lässt sich nicht rückgängig machen.";
        if (!await JS.InvokeAsync<bool>("confirm", frage)) return;

        _speicherLoeschtBusy = true;
        try
        {
            var ergebnis = await SpeicherVerwaltung.LoeschenAsync(_speicherAuswahl.ToList());
            _speicherAuswahl.Clear();
            _speicherFehler = ergebnis.Fehler.Count > 0;
            _speicherMeldung = $"{ergebnis.Geloescht} gelöscht, {SpeicherVerwaltungService.FormatGroesse(ergebnis.FreigegebenBytes)} frei geworden."
                + (ergebnis.Fehler.Count > 0 ? " Nicht gelöscht: " + string.Join(" ", ergebnis.Fehler) : string.Empty);
            await LadeSpeicherAsync();
        }
        catch (Exception ex)
        {
            _speicherMeldung = $"Löschen fehlgeschlagen: {ex.Message}";
            _speicherFehler = true;
        }
        finally
        {
            _speicherLoeschtBusy = false;
        }
    }
}
