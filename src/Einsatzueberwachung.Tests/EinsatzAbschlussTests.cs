using System.Reflection;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

public class EinsatzAbschlussTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 26, 18, 30, 0);

    [Fact]
    public void Validate_VerlangtErgebnisUndPruefung()
    {
        Assert.NotNull(EinsatzAbschlussService.Validate("", true));
        Assert.NotNull(EinsatzAbschlussService.Validate("Uebung abgeschlossen", false));
        Assert.Null(EinsatzAbschlussService.Validate("Uebung abgeschlossen", true));
    }

    [Fact]
    public void ResolveEndTime_NutztGewaehlteUhrzeitAmHeutigenTag()
    {
        Assert.Equal(new DateTime(2026, 10, 26, 17, 45, 0), EinsatzAbschlussService.ResolveEndTime(new TimeOnly(17, 45), Jetzt));
        Assert.Equal(Jetzt, EinsatzAbschlussService.ResolveEndTime(null, Jetzt));
    }

    [Fact]
    public void ResolveEndTime_KurzNachMitternachtGewaehltesEndeVonGestern()
    {
        var kurzNachMitternacht = new DateTime(2026, 10, 27, 0, 20, 0);

        Assert.Equal(new DateTime(2026, 10, 26, 23, 50, 0), EinsatzAbschlussService.ResolveEndTime(new TimeOnly(23, 50), kurzNachMitternacht));
        Assert.Equal(new DateTime(2026, 10, 27, 0, 10, 0), EinsatzAbschlussService.ResolveEndTime(new TimeOnly(0, 10), kurzNachMitternacht));
    }

    [Fact]
    public void CollarOptions_ZuletztBenutztesHalsbandZuerst()
    {
        var frei = new[] { new Collar("C-1", "Alpha"), new Collar("C-2", "Bravo"), new Collar("C-3", "Charlie") };
        var tracks = new[]
        {
            new TeamTrackSnapshot { TeamId = "t1", CollarId = "C-1", TrackType = TrackType.CollarTrack, CapturedAt = Jetzt.AddHours(-2) },
            new TeamTrackSnapshot { TeamId = "t1", CollarId = "C-3", TrackType = TrackType.CollarTrack, CapturedAt = Jetzt.AddHours(-1) },
            new TeamTrackSnapshot { TeamId = "t2", CollarId = "C-2", TrackType = TrackType.CollarTrack, CapturedAt = Jetzt }
        };

        Assert.Equal("C-3", TeamSucheService.LastCollarId(tracks, "t1"));
        Assert.Equal(["C-3", "C-1", "C-2"], TeamSucheService.OrderCollarOptions(frei, tracks, "t1").Select(c => c.Id));
        Assert.Null(TeamSucheService.LastCollarId(tracks, "t-ohne"));
    }

    [Fact]
    public async Task TeamSuche_NachStoppFragtNeueSucheNachHalsband()
    {
        var einsatz = new EinsatzService();
        var collars = new CollarTrackingService(einsatz);
        var suche = new TeamSucheService(einsatz, collars, new FesteZeit(Jetzt));
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        await collars.AssignCollarToTeamAsync("C-1", team.TeamId);

        Assert.False(suche.NeedsCollarChoice(team.TeamId)); // Halsband zugewiesen
        await suche.StartAsync(team.TeamId);
        await collars.ReceiveLocationAsync("C-1", "Halsband 1", 49.0, 8.0);
        await suche.StopAsync(team.TeamId);

        Assert.True(suche.NeedsCollarChoice(team.TeamId)); // Stopp hat das Halsband freigegeben
        Assert.Equal("C-1", suche.CollarOptions(team.TeamId)[0].Id);

        await suche.StartWithCollarAsync(team.TeamId, "C-1");
        Assert.Equal("C-1", team.CollarId);
        Assert.True(team.IsRunning);
    }

    [Fact]
    public void PreselectVorOrt_WaehltEingeteiltePersonenUndHunde()
    {
        var personal = new List<PersonalEntry>
        {
            new() { Id = "p1", Vorname = "Anna", Nachname = "Berg" },
            new() { Id = "p2", Vorname = "Ben", Nachname = "Coll" },
            new() { Id = "p3", Vorname = "Nicht", Nachname = "Dabei" }
        };
        var hunde = new List<DogEntry> { new() { Id = "d1", Name = "Rex" }, new() { Id = "d2", Name = "Luna" } };
        var team = new Team { TeamName = "Rex", HundefuehrerName = "anna berg ", DogName = "Rex" };
        team.HelferNames.Add("Ben Coll");

        var (personalIds, hundeIds) = EinsatzAbschlussService.PreselectVorOrt([team], personal, hunde);

        Assert.Equal(["p1", "p2"], personalIds.OrderBy(x => x));
        Assert.Equal(["d1"], hundeIds);
    }

    [Fact]
    public async Task Abschliessen_ArchiviertSzenarioSuchenUndVermissteUndSetztZurueck()
    {
        var einsatz = new EinsatzService();
        einsatz.CurrentEinsatz.Einsatzort = "Waldweg";
        einsatz.CurrentEinsatz.EinsatzDatum = Jetzt.AddHours(-3);
        einsatz.CurrentEinsatz.Szenario = EinsatzSzenarioType.Flaeche;
        einsatz.CurrentEinsatz.Vermisste.Add(new VermisstenInfo());
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        await einsatz.CreateCompletedSearchAsync(team.TeamId, Jetzt.AddHours(-2), Jetzt.AddHours(-1));

        var archiv = Recorder<IArchivService>.Create((method, args) =>
            method.Name == nameof(IArchivService.ArchiveEinsatzAsync)
                ? Task.FromResult(ArchivedEinsatz.FromEinsatzData((EinsatzData)args![0]!, (string)args[1]!, (string)args[2]!, Jetzt))
                : null);
        var pdf = Recorder<IPdfExportService>.Create((_, _) => Task.FromResult(new PdfExportResult { Success = true }));
        var service = new EinsatzAbschlussService(einsatz, archiv.Proxy, pdf.Proxy, new FesteZeit(Jetzt));

        var ergebnis = await service.AbschliessenAsync(new EinsatzAbschlussAnfrage(
            Jetzt.AddHours(-3), "Suche erfolglos abgeschlossen", "  ohne Befund ", new TimeOnly(18, 0), false, ["Anna Berg"], ["Rex"]));

        Assert.Equal(EinsatzSzenarioType.Flaeche, ergebnis.Archiviert.Szenario);
        Assert.Single(ergebnis.Archiviert.Vermisste);
        Assert.Single(ergebnis.Archiviert.CompletedSearches!);
        Assert.Equal("ohne Befund", ergebnis.Archiviert.Bemerkungen);
        Assert.Equal(new DateTime(2026, 10, 26, 18, 0, 0), ergebnis.Archiviert.EinsatzEnde);
        Assert.True(ergebnis.Pdf.Success);
        Assert.Empty(einsatz.Teams);
        Assert.Equal(string.Empty, einsatz.CurrentEinsatz.Einsatzort ?? string.Empty);
    }

    [Fact]
    public async Task Abschliessen_ZweiterLeitungsplatzBekommtMeldungStattZweitemArchiveintrag()
    {
        var einsatz = new EinsatzService();
        einsatz.CurrentEinsatz.Einsatzort = "Waldweg";
        einsatz.CurrentEinsatz.EinsatzDatum = Jetzt.AddHours(-3);

        var archiv = Recorder<IArchivService>.Create((method, args) =>
            Task.FromResult(ArchivedEinsatz.FromEinsatzData((EinsatzData)args![0]!, "", "", Jetzt)));
        var pdf = Recorder<IPdfExportService>.Create((_, _) => Task.FromResult(new PdfExportResult { Success = true }));
        var service = new EinsatzAbschlussService(einsatz, archiv.Proxy, pdf.Proxy, new FesteZeit(Jetzt));
        var anfrage = new EinsatzAbschlussAnfrage(Jetzt.AddHours(-3), "Uebung abgeschlossen", "", null, false, [], []);

        await service.AbschliessenAsync(anfrage);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AbschliessenAsync(anfrage));

        Assert.Equal(EinsatzAbschlussService.BereitsBeendetMeldung, ex.Message);
        Assert.Equal(1, archiv.Calls.Count(c => c == nameof(IArchivService.ArchiveEinsatzAsync)));
    }

    [Fact]
    public void ChooseColor_ErsteSucheGrundfarbe_FolgesuchenAndereFarben()
    {
        var erste = TeamSucheService.ChooseColor("#123456", []);
        var zweite = TeamSucheService.ChooseColor("#123456", [erste]);
        var dritte = TeamSucheService.ChooseColor("#123456", [erste, zweite]);

        Assert.Equal("#123456", erste);
        Assert.NotEqual(erste, zweite);
        Assert.NotEqual(zweite, dritte);
        Assert.NotEqual(erste, dritte);
    }

    [Fact]
    public async Task TeamSuche_StoppSpeichertTrack_NeueSucheMitHalsbandBekommtAndereFarbe()
    {
        var einsatz = new EinsatzService();
        var collars = new CollarTrackingService(einsatz);
        var suche = new TeamSucheService(einsatz, collars, new FesteZeit(Jetzt));
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });

        await LaufeSucheAsync(team, 49.00);
        await LaufeSucheAsync(team, 49.10);

        var tracks = einsatz.CurrentEinsatz.TrackSnapshots.Where(t => t.TrackType == TrackType.CollarTrack).ToList();
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, t => Assert.Equal(2, t.Points.Count));
        Assert.Equal(TeamSucheService.StandardFarbeHalsband, tracks[0].Color);
        Assert.NotEqual(tracks[0].Color, tracks[1].Color);
        Assert.Equal(2, einsatz.CurrentEinsatz.CompletedSearches.Count);
        Assert.Null(team.CollarId); // Halsband nach dem Stopp frei
        Assert.Empty(collars.GetLocationHistory("C-1")); // Live-Track von der Karte genommen

        async Task LaufeSucheAsync(Team t, double lat)
        {
            await collars.AssignCollarToTeamAsync("C-1", t.TeamId);
            await collars.ReceiveLocationAsync("C-1", "Halsband 1", 48.0, 8.0); // vor dem Start – gehoert nicht zur Suche
            await suche.StartAsync(t.TeamId);
            await collars.ReceiveLocationAsync("C-1", "Halsband 1", lat, 8.0);
            await collars.ReceiveLocationAsync("C-1", "Halsband 1", lat + 0.001, 8.0);
            await suche.StopAsync(t.TeamId);
        }
    }

    [Fact]
    public async Task TeamSuche_PauseUnterbrichtNur_TrackLaeuftNachDemWeitermachenWeiter()
    {
        var einsatz = new EinsatzService();
        var collars = new CollarTrackingService(einsatz);
        var suche = new TeamSucheService(einsatz, collars, new FesteZeit(Jetzt));
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        await collars.AssignCollarToTeamAsync("C-1", team.TeamId);

        await suche.StartAsync(team.TeamId);
        await collars.ReceiveLocationAsync("C-1", "Halsband 1", 49.00, 8.0);
        await einsatz.PauseTeamTimerAsync(team.TeamId);
        await suche.StartAsync(team.TeamId); // weiter nach der Pause
        await collars.ReceiveLocationAsync("C-1", "Halsband 1", 49.01, 8.0);
        await suche.StopAsync(team.TeamId);

        var track = Assert.Single(einsatz.CurrentEinsatz.TrackSnapshots);
        Assert.Equal(2, track.Points.Count);
        Assert.Single(einsatz.CurrentEinsatz.CompletedSearches);
    }

    private sealed class FesteZeit(DateTime now) : ITimeService
    {
        public DateTime Now => now;
        public void Refresh() { }
    }

    /// <summary>Kleiner Ersatz fuer ein Mock-Framework: zeichnet Aufrufe auf und liefert die vorgegebenen Antworten.</summary>
    public class Recorder<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[]?, object?> _handler = (_, _) => null;
        public List<string> Calls { get; } = [];
        public T Proxy => (T)(object)this;

        public static Recorder<T> Create(Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create<T, Recorder<T>>();
            var recorder = (Recorder<T>)(object)proxy;
            recorder._handler = handler;
            return recorder;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return _handler(targetMethod, args);
        }
    }
}
