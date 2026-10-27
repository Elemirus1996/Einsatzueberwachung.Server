using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class DiveraStatus : IDisposable
{
    [Inject] private IDiveraService DiveraService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private List<DiveraAlarm> _alarms = new();
    private List<DiveraMember> _members = new();
    private Dictionary<int, string> _personalByDiveraId = new();
    private bool _loading;
    private bool _connectionOk;
    private string? _errorMessage;
    private DateTime? _lastUpdated;
    private CancellationTokenSource? _pollingCts;

    private const int Status30Minutes = 56296;
    private const int StatusOneHour = 56297;
    private const int StatusNotReady = 56298;

    private List<DiveraUcrEntry> CurrentAlarmResponses => _alarms
        .OrderByDescending(alarm => alarm.Date)
        .FirstOrDefault()?.UcrDetails ?? new List<DiveraUcrEntry>();

    private List<DiveraUcrEntry> Members30Minutes => CurrentAlarmResponses
        .Where(response => response.Status == Status30Minutes)
        .OrderBy(response => GetUcrDisplayName(response))
        .ToList();

    private List<DiveraUcrEntry> MembersOneHour => CurrentAlarmResponses
        .Where(response => response.Status == StatusOneHour)
        .OrderBy(response => GetUcrDisplayName(response))
        .ToList();

    private List<DiveraUcrEntry> MembersNotReady => CurrentAlarmResponses
        .Where(response => response.Status == StatusNotReady)
        .OrderBy(response => GetUcrDisplayName(response))
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        StartPolling();
    }

    private void StartPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(_pollingCts.Token);
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var intervalSeconds = _alarms.Any()
                ? DiveraService.PollIntervalActiveSeconds
                : DiveraService.PollIntervalIdleSeconds;

            if (intervalSeconds < 5)
                intervalSeconds = 5;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await InvokeAsync(async () =>
            {
                await RefreshAsync();
                StateHasChanged();
            });
        }
    }

    private async Task RefreshAsync()
    {
        _loading = true;
        _errorMessage = null;

        try
        {
            _alarms = await DiveraService.GetActiveAlarmsAsync();

            var personalList = await MasterDataService.GetPersonalListAsync();
            _personalByDiveraId = personalList
                .Where(p => p.DiveraUserId.HasValue)
                .ToDictionary(p => p.DiveraUserId!.Value, p => p.FullName);
            foreach (var alarm in _alarms)
                foreach (var ucr in alarm.UcrDetails)
                    if (_personalByDiveraId.TryGetValue(ucr.MemberId, out var realName) && !string.IsNullOrWhiteSpace(realName))
                        ucr.MemberName = realName;

            var pull = await DiveraService.PullAllAsync();

            _connectionOk = _alarms.Any() || pull != null;

            if (!_connectionOk && DiveraService.IsConfigured)
            {
                _errorMessage = "Keine Antwort von Divera erhalten. Bitte Verbindung und API-Key prüfen.";
            }

            _members = pull?.Members
                .OrderBy(m => m.StatusId == 0 ? 99 : m.StatusId)
                .ThenBy(m => m.Lastname)
                .ThenBy(m => m.Firstname)
                .ToList() ?? new();
            _lastUpdated = pull?.LastUpdated ?? (_alarms.Any() ? DateTime.Now : null);
        }
        catch (Exception ex)
        {
            _connectionOk = false;
            _errorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
    }

    private string GetMemberDisplayName(DiveraMember member)
    {
        if (_personalByDiveraId.TryGetValue(member.Id, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return member.FullName;
    }

    private string GetUcrDisplayName(DiveraUcrEntry response)
    {
        if (!string.IsNullOrWhiteSpace(response.MemberName) && !response.MemberName.StartsWith("#"))
            return response.MemberName;
        if (_personalByDiveraId.TryGetValue(response.MemberId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return response.MemberName;
    }
}
