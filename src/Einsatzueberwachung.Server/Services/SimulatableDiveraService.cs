using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Decorator vor <see cref="IDiveraService"/>: liefert bei aktivem lokalem DIVERA-Testmodus
/// ausschliesslich die In-Memory-Simulationsdaten, ohne das produktive DIVERA-System aufzurufen.
/// </summary>
public sealed class SimulatableDiveraService : IDiveraService, IDisposable
{
    private readonly IDiveraService _inner;
    private readonly DiveraTestSimulationService _simulation;

    public event Action? DataChanged;

    public SimulatableDiveraService(IDiveraService inner, DiveraTestSimulationService simulation)
    {
        _inner = inner;
        _simulation = simulation;
        _inner.DataChanged += OnInnerDataChanged;
        _simulation.StateChanged += OnSimulationStateChanged;
    }

    public bool IsConfigured => _simulation.IsActive || _inner.IsConfigured;

    public bool HasActiveAlarms => _simulation.IsActive || _inner.HasActiveAlarms;

    public int PollIntervalIdleSeconds => _inner.PollIntervalIdleSeconds;

    public int PollIntervalActiveSeconds => _simulation.IsActive ? 15 : _inner.PollIntervalActiveSeconds;

    public async Task<List<DiveraAlarm>> GetActiveAlarmsAsync()
    {
        if (_simulation.GetActiveAlarmSnapshot() is { } simulatedAlarm)
        {
            if (_inner.IsConfigured)
            {
                var realAlarms = await _inner.GetActiveAlarmsAsync();
                return [simulatedAlarm, .. realAlarms.Where(alarm => alarm.Id != simulatedAlarm.Id)];
            }

            return [simulatedAlarm];
        }

        return await _inner.GetActiveAlarmsAsync();
    }

    public async Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId)
    {
        if (_simulation.IsActive && _simulation.GetActiveAlarmSnapshot() is { } alarm && alarm.Id == alarmId)
            return alarm;

        return await _inner.GetAlarmByIdAsync(alarmId);
    }

    public async Task<DiveraAlarm?> GetLastAlarmAsync()
    {
        if (_simulation.IsActive)
            return _simulation.GetActiveAlarmSnapshot();

        return await _inner.GetLastAlarmAsync();
    }

    public async Task<List<DiveraMember>> GetMembersWithStatusAsync()
    {
        if (_simulation.GetPullResponseSnapshot() is { } pull)
        {
            return pull.Members
                .OrderBy(member => member.StatusId == 0 ? 99 : member.StatusId)
                .ThenBy(member => member.Lastname)
                .ThenBy(member => member.Firstname)
                .ToList();
        }

        return await _inner.GetMembersWithStatusAsync();
    }

    public async Task<List<DiveraMember>> GetAvailableMembersAsync()
    {
        if (_simulation.IsActive)
        {
            var members = await GetMembersWithStatusAsync();
            return members.Where(member => member.IsVerfuegbar).ToList();
        }

        return await _inner.GetAvailableMembersAsync();
    }

    public async Task<DiveraPullResponse?> PullAllAsync()
    {
        if (_simulation.GetPullResponseSnapshot() is { } pull)
            return pull;

        return await _inner.PullAllAsync();
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (_simulation.IsActive)
            return true;

        return await _inner.TestConnectionAsync();
    }

    public async Task<Dictionary<string, string>> GetRawDiagnosticAsync()
    {
        if (_simulation.GetPullResponseSnapshot() is { } pull)
        {
            return new Dictionary<string, string>
            {
                ["mode"] = "simulation",
                ["simulated_json"] = DiveraTestSimulationService.BuildDiagnosticJson(pull)
            };
        }

        return await _inner.GetRawDiagnosticAsync();
    }

    public Task RefreshConfigurationAsync() => _inner.RefreshConfigurationAsync();

    public void Dispose()
    {
        _inner.DataChanged -= OnInnerDataChanged;
        _simulation.StateChanged -= OnSimulationStateChanged;
    }

    private void OnInnerDataChanged()
    {
        if (!_simulation.IsActive)
            DataChanged?.Invoke();
    }

    private void OnSimulationStateChanged() => DataChanged?.Invoke();
}
