using System;
using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraPullResponse
    {
        public List<DiveraAlarm> Alarms { get; set; } = new();
        public List<DiveraMember> Members { get; set; } = new();
        public Dictionary<int, DiveraStatusDefinition> StatusDefinitions { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}
