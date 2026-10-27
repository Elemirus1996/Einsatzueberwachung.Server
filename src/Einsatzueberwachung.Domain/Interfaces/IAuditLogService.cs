using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface IAuditLogService
    {
        IReadOnlyList<AuditLogEntry> Entries { get; }

        void Log(string aktion, string details, string? teamName = null);
        void Clear();
        Task<List<AuditLogEntry>> GetEntriesAsync(int maxCount = 500);
    }

    public class AuditLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = Ortszeit.Jetzt;
        public string Aktion { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string? TeamName { get; set; }
        public AuditLogKategorie Kategorie { get; set; }
    }

    public enum AuditLogKategorie
    {
        Einsatz,
        Team,
        Notiz,
        System
    }
}
