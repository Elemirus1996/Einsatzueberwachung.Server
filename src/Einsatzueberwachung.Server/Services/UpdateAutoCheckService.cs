using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

public class UpdateAutoCheckService : BackgroundService
{
    private readonly GitHubUpdateService _updateService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<UpdateAutoCheckService> _logger;

    public UpdateAutoCheckService(
        GitHubUpdateService updateService,
        ISettingsService settingsService,
        ILogger<UpdateAutoCheckService> logger)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _settingsService.GetAppSettingsAsync();
                if (settings.AutoCheckUpdates)
                {
                    await _updateService.CheckForUpdatesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatische Update-Pruefung fehlgeschlagen");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}
