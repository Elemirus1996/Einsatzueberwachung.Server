using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class PdfExportService : IPdfExportService
    {
        private readonly ISettingsService? _settingsService;
        private readonly ITimeService? _timeService;
        private readonly IStaticMapRenderer? _mapRenderer;

        public PdfExportService(ISettingsService? settingsService = null, ITimeService? timeService = null, IStaticMapRenderer? mapRenderer = null)
        {
            _settingsService = settingsService;
            _timeService = timeService;
            _mapRenderer = mapRenderer;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private DateTime Now => _timeService?.Now ?? DateTime.Now;

        private void AddTableRow(TableDescriptor table, string label, string value)
        {
            table.Cell().Background("#F4F6F9").PaddingVertical(5).PaddingHorizontal(8)
                .Text(label).Bold().FontSize(10).FontColor("#2C3E50");
            table.Cell().PaddingVertical(5).PaddingHorizontal(8)
                .Text(value ?? "-").FontSize(10);
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private string GetNoteTypeColor(GlobalNotesEntryType type)
        {
            return type switch
            {
                GlobalNotesEntryType.TeamStart => "#27AE60",
                GlobalNotesEntryType.TeamStop => "#7F8C8D",
                GlobalNotesEntryType.TeamPause => "#2C81BA",
                GlobalNotesEntryType.TeamWarning => "#C0392B",
                GlobalNotesEntryType.TeamReset => "#7F8C8D",
                GlobalNotesEntryType.EinsatzUpdate => "#2C3E50",
                GlobalNotesEntryType.System => "#95A5A6",
                _ => "#95A5A6"
            };
        }

        private static string GetNoteTypeLabel(GlobalNotesEntryType type)
        {
            return type switch
            {
                GlobalNotesEntryType.TeamStart => "Ausrücken",
                GlobalNotesEntryType.TeamStop => "Einrücken",
                GlobalNotesEntryType.TeamPause => "Kurzpause",
                GlobalNotesEntryType.TeamWarning => "Warnung",
                GlobalNotesEntryType.TeamReset => "Reset",
                GlobalNotesEntryType.EinsatzUpdate => "Einsatz",
                GlobalNotesEntryType.System => "System",
                GlobalNotesEntryType.Manual => "Notiz",
                _ => type.ToString()
            };
        }

        private static string BuildKontaktLine(StaffelInfo info)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Telefon))
                parts.Add($"Tel: {info.Telefon}");
            if (!string.IsNullOrWhiteSpace(info.Email))
                parts.Add($"E-Mail: {info.Email}");
            return string.Join(" | ", parts);
        }

        private bool TryLoadLogoBytes(string? logoPath, out byte[] logoBytes)
        {
            logoBytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(logoPath))
                return false;

            try
            {
                var filePath = ResolveLogoPath(logoPath);
                if (!File.Exists(filePath))
                    return false;

                var extension = Path.GetExtension(filePath);
                if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
                    return false;

                logoBytes = File.ReadAllBytes(filePath);
                return logoBytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveLogoPath(string logoPath)
        {
            if (Path.IsPathRooted(logoPath))
                return logoPath;

            var cleanPath = logoPath.TrimStart('/', '\\');
            return Path.Combine(AppPathResolver.GetDataDirectory(), cleanPath);
        }

        private async Task<StaffelInfo> ResolveStaffelInfoAsync(EinsatzData einsatzData)
        {
            var settings = await GetStaffelSettingsOrDefaultAsync();
            return new StaffelInfo
            {
                Name = PickValue(einsatzData.StaffelName, settings.StaffelName),
                Address = PickValue(einsatzData.StaffelAdresse, settings.StaffelAdresse),
                Telefon = PickValue(einsatzData.StaffelTelefon, settings.StaffelTelefon),
                Email = PickValue(einsatzData.StaffelEmail, settings.StaffelEmail),
                LogoPath = PickValue(settings.PdfLogoPfad, settings.StaffelLogoPfad)
            };
        }

        private async Task<StaffelInfo> ResolveStaffelInfoAsync(ArchivedEinsatz einsatz)
        {
            var settings = await GetStaffelSettingsOrDefaultAsync();
            return new StaffelInfo
            {
                Name = PickValue(einsatz.StaffelName, settings.StaffelName),
                Address = PickValue(einsatz.StaffelAdresse, settings.StaffelAdresse),
                Telefon = PickValue(einsatz.StaffelTelefon, settings.StaffelTelefon),
                Email = PickValue(einsatz.StaffelEmail, settings.StaffelEmail),
                LogoPath = PickValue(settings.PdfLogoPfad, settings.StaffelLogoPfad)
            };
        }

        private async Task<StaffelSettings> GetStaffelSettingsOrDefaultAsync()
        {
            if (_settingsService is null)
                return new StaffelSettings();

            try
            {
                return await _settingsService.GetStaffelSettingsAsync();
            }
            catch
            {
                return new StaffelSettings();
            }
        }

        private static string PickValue(string preferred, string fallback)
            => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

        private static (byte r, byte g, byte b) ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7)
                return (255, 68, 68);

            try
            {
                hex = hex.TrimStart('#');
                return (
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            catch
            {
                return (255, 68, 68);
            }
        }

        private sealed class StaffelInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string Telefon { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string LogoPath { get; set; } = string.Empty;
        }
    }
}
