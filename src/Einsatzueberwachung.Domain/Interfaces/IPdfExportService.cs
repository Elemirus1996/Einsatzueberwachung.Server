// Service-Interface für PDF-Export von Einsatzberichten
// Quelle: Abgeleitet von WPF Services/PdfExportService.cs und ViewModels/PdfExportViewModel.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface IPdfExportService
    {
        /// <summary>
        /// Exportiert einen aktiven Einsatz als PDF
        /// </summary>
        Task<PdfExportResult> ExportEinsatzToPdfAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes);
        Task<PdfExportResult> ExportEinsatzToPdfAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks);
        
        /// <summary>
        /// Exportiert einen archivierten Einsatz als PDF
        /// </summary>
        Task<PdfExportResult> ExportArchivedEinsatzToPdfAsync(ArchivedEinsatz archivedEinsatz);
        Task<PdfExportResult> ExportArchivedEinsatzToPdfAsync(ArchivedEinsatz archivedEinsatz, bool includeTracks);
        
        /// <summary>
        /// Exportiert einen archivierten Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz);
        
        /// <summary>
        /// Exportiert einen aktiven Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes);
        Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks);
    }

    public class PdfExportResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
