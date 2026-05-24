using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Einsatzueberwachung.Server.Security;
using QRCoder;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    [Inject] private ITeamMobileTokenService TeamMobileTokenService { get; set; } = default!;
    [Inject] private IOptionsMonitor<TeamMobileOptions> TeamMobileOptions { get; set; } = default!;

    private bool _showTeamMobileModal;
    private string _teamMobileQrDataUrl = string.Empty;
    private string _teamMobileFullUrl = string.Empty;

    private bool _showTeamMessageModal;
    private string _teamMessageTargetId = string.Empty;
    private string _teamMessageText = string.Empty;
    private string _teamMessageFeedback = string.Empty;

    private void OpenTeamMessageModal()
    {
        _teamMessageTargetId = EinsatzService.Teams.FirstOrDefault()?.TeamId ?? string.Empty;
        _teamMessageText = string.Empty;
        _teamMessageFeedback = string.Empty;
        _showTeamMessageModal = true;
    }

    private void CloseTeamMessageModal() => _showTeamMessageModal = false;

    private void SendTeamMessage()
    {
        if (string.IsNullOrWhiteSpace(_teamMessageTargetId) || string.IsNullOrWhiteSpace(_teamMessageText))
        {
            _teamMessageFeedback = "Team und Text erforderlich.";
            return;
        }
        TeamMobileTokenService.BroadcastTeamMessage(_teamMessageTargetId, _teamMessageText.Trim());
        _teamMessageFeedback = "Nachricht gesendet.";
        _teamMessageText = string.Empty;
    }

    private async Task OpenTeamMobileModalAsync()
    {
        var token = TeamMobileTokenService.CurrentMasterToken;
        if (string.IsNullOrEmpty(token))
        {
            _teamMobileFullUrl = string.Empty;
            _teamMobileQrDataUrl = string.Empty;
        }
        else
        {
            var baseUrl = await ResolveTeamMobileBaseUrlAsync();
            _teamMobileFullUrl = string.IsNullOrEmpty(baseUrl)
                ? $"/team/login/{token}"
                : $"{baseUrl}/team/login/{token}";

            _teamMobileQrDataUrl = GenerateQrDataUrl(_teamMobileFullUrl);
        }
        _showTeamMobileModal = true;
    }

    /// <summary>
    /// Gibt die effektive Basis-URL für den Team-Mobile QR-Code zurück.
    /// Priorität: AppSettings.MobileBaseUrl > TeamMobileOptions.PublicBaseUrl > leer (= relativ).
    /// </summary>
    private async Task<string> ResolveTeamMobileBaseUrlAsync()
    {
        var appSettings = await SettingsService.GetAppSettingsAsync();
        if (!string.IsNullOrWhiteSpace(appSettings.MobileBaseUrl))
            return NormalizeBaseUrl(appSettings.MobileBaseUrl);

        var configuredUrl = TeamMobileOptions.CurrentValue.PublicBaseUrl;
        if (!string.IsNullOrWhiteSpace(configuredUrl))
            return NormalizeBaseUrl(configuredUrl);

        return string.Empty;
    }

    /// <summary>
    /// Stellt sicher, dass die URL ein gültiges URL-Schema hat. Fehlt es, wird „https://" vorangestellt.
    /// </summary>
    private static string NormalizeBaseUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }
        return trimmed;
    }

    private void CloseTeamMobileModal() => _showTeamMobileModal = false;

    private static string GenerateQrDataUrl(string content)
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var pngQr = new PngByteQRCode(data);
        var bytes = pngQr.GetGraphic(8);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
