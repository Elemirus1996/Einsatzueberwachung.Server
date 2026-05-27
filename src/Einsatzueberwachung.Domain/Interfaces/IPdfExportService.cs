// Service-Interface für PDF-Export von Einsatzberichten
// Quelle: Abgeleitet von WPF Services/PdfExportService.cs und ViewModels/PdfExportViewModel.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

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
        Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz, bool includeTracks);
        
        /// <summary>
        /// Exportiert einen aktiven Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes);
        Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks);

        /// <summary>
        /// Exportiert eine zweiseitige Einsatzkarte als PDF (Seite 1: Karte, Seite 2: Suchgebietsliste).
        /// Optimiert für Duplexdruck auf A4 Landscape.
        /// filterTeamId: wenn gesetzt, werden nur Suchgebiete dieses Teams angezeigt.
        /// </summary>
        Task<byte[]> ExportEinsatzKarteToPdfBytesAsync(
            EinsatzData einsatzData,
            List<Team> teams,
            MapTileType tileType = MapTileType.Streets,
            string? filterTeamId = null);

        /// <summary>
        /// Exportiert eine Einsatzkarte als PDF mit erweiterten Optionen für Layer-Sichtbarkeit.
        /// </summary>
        Task<byte[]> ExportEinsatzKarteToPdfBytesAsync(
            EinsatzData einsatzData,
            List<Team> teams,
            MapPrintOptions options,
            List<TeamTrackSnapshot>? gpsTracks = null,
            Dictionary<string, List<TeamPhoneLocation>>? phoneTrackHistories = null);
    }

    public class PdfExportResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
