// Quelle: WPF-Projekt Models/EinsatzData.cs
// Zentrale Einsatzdaten (Einsatzleiter, Ort, Teams, Funksprüche, Suchgebiete)

using System;
using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models
{
    public class EinsatzData
    {
        public string Einsatzleiter { get; set; }
        public string Fuehrungsassistent { get; set; }
        public string Alarmiert { get; set; }
        public string Einsatzort { get; set; }
        public string MapAddress { get; set; }
        public string ExportPfad { get; set; }
        public bool IstEinsatz { get; set; }
        public int AnzahlTeams { get; set; }
        public DateTime EinsatzDatum { get; set; }
        public DateTime? EinsatzEnde { get; set; }

        public string EinsatzNummer { get; set; }
        public string StaffelName { get; set; }
        public string StaffelAdresse { get; set; }
        public string StaffelTelefon { get; set; }
        public string StaffelEmail { get; set; }
        public string StaffelLogoPfad { get; set; }
        public DateTime? AlarmierungsZeit { get; set; }

        public List<GlobalNotesEntry> GlobalNotesEntries { get; set; }
        public List<SearchArea> SearchAreas { get; set; }
        public List<Team> Teams { get; set; }

        public (double Latitude, double Longitude)? ElwPosition { get; set; }
        
        // Koordinaten fuer Wetter-Abfrage
        public double? ElwLatitude => ElwPosition?.Latitude;
        public double? ElwLongitude => ElwPosition?.Longitude;

        public EinsatzData()
        {
            Einsatzleiter = string.Empty;
            Fuehrungsassistent = string.Empty;
            Alarmiert = string.Empty;
            Einsatzort = string.Empty;
            MapAddress = string.Empty;
            ExportPfad = string.Empty;
            IstEinsatz = true;
            AnzahlTeams = 1;
            EinsatzDatum = DateTime.Now;
            EinsatzEnde = null;
            EinsatzNummer = string.Empty;
            StaffelName = string.Empty;
            StaffelAdresse = string.Empty;
            StaffelTelefon = string.Empty;
            StaffelEmail = string.Empty;
            StaffelLogoPfad = string.Empty;
            AlarmierungsZeit = null;
            GlobalNotesEntries = new List<GlobalNotesEntry>();
            SearchAreas = new List<SearchArea>();
            Teams = new List<Team>();
            ElwPosition = null;
        }

        public string EinsatzTyp => IstEinsatz ? "Einsatz" : "Übung";
    }
}
