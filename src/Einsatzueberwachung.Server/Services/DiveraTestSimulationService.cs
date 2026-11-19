using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Server.Services;

public sealed record DiveraTestSimulationSummary(
    int AlarmId,
    string AlarmTitle,
    int TotalMembers,
    int PositiveResponseCount,
    int NegativeResponseCount,
    int PendingResponseCount,
    bool LinkedActiveEinsatz);

/// <summary>
/// Lokaler, nicht-persistenter DIVERA-Testmodus fuer Entwicklung und Uebung (Einstellungen -> Integration):
/// erzeugt im Speicher einen aktiven Beispiel-Alarm samt Mitgliederstatus und UCR-Rueckmeldungen, ohne das
/// produktive DIVERA-System zu kontaktieren oder einen echten Alarm auszuloesen.
/// </summary>
public sealed class DiveraTestSimulationService
{
    public const int SimulatedAlarmId = 990001;
    private const int SyntheticMemberIdStart = 900001;
    private const int Status30Minutes = 56296;
    private const int StatusOneHour = 56297;
    private const int StatusNotReady = 56298;
    private const int StatusNoResponse = 0;

    private static readonly IReadOnlyDictionary<int, PersonalEntry> EmptyMemberMap =
        new Dictionary<int, PersonalEntry>();

    private readonly IMasterDataService _masterDataService;
    private readonly ISettingsService _settingsService;
    private readonly IEinsatzService _einsatzService;
    private readonly ITimeService _timeService;
    private readonly ILogger<DiveraTestSimulationService> _logger;
    private readonly object _sync = new();

    private SimulationSnapshot? _snapshot;

    public event Action? StateChanged;

