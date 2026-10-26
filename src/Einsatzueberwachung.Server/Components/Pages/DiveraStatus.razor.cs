using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class DiveraStatus : IDisposable
{
    [Inject] private IDiveraService DiveraService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private List<DiveraAlarm> _alarms = new();
    private List<DiveraMember> _members = new();
    private bool _loading;
    private bool _connectionOk;
    private string? _errorMessage;
    private DateTime? _lastUpdated;
    private CancellationTokenSource? _pollingCts;

    private DiveraAlarm? CurrentAlarm => _alarms.OrderByDescending(alarm => alarm.Date).FirstOrDefault();

    private List<DiveraUcrEntry> CurrentAlarmResponses => CurrentAlarm?.UcrDetails ?? new List<DiveraUcrEntry>();

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

    // Namen kommen aufgeloest aus dem Divera-Dienst (Stammdaten „Divera Benutzer-ID“ vor Divera-Namen)
    private static string GetMemberDisplayName(DiveraMember member) => member.FullName;

    private static string GetUcrDisplayName(DiveraUcrEntry response) => response.MemberName;
}
