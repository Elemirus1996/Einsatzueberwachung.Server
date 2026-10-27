using System;

namespace Einsatzueberwachung.Domain.Models
{
    public class TeamPhoneLocation
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }
        public double? Accuracy { get; set; }
        /// <summary>Akkustand des Handys in Prozent (0–100), sofern gemeldet (Traccar Client)</summary>
        public int? BatteryPercent { get; set; }
    }
}
