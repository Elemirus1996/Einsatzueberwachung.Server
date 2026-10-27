using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Ueberwacht die Handys der Teams (Traccar Client oder Team-Seite im Browser) waehrend einer Suche – wie
/// <see cref="CollarTrackingRelayService"/> fuer die Halsbaender:
///  - Funkstille: Ein Team, dessen Handy in diesem Einsatz schon Positionen geschickt hat, sendet waehrend einer
///    laufenden Suche laenger als das eingestellte Zeitfenster nichts (z. B. Traccar nach neuem Start vergessen).
///  - Akku: Handy meldet weniger als <see cref="AkkuWarnungProzent"/> Prozent.
/// </summary>
public sealed class TeamHandyUeberwachungService : BackgroundService
{
    public const int AkkuWarnungProzent = 20;
    private const int PruefSekunden = 15;

    private readonly IEinsatzService _einsatzService;
    private readonly ISettingsService _settingsService;
    private readonly IWarningService _warningService;
    private readonly ITimeService _timeService;
    private readonly ILogger<TeamHandyUeberwachungService> _logger;

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _letztesSignal = new();
    private readonly Dictionary<string, DateTime> _letzteWarnung = new();

    public TeamHandyUeberwachungService(
        IEinsatzService einsatzService,
        ISettingsService settingsService,
        IWarningService warningService,
        ITimeService timeService,
        ILogger<TeamHandyUeberwachungService> logger)
    {
        _einsatzService = einsatzService;
        _settingsService = settingsService;
        _warningService = warningService;
        _timeService = timeService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _einsatzService.TeamPhoneLocationChanged += OnPosition;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PruefSekunden));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var timeout = TimeSpan.FromSeconds(Math.Max(30, (await _settingsService.GetAppSettingsAsync()).PhoneNoSignalTimeoutSeconds));
                    PruefeFunkstille(_timeService.Now, timeout);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler bei der Funkstille-Ueberwachung der Team-Handys");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren
        }
        finally
        {
            _einsatzService.TeamPhoneLocationChanged -= OnPosition;
        }
    }

    private void OnPosition(string teamId, string teamName, TeamPhoneLocation location)
    {
        var now = _timeService.Now;
        lock (_lock) _letztesSignal[teamId] = now;

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team is not { IsRunning: true } || location.BatteryPercent is not int akku || akku >= AkkuWarnungProzent)
            return;
        if (!DarfWarnen(WarningRuleDefinition.Sources.PhoneLowBattery, teamId, now))
            return;

        _warningService.AddWarning(new WarningEntry
        {
            Title = "Handy-Akku niedrig",
            Message = $"Das Handy von Team \"{teamName}\" meldet nur noch {akku} % Akku.",
            Level = WarningLevel.Warning,
            TeamId = teamId,
            Source = WarningRuleDefinition.Sources.PhoneLowBattery,
            Timestamp = now
        });
    }

    /// <summary>Oeffentlich fuer Tests: prueft alle laufenden Teams mit bekanntem Handy.</summary>
    public void PruefeFunkstille(DateTime now, TimeSpan timeout)
    {
        foreach (var team in _einsatzService.Teams.Where(t => t.IsRunning))
        {
            DateTime zuletzt;
            lock (_lock)
            {
                // Nur Teams, deren Handy in diesem Einsatz schon gesendet hat – sonst nutzen sie kein Handy-GPS
                if (!_letztesSignal.TryGetValue(team.TeamId, out zuletzt)) continue;
            }

            // Frisch gestartete Suche: Zeitfenster ab Start, nicht ab der letzten Position der vorigen Suche
            var suchStart = now - team.ElapsedTime;
            if (suchStart > zuletzt) zuletzt = suchStart;
            if (now - zuletzt < timeout) continue;
            if (!DarfWarnen(WarningRuleDefinition.Sources.PhoneNoSignal, team.TeamId, now)) continue;

            var minuten = (int)Math.Round((now - zuletzt).TotalMinutes);
            _warningService.AddWarning(new WarningEntry
            {
                Title = "Handy sendet nicht",
                Message = $"Vom Handy von Team \"{team.TeamName}\" kam seit {Math.Max(1, minuten)} Min. keine Position – Traccar gestartet? Funkloch?",
                Level = WarningLevel.Warning,
                TeamId = team.TeamId,
                Source = WarningRuleDefinition.Sources.PhoneNoSignal,
                Timestamp = now
            });
        }
    }

    /// <summary>Fuer Tests und nach Neustart: Signal von aussen vermerken.</summary>
    public void VermerkeSignal(string teamId, DateTime zeitpunkt)
    {
        lock (_lock) _letztesSignal[teamId] = zeitpunkt;
    }

    private bool DarfWarnen(string source, string teamId, DateTime now)
    {
        var abstand = TimeSpan.FromSeconds(Math.Max(60, _warningService.GetRuleConfig(source).CooldownSeconds));
        var key = source + ":" + teamId;
        lock (_lock)
        {
            if (_letzteWarnung.TryGetValue(key, out var zuletzt) && now - zuletzt < abstand) return false;
            _letzteWarnung[key] = now;
            return true;
        }
    }
}