    public DiveraTestSimulationService(
        IMasterDataService masterDataService,
        ISettingsService settingsService,
        IEinsatzService einsatzService,
        ITimeService timeService,
        ILogger<DiveraTestSimulationService> logger)
    {
        _masterDataService = masterDataService;
        _settingsService = settingsService;
        _einsatzService = einsatzService;
        _timeService = timeService;
        _logger = logger;
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _snapshot is not null;
        }
    }

    public DiveraTestSimulationSummary? CurrentSummary
    {
        get
        {
            lock (_sync)
                return _snapshot?.Summary;
        }
    }

    public async Task<DiveraTestSimulationSummary> StartTestAlarmAsync()
    {
        var activePersonnel = SelectionListOrdering
            .OrderPersonnelByFirstName((await _masterDataService.GetPersonalListAsync()).Where(person => person.IsActive))
            .ToList();
        var staffelSettings = await _settingsService.GetStaffelSettingsAsync();
        var now = _timeService.Now;

        var statusDefinitions = BuildStatusDefinitions();
        var (members, ucr, ucrDetails, mappedPersonnelByMemberId) = BuildMembersAndResponses(activePersonnel, statusDefinitions);

        var address = !string.IsNullOrWhiteSpace(staffelSettings.StaffelAdresse)
            ? staffelSettings.StaffelAdresse
            : "Waldparkplatz Musterforst, 45133 Essen";

        var alarm = new DiveraAlarm
        {
            Id = SimulatedAlarmId,
            ForeignId = "TEST-DIVERA",
            Title = "[TEST] Vermisstensuche Musterwald",
            Text = "Lokaler DIVERA-Testalarm (nur auf diesem Server sichtbar, keine echte Alarmierung).",
            Remark = "Simulierte Rückmeldungen zum Testen der Ressourcenverwaltung.",
            Caller = "Test-Leitstelle (lokal)",
            Address = address,
            Lat = 51.4052,
            Lng = 7.0056,
            Date = now,
            Closed = false,
            Priority = true,
            Ucr = ucr,
            UcrDetails = ucrDetails,
            AddressedUserIds = members.Select(member => member.Id).ToList()
        };

        var pullResponse = new DiveraPullResponse
        {
            Alarms = [alarm],
            Members = members,
            StatusDefinitions = statusDefinitions,
            LastUpdated = now
        };

        var (linkedActiveEinsatz, einsatzToLink) = GetAutoLinkPlanForCurrentEinsatz();
        var summary = new DiveraTestSimulationSummary(
            AlarmId: alarm.Id,
            AlarmTitle: alarm.Title,
            TotalMembers: members.Count,
            PositiveResponseCount: ucrDetails.Count(entry => entry.IsPositive),
            NegativeResponseCount: ucrDetails.Count(entry => entry.Status == StatusNotReady),
            PendingResponseCount: ucrDetails.Count(entry => entry.Status == StatusNoResponse),
            LinkedActiveEinsatz: linkedActiveEinsatz);

        var snapshot = new SimulationSnapshot(
            pullResponse,
            mappedPersonnelByMemberId,
            linkedActiveEinsatz,
            summary);
        lock (_sync)
        {
            _snapshot = snapshot;
        }

        if (einsatzToLink is not null)
        {
            einsatzToLink.DiveraAlarmId = SimulatedAlarmId;
            try
            {
                await _einsatzService.UpdateEinsatzAsync(einsatzToLink);
            }
            catch
            {
                einsatzToLink.DiveraAlarmId = null;
                lock (_sync)
                {
                    if (ReferenceEquals(_snapshot, snapshot))
                        _snapshot = null;
                }

                throw;
            }
        }

        _logger.LogInformation(
            "Lokaler DIVERA-Testalarm gestartet ({Total} Mitglieder, {Positive} positive Rückmeldungen, Einsatz verknüpft: {Linked}).",
            summary.TotalMembers,
            summary.PositiveResponseCount,
            linkedActiveEinsatz);

        StateChanged?.Invoke();
        return summary;
    }

    public async Task StopTestAlarmAsync()
    {
        SimulationSnapshot? previous;
        lock (_sync)
        {
            previous = _snapshot;
            _snapshot = null;
        }

        if (previous is null)
            return;

        if (previous.AutoLinkedCurrentEinsatz
            && _einsatzService.CurrentEinsatz.DiveraAlarmId == SimulatedAlarmId
            && _einsatzService.CurrentEinsatz.EinsatzEnde is null)
        {
            _einsatzService.CurrentEinsatz.DiveraAlarmId = null;
            await _einsatzService.UpdateEinsatzAsync(_einsatzService.CurrentEinsatz);
        }

        _logger.LogInformation("Lokaler DIVERA-Testalarm beendet.");
        StateChanged?.Invoke();
    }

    public DiveraPullResponse? GetPullResponseSnapshot()
    {
        lock (_sync)
        {
            return _snapshot is null ? null : ClonePullResponse(_snapshot.PullResponse);
        }
    }

    public DiveraAlarm? GetActiveAlarmSnapshot()
    {
        lock (_sync)
        {
            var alarm = _snapshot?.PullResponse.Alarms.FirstOrDefault(candidate => !candidate.Closed);
            return alarm is null ? null : CloneAlarm(alarm);
        }
    }

    public IReadOnlyDictionary<int, PersonalEntry> GetSimulatedPersonnelByMemberId()
    {
        lock (_sync)
        {
            return _snapshot is null
                ? EmptyMemberMap
                : new Dictionary<int, PersonalEntry>(_snapshot.PersonnelByMemberId);
        }
    }

    private (bool LinkedActiveEinsatz, EinsatzData? EinsatzToLink) GetAutoLinkPlanForCurrentEinsatz()
    {
        var current = _einsatzService.CurrentEinsatz;
        var isRunning = current.IstEinsatz
            && !string.IsNullOrWhiteSpace(current.Einsatzort)
            && current.EinsatzEnde is null;
        if (!isRunning)
            return (false, null);

        if (current.DiveraAlarmId == SimulatedAlarmId)
            return (true, null);

        if (current.DiveraAlarmId.HasValue)
            return (false, null);

        return (true, current);
    }

    private static Dictionary<int, DiveraStatusDefinition> BuildStatusDefinitions() =>
        new()
        {
            [Status30Minutes] = new DiveraStatusDefinition { Id = Status30Minutes, Name = "30 Minuten", Color = "28A745" },
            [StatusOneHour] = new DiveraStatusDefinition { Id = StatusOneHour, Name = "1 Stunde", Color = "FFC107" },
            [StatusNotReady] = new DiveraStatusDefinition { Id = StatusNotReady, Name = "Nicht einsatzbereit", Color = "DC3545" }
        };

    private static (
        List<DiveraMember> Members,
        Dictionary<int, int> Ucr,
        List<DiveraUcrEntry> UcrDetails,
        Dictionary<int, PersonalEntry> PersonnelByMemberId)
        BuildMembersAndResponses(
            IReadOnlyList<PersonalEntry> activePersonnel,
            IReadOnlyDictionary<int, DiveraStatusDefinition> statusDefinitions)
    {
        var members = new List<DiveraMember>();
        var ucr = new Dictionary<int, int>();
        var ucrDetails = new List<DiveraUcrEntry>();
        var personnelByMemberId = new Dictionary<int, PersonalEntry>();

        if (activePersonnel.Count == 0)
        {
            var fallbackPeople = new (int Id, string First, string Last, int Status)[]
            {
                (SyntheticMemberIdStart, "Anna", "Beispiel", Status30Minutes),
                (SyntheticMemberIdStart + 1, "Ben", "Muster", StatusOneHour),
                (SyntheticMemberIdStart + 2, "Clara", "Test", StatusNotReady)
            };

            foreach (var sample in fallbackPeople)
            {
                AddMemberAndResponse(
                    sample.Id,
                    sample.First,
                    sample.Last,
                    sample.Status,
                    statusDefinitions,
                    members,
                    ucr,
                    ucrDetails);
            }

            return (members, ucr, ucrDetails, personnelByMemberId);
        }

        var uniqueConfiguredIds = activePersonnel
            .Where(person => person.DiveraUserId.HasValue)
            .GroupBy(person => person.DiveraUserId!.Value)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet();

        var usedMemberIds = new HashSet<int>(uniqueConfiguredIds);
        var nextSyntheticId = SyntheticMemberIdStart;

        // Rotationsmuster stellt sicher, dass bereits ab 1 Person eine positive Zusage vorliegt
        // und bei mehreren Personen sowohl 30 Min, 1 Std, Absage als auch offene Rueckmeldung vertreten sind.
        int[] statusCycle = [Status30Minutes, StatusOneHour, Status30Minutes, StatusNotReady, StatusNoResponse];

        for (var index = 0; index < activePersonnel.Count; index++)
        {
            var person = activePersonnel[index];
            int memberId;
            if (person.DiveraUserId is int configuredId && uniqueConfiguredIds.Contains(configuredId))
            {
                memberId = configuredId;
            }
            else
            {
                while (!usedMemberIds.Add(nextSyntheticId))
                    nextSyntheticId++;
                memberId = nextSyntheticId++;
            }

            var status = statusCycle[index % statusCycle.Length];
            personnelByMemberId[memberId] = person;
            AddMemberAndResponse(
                memberId,
                person.Vorname,
                person.Nachname,
                status,
                statusDefinitions,
                members,
                ucr,
                ucrDetails);
        }

        return (members, ucr, ucrDetails, personnelByMemberId);
    }

    private static void AddMemberAndResponse(
        int memberId,
        string firstName,
        string lastName,
        int status,
        IReadOnlyDictionary<int, DiveraStatusDefinition> statusDefinitions,
        List<DiveraMember> members,
        Dictionary<int, int> ucr,
        List<DiveraUcrEntry> ucrDetails)
    {
        statusDefinitions.TryGetValue(status, out var statusDef);
        var statusName = statusDef?.Name ?? (status == StatusNoResponse ? "Keine Rückmeldung" : string.Empty);
        var statusColor = statusDef?.Color ?? string.Empty;

        members.Add(new DiveraMember
        {
            Id = memberId,
            Firstname = firstName,
            Lastname = lastName,
            StatusId = status,
            StatusName = statusName,
            StatusColor = statusColor
        });

        ucr[memberId] = status;
        ucrDetails.Add(new DiveraUcrEntry
        {
            MemberId = memberId,
            MemberName = $"{firstName} {lastName}".Trim(),
            Status = status,
            StatusName = statusDef?.Name ?? string.Empty,
            StatusColor = statusColor
        });
    }

    private static DiveraPullResponse ClonePullResponse(DiveraPullResponse source) =>
        new()
        {
            Alarms = source.Alarms.Select(CloneAlarm).ToList(),
            Members = source.Members.Select(CloneMember).ToList(),
            StatusDefinitions = source.StatusDefinitions.ToDictionary(
                pair => pair.Key,
                pair => new DiveraStatusDefinition
                {
                    Id = pair.Value.Id,
                    Name = pair.Value.Name,
                    Color = pair.Value.Color
                }),
            LastUpdated = source.LastUpdated
        };

    private static DiveraAlarm CloneAlarm(DiveraAlarm source) =>
        new()
        {
            Id = source.Id,
            ForeignId = source.ForeignId,
            Title = source.Title,
            Text = source.Text,
            Address = source.Address,
            Lat = source.Lat,
            Lng = source.Lng,
            Date = source.Date,
            Closed = source.Closed,
            Priority = source.Priority,
            Caller = source.Caller,
            Remark = source.Remark,
            Ucr = new Dictionary<int, int>(source.Ucr),
            UcrDetails = source.UcrDetails.Select(entry => new DiveraUcrEntry
            {
                MemberId = entry.MemberId,
                MemberName = entry.MemberName,
                Status = entry.Status,
                StatusName = entry.StatusName,
                StatusColor = entry.StatusColor
            }).ToList(),
            AddressedUserIds = new List<int>(source.AddressedUserIds)
        };

    private static DiveraMember CloneMember(DiveraMember source) =>
        new()
        {
            Id = source.Id,
            Firstname = source.Firstname,
            Lastname = source.Lastname,
            StatusId = source.StatusId,
            StatusName = source.StatusName,
            StatusColor = source.StatusColor,
            QualificationIds = new List<int>(source.QualificationIds)
        };

    public static string BuildDiagnosticJson(DiveraPullResponse pull) =>
        JsonSerializer.Serialize(pull, new JsonSerializerOptions { WriteIndented = true });

    private sealed record SimulationSnapshot(
        DiveraPullResponse PullResponse,
        Dictionary<int, PersonalEntry> PersonnelByMemberId,
        bool AutoLinkedCurrentEinsatz,
        DiveraTestSimulationSummary Summary);
}
