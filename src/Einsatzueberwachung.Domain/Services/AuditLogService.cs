using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Domain.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly List<AuditLogEntry> _entries = new();
        private readonly object _lock = new();
        private const int MaxEntries = 2000;

        public IReadOnlyList<AuditLogEntry> Entries
        {
            get { lock (_lock) { return _entries.AsReadOnly(); } }
        }

        public void Log(string aktion, string details, string? teamName = null)
        {
            var kategorie = aktion.StartsWith("Team") || aktion.StartsWith("Timer")
                ? AuditLogKategorie.Team
                : aktion.StartsWith("Einsatz")
                    ? AuditLogKategorie.Einsatz
                    : aktion.StartsWith("Notiz") || aktion.StartsWith("Funk")
                        ? AuditLogKategorie.Notiz
                        : AuditLogKategorie.System;

            var entry = new AuditLogEntry
            {
                Timestamp = Ortszeit.Jetzt,
                Aktion = aktion,
                Details = details,
                TeamName = teamName,
                Kategorie = kategorie
            };

            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
            }
        }

        public void Clear()
        {
            lock (_lock) { _entries.Clear(); }
        }

        public Task<List<AuditLogEntry>> GetEntriesAsync(int maxCount = 500)
        {
            lock (_lock)
            {
                var result = _entries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(maxCount)
                    .ToList();
                return Task.FromResult(result);
            }
        }
    }
}
