// Quelle: WPF-Projekt Models/DogEntry.cs
// Repräsentiert einen Hund mit Name, Rasse, Ausbildungen und zugeordneten Hundeführern

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models
{
    public class DogEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Rasse { get; set; }
        public string Organisation { get; set; }
        public int Alter { get; set; }
        public DogSpecialization Specializations { get; set; }

        /// <summary>
        /// Liste aller zugewiesenen Hundeführer-IDs (Mehrfachzuweisung möglich).
        /// </summary>
        public List<string> HundefuehrerIds { get; set; }

        /// <summary>
        /// Backward-Compat: Beim Deserialisieren alter JSON-Daten mit einzelnem HundefuehrerId
        /// wird der Wert automatisch in die HundefuehrerIds-Liste übernommen.
        /// Wird beim Serialisieren NICHT geschrieben.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("HundefuehrerId")]
        public string? LegacyHundefuehrerId
        {
            get => null; // Nie serialisieren
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && !HundefuehrerIds.Contains(value))
                {
                    HundefuehrerIds.Add(value);
                }
            }
        }

        /// <summary>
        /// Erster Hundeführer (für Abwärtskompatibilität in Team-Logik).
        /// </summary>
        [JsonIgnore]
        public string PrimaryHundefuehrerId => HundefuehrerIds.FirstOrDefault() ?? string.Empty;

        public string Notizen { get; set; }
        public bool IsActive { get; set; }

        public DogEntry()
        {
            Id = Guid.NewGuid().ToString();
            Name = string.Empty;
            Rasse = string.Empty;
            Organisation = string.Empty;
            Specializations = DogSpecialization.None;
            HundefuehrerIds = new List<string>();
            Notizen = string.Empty;
            IsActive = true;
            Alter = 0;
        }

        public string SpecializationsDisplay
        {
            get
            {
                if (Specializations == DogSpecialization.None)
                    return "Keine Spezialisierung";

                var specList = new List<string>();
                foreach (DogSpecialization spec in Enum.GetValues(typeof(DogSpecialization)))
                {
                    if (spec != DogSpecialization.None && Specializations.HasFlag(spec))
                    {
                        specList.Add(spec.GetDisplayName());
                    }
                }
                return string.Join(", ", specList);
            }
        }

        public string SpecializationsShortDisplay
        {
            get
            {
                if (Specializations == DogSpecialization.None)
                    return "-";

                var specList = new List<string>();
                foreach (DogSpecialization spec in Enum.GetValues(typeof(DogSpecialization)))
                {
                    if (spec != DogSpecialization.None && Specializations.HasFlag(spec))
                    {
                        specList.Add(spec.GetShortName());
                    }
                }
                return string.Join(", ", specList);
            }
        }

        public string PrimarySpecializationColor => Specializations.GetColorHex();
    }
}
