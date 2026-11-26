using System.Threading.Channels;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Services;

public sealed class DiveraResourceSyncService : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, string> EmptyResponses =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IEinsatzService _einsatzService;
    private readonly IDiveraService _diveraService;
    private readonly IMasterDataService _masterDataService;
    private readonly DiveraTestSimulationService? _simulationService;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<DiveraResourceSyncService> _logger;
    private readonly HashSet<int> _unmappedResponseMemberIds = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly object _stateLock = new();
    private Dictionary<string, string> _positiveResponsesByPersonId = new(StringComparer.Ordinal);
    private int? _loggedUnmappedAlarmId;
    private readonly Channel<bool> _syncSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public event Action? ResponsesChanged;

    public DiveraResourceSyncService(
        IEinsatzService einsatzService,
        IDiveraService diveraService,
        IMasterDataService masterDataService,
        ILogger<DiveraResourceSyncService> logger,
        DiveraTestSimulationService? simulationService = null,
        ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;
        _einsatzService = einsatzService;
        _diveraService = diveraService;
        _masterDataService = masterDataService;
        _logger = logger;
        _simulationService = simulationService;
    }

    public IReadOnlyDictionary<string, string> GetPositiveResponsesByPersonId()
    {
        if (!HasActiveLinkedEinsatz(_einsatzService.CurrentEinsatz))
            return EmptyResponses;

        lock (_stateLock)
        {
            return _positiveResponsesByPersonId.Count == 0
                ? EmptyResponses
                : new Dictionary<string, string>(_positiveResponsesByPersonId, StringComparer.Ordinal);
        }
    }

    public void RequestSync() => _syncSignals.Writer.TryWrite(true);

    public async Task<int> CheckInPendingPositiveResponsesAsync(CancellationToken cancellationToken = default)
    {
        var einsatz = _einsatzService.CurrentEinsatz;
        if (!HasActiveLinkedEinsatz(einsatz))
            return 0;

        var responses = GetPositiveResponsesByPersonId();
        if (responses.Count == 0)
        {
            await SyncActiveAlarmResponsesAsync(cancellationToken);
            responses = GetPositiveResponsesByPersonId();
        }

        if (responses.Count == 0)
            return 0;

        var checkedInPersonIds = _einsatzService.GetEinsatzResources()
            .Where(resource => resource.EntityType == ResourceEntityType.Person)
            .Select(resource => resource.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        var checkedInCount = 0;
        foreach (var (personId, statusText) in responses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!checkedInPersonIds.Add(personId))
                continue;

            await _einsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = personId,
                Status = ResourceStatus.AufAnfahrt,
                DiveraRueckmeldung = statusText
            });
            checkedInCount++;
        }

        if (checkedInCount > 0)
        {
            _logger.LogInformation(
                "{Count} Person(en) mit positiver DIVERA-Rückmeldung gesammelt als auf Anfahrt eingecheckt (Alarm {AlarmId}).",
                checkedInCount,
                einsatz.DiveraAlarmId);
        }

        return checkedInCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _einsatzService.EinsatzChanged += OnEinsatzChanged;
        _diveraService.DataChanged += OnDiveraDataChanged;
        if (_settingsService is not null)
            _settingsService.AppSettingsChanged += OnSettingsChanged;
        _syncSignals.Writer.TryWrite(true);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncActiveAlarmResponsesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Abrufen von DIVERA-Rückmeldungen für die Ressourcenverwaltung.");
                }

                await WaitForNextSyncAsync(stoppingToken);
            }
        }
        finally
        {
            _einsatzService.EinsatzChanged -= OnEinsatzChanged;
            _diveraService.DataChanged -= OnDiveraDataChanged;
            if (_settingsService is not null)
                _settingsService.AppSettingsChanged -= OnSettingsChanged;
        }
    }

    public async Task SyncActiveAlarmResponsesAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var einsatz = _einsatzService.CurrentEinsatz;
            var ressourcenAktiv = _settingsService is null
                || (await _settingsService.GetAppSettingsAsync()).RessourcenverwaltungAktiv;
            if (!ressourcenAktiv
                || !HasActiveLinkedEinsatz(einsatz)
                || einsatz.DiveraAlarmId is not int alarmId
                || (alarmId == DiveraTestSimulationService.SimulatedAlarmId && _simulationService?.IsActive != true))
            {
                _unmappedResponseMemberIds.Clear();
                _loggedUnmappedAlarmId = null;
                UpdatePositiveResponses(new Dictionary<string, string>(StringComparer.Ordinal));
                return;
            }

            var alarm = (await _diveraService.GetActiveAlarmsAsync())
                .FirstOrDefault(candidate => candidate.Id == alarmId && !candidate.Closed);
            if (alarm is null)
            {
                UpdatePositiveResponses(new Dictionary<string, string>(StringComparer.Ordinal));
                return;
            }

            if (_loggedUnmappedAlarmId != alarmId)
            {
                _unmappedResponseMemberIds.Clear();
                _loggedUnmappedAlarmId = alarmId;
            }

            var positiveResponses = alarm.UcrDetails
                .Where(response => response.IsPositive)
                .GroupBy(response => response.MemberId)
                .Select(group => group.First())
                .ToList();
            if (positiveResponses.Count == 0)
            {
                UpdatePositiveResponses(new Dictionary<string, string>(StringComparer.Ordinal));
                return;
            }

            var personal = (await _masterDataService.GetPersonalListAsync())
                .Where(person => person.IsActive && person.DiveraUserId.HasValue)
                .GroupBy(person => person.DiveraUserId!.Value)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            if (_simulationService is { IsActive: true } && alarmId == DiveraTestSimulationService.SimulatedAlarmId)
            {
                foreach (var (simulatedMemberId, simulatedPerson) in _simulationService.GetSimulatedPersonnelByMemberId())
                {
                    personal.TryAdd(simulatedMemberId, simulatedPerson);
                }
            }
            var unmatchedResponses = positiveResponses
                .Where(response => !personal.ContainsKey(response.MemberId))
                .Select(response => response.MemberId)
                .ToList();
            foreach (var response in positiveResponses.Where(response => personal.ContainsKey(response.MemberId)))
                _unmappedResponseMemberIds.Remove(response.MemberId);

            var newUnmatchedResponseCount = unmatchedResponses.Count(_unmappedResponseMemberIds.Add);
            if (newUnmatchedResponseCount > 0)
            {
                _logger.LogWarning(
                    "{Count} positive DIVERA-Rückmeldung(en) für Alarm {AlarmId} haben keine eindeutige aktive Personenzuordnung.",
                    newUnmatchedResponseCount,
                    alarmId);
            }

            var nextResponses = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var response in positiveResponses)
            {
                if (personal.TryGetValue(response.MemberId, out var person))
                    nextResponses[person.Id] = response.StatusText;
            }

            UpdatePositiveResponses(nextResponses);

            var existingEnRouteByPersonId = _einsatzService.GetEinsatzResources()
                .Where(resource => resource.EntityType == ResourceEntityType.Person && resource.Status == ResourceStatus.AufAnfahrt)
                .ToDictionary(resource => resource.EntityId, StringComparer.Ordinal);

            foreach (var (personId, statusText) in nextResponses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!existingEnRouteByPersonId.TryGetValue(personId, out var existingResource)
                    || string.Equals(existingResource.DiveraRueckmeldung, statusText, StringComparison.Ordinal))
                    continue;

                await _einsatzService.CheckInResourceAsync(new EinsatzResource
                {
                    EntityType = ResourceEntityType.Person,
                    EntityId = personId,
                    Status = ResourceStatus.AufAnfahrt,
                    DiveraRueckmeldung = statusText
                });
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void UpdatePositiveResponses(Dictionary<string, string> nextResponses)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (!AreSameResponses(_positiveResponsesByPersonId, nextResponses))
            {
                _positiveResponsesByPersonId = nextResponses;
                changed = true;
            }
        }

        if (changed)
            ResponsesChanged?.Invoke();
    }

    private static bool AreSameResponses(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue)
                || !string.Equals(value, otherValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool HasActiveLinkedEinsatz(EinsatzData einsatz) =>
        einsatz.EinsatzEnde is null
        && !string.IsNullOrWhiteSpace(einsatz.Einsatzort)
        && einsatz.DiveraAlarmId.HasValue;

    private async Task WaitForNextSyncAsync(CancellationToken cancellationToken)
    {
        var hasLinkedActiveEinsatz = HasActiveLinkedEinsatz(_einsatzService.CurrentEinsatz);
        var intervalSeconds = hasLinkedActiveEinsatz
            ? _diveraService.PollIntervalActiveSeconds
            : _diveraService.PollIntervalIdleSeconds;
        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 5, 600));

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _syncSignals.Reader.ReadAsync(waitCts.Token).AsTask();
        var delayTask = Task.Delay(interval, waitCts.Token);
        var completedTask = await Task.WhenAny(signalTask, delayTask);
        waitCts.Cancel();

        if (completedTask == signalTask)
        {
            await signalTask;
            while (_syncSignals.Reader.TryRead(out _))
            {
            }
        }
        else
        {
            await delayTask;
            try
            {
                await signalTask;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private void OnEinsatzChanged() => _syncSignals.Writer.TryWrite(true);
    private void OnDiveraDataChanged() => _syncSignals.Writer.TryWrite(true);
    private void OnSettingsChanged() => _syncSignals.Writer.TryWrite(true);
}
