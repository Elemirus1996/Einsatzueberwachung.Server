using System.Threading.Channels;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Services;

public sealed class DiveraResourceSyncService : BackgroundService
{
    private readonly IEinsatzService _einsatzService;
    private readonly IDiveraService _diveraService;
    private readonly IMasterDataService _masterDataService;
    private readonly ILogger<DiveraResourceSyncService> _logger;
    private readonly HashSet<int> _unmappedResponseMemberIds = new();
    private int? _loggedUnmappedAlarmId;
    private readonly Channel<bool> _syncSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public DiveraResourceSyncService(
        IEinsatzService einsatzService,
        IDiveraService diveraService,
        IMasterDataService masterDataService,
        ILogger<DiveraResourceSyncService> logger)
    {
        _einsatzService = einsatzService;
        _diveraService = diveraService;
        _masterDataService = masterDataService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _einsatzService.EinsatzChanged += OnEinsatzChanged;
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
                    _logger.LogError(ex, "Fehler bei der Übernahme von DIVERA-Rückmeldungen in den Einsatz.");
                }

                await WaitForNextSyncAsync(stoppingToken);
            }
        }
        finally
        {
            _einsatzService.EinsatzChanged -= OnEinsatzChanged;
        }
    }

    private async Task SyncActiveAlarmResponsesAsync(CancellationToken cancellationToken)
    {
        var einsatz = _einsatzService.CurrentEinsatz;
        if (!einsatz.IstEinsatz
            || einsatz.EinsatzEnde is not null
            || string.IsNullOrWhiteSpace(einsatz.Einsatzort)
            || einsatz.DiveraAlarmId is not int alarmId)
            return;

        var alarm = (await _diveraService.GetActiveAlarmsAsync())
            .FirstOrDefault(candidate => candidate.Id == alarmId && !candidate.Closed);
        if (alarm is null)
            return;
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
            return;

        var personal = (await _masterDataService.GetPersonalListAsync())
            .Where(person => person.IsActive && person.DiveraUserId.HasValue)
            .GroupBy(person => person.DiveraUserId!.Value)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
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

        var knownResourceIds = _einsatzService.GetEinsatzResources()
            .Where(resource => resource.EntityType == ResourceEntityType.Person)
            .Select(resource => resource.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        var checkedInCount = 0;
        foreach (var response in positiveResponses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!personal.TryGetValue(response.MemberId, out var person))
                continue;

            await _einsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = person.Id,
                Status = ResourceStatus.AufAnfahrt,
                DiveraRueckmeldung = response.StatusText
            });
            if (knownResourceIds.Add(person.Id))
                checkedInCount++;
        }

        if (checkedInCount > 0)
        {
            _logger.LogInformation(
                "{Count} Person(en) aufgrund positiver DIVERA-Rückmeldung als auf Anfahrt eingecheckt (Alarm {AlarmId}).",
                checkedInCount,
                alarmId);
        }
    }

    private async Task WaitForNextSyncAsync(CancellationToken cancellationToken)
    {
        var einsatz = _einsatzService.CurrentEinsatz;
        var hasLinkedActiveEinsatz = einsatz.IstEinsatz
            && einsatz.EinsatzEnde is null
            && !string.IsNullOrWhiteSpace(einsatz.Einsatzort)
            && einsatz.DiveraAlarmId.HasValue;
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
}
