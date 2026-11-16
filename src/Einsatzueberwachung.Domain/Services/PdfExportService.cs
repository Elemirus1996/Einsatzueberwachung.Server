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

        private DateTime Now => _timeService?.Now ?? Ortszeit.Jetzt;

        private void AddTableRow(TableDescriptor table, string label, string value)
        {
            table.Cell().Background("#F4F6F9").PaddingVertical(5).PaddingHorizontal(8)
                .Text(label).Bold().FontSize(10).FontColor("#2C3E50");
            table.Cell().PaddingVertical(5).PaddingHorizontal(8)
                .Text(value ?? "-").FontSize(10);
        }

        /// <summary>Eine Schreibweise für alle Dauern im Bericht: „45 s“, „4 min 43 s“, „1 h 05 min“</summary>
        public static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} h {ts.Minutes:D2} min";
            if (ts.TotalMinutes >= 1) return ts.Seconds == 0 ? $"{ts.Minutes} min" : $"{ts.Minutes} min {ts.Seconds} s";
            return $"{ts.Seconds} s";
        }

        /// <summary>
        /// Melder („Alarmiert durch“); null, wenn dort nur eine Uhrzeit steht (Einsätze vor 1.16 schrieben die Alarmzeit hinein)
        /// </summary>
        public static string? Melder(string? alarmiert)
        {
            var text = alarmiert?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return DateTime.TryParse(text, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), System.Globalization.DateTimeStyles.None, out _)
                || TimeSpan.TryParse(text, out _) ? null : text;
        }

        /// <summary>
        /// Notiztext für den Bericht: Zeichen ohne Glyphe in der Berichtsschrift (Emoji, Symbole) weglassen und eine
        /// vorangestellte Uhrzeit „[19:31] “ entfernen, wenn sie der Zeit des Eintrags entspricht (steht schon in der Spalte Zeit).
        /// </summary>
        public static string NotizText(string? text, DateTime zeit)
        {
            var sauber = new System.Text.StringBuilder();
            foreach (var rune in (text ?? string.Empty).EnumerateRunes())
            {
                // Emoji außerhalb der BMP, Verschiedene Symbole/Dingbats (✅ ✓ ☎ …), Pfeile-Symbole, Variantenwähler, ZWJ.
                // Gradzeichen, ©, € usw. bleiben.
                if (rune.Value > 0xFFFF || rune.Value is >= 0x2600 and <= 0x27BF || rune.Value is >= 0x2B00 and <= 0x2BFF
                    || rune.Value is 0xFE0F or 0x200D) continue;
                sauber.Append(rune.ToString());
            }
            var ergebnis = sauber.ToString().Trim();
            var vorsatz = $"[{zeit:HH:mm}]";
            if (ergebnis.StartsWith(vorsatz)) ergebnis = ergebnis[vorsatz.Length..].TrimStart();
            return ergebnis;
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

        /// <summary>Programm-Logo (Einsatzüberwachung) für den Fuß der Berichte, eingebettet in die Assembly</summary>
        private static readonly Lazy<byte[]> ProgrammLogo = new(() =>
        {
            using var stream = typeof(PdfExportService).Assembly.GetManifestResourceStream("Einsatzueberwachung.Logo.Hell.png");
            if (stream is null)
                return Array.Empty<byte>();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        });

        private static void ComposeFussLogo(IContainer container)
        {
            var logo = ProgrammLogo.Value;
            if (logo.Length > 0)
                container.AlignRight().AlignMiddle().Height(22).Image(logo).FitArea();
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
