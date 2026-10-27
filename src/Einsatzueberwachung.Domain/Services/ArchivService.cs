// Archiv-Service - Speichert und verwaltet abgeschlossene Einsaetze

using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class ArchivService : IArchivService
    {
        private readonly string _archivDirectory;
        private readonly string _archivFilePath;
        private readonly ITimeService? _timeService;
        private readonly ILogger<ArchivService>? _logger;
        private List<ArchivedEinsatz> _archiv = new();
        private bool _isLoaded = false;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ArchivService(ITimeService? timeService = null, ILogger<ArchivService>? logger = null)
        {
            _timeService = timeService;
            _logger = logger;
            _archivDirectory = AppPathResolver.GetArchiveDirectory();
            _archivFilePath = Path.Combine(_archivDirectory, "einsatz_archiv.json");
        }

        private DateTime Now => _timeService?.Now ?? Ortszeit.Jetzt;

        private async Task EnsureLoadedAsync()
        {
            if (_isLoaded) return;

            if (File.Exists(_archivFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_archivFilePath);
                    _archiv = JsonSerializer.Deserialize<List<ArchivedEinsatz>>(json, JsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Archivdatei konnte nicht geladen werden, starte mit leerem Archiv");
                    _archiv = new();
                }
            }

            _isLoaded = true;
        }

        private async Task SaveAsync()
        {
            var json = JsonSerializer.Serialize(_archiv, JsonOptions);
            await File.WriteAllTextAsync(_archivFilePath, json);
        }

        public async Task<ArchivedEinsatz> ArchiveEinsatzAsync(
            EinsatzData einsatzData,
            string ergebnis,
            string bemerkungen,
            List<string>? personalVorOrt = null,
            List<string>? hundeVorOrt = null)
        {
            await EnsureLoadedAsync();

            var archived = ArchivedEinsatz.FromEinsatzData(einsatzData, ergebnis, bemerkungen, Now);

            if (personalVorOrt is not null)
            {
                archived.PersonalNamen = personalVorOrt
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToList();
            }

            if (hundeVorOrt is not null)
            {
                archived.HundeNamen = hundeVorOrt
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToList();
            }

            archived.AnzahlPersonal = archived.PersonalNamen.Count;
            archived.AnzahlHunde = archived.HundeNamen.Count;
            archived.AnzahlRessourcen = archived.AnzahlPersonal + archived.AnzahlHunde + archived.AnzahlDrohnen;

            _archiv.Insert(0, archived);

            await SaveAsync();

            return archived;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            await EnsureLoadedAsync();

            var einsatz = _archiv.FirstOrDefault(e => e.Id == id);
            if (einsatz == null) return false;

            _archiv.Remove(einsatz);
            await SaveAsync();

            return true;
        }

        public async Task UpdateArchivedEinsatzAsync(ArchivedEinsatz archived)
        {
            await EnsureLoadedAsync();

            var index = _archiv.FindIndex(e => e.Id == archived.Id);
            if (index >= 0)
            {
                _archiv[index] = archived;
            }
            else
            {
                _archiv.Insert(0, archived);
            }

            await SaveAsync();
        }
    }
}
