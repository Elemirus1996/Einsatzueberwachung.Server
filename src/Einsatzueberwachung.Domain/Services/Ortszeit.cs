namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Uhrzeit in der eingestellten Zeitzone (Einstellungen → Zeitzone, Standard Europe/Berlin) – auch dort, wo
    /// kein ITimeService eingebunden werden kann (Modelle mit Standardwerten, Razor-Komponenten). AppTimeService setzt
    /// die Zone beim Start und nach Aenderung. Statt DateTime.Now verwenden: der Server laeuft oft auf UTC (#87).
    /// </summary>
    public static class Ortszeit
    {
        private static TimeZoneInfo _zone = TimeZoneInfo.Local;

        public static TimeZoneInfo Zone
        {
            get => Volatile.Read(ref _zone);
            set => Volatile.Write(ref _zone, value ?? TimeZoneInfo.Local);
        }

        /// <summary>Jetzt in der eingestellten Zeitzone (wie ITimeService.Now)</summary>
        public static DateTime Jetzt => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        /// <summary>
        /// Zeitwert in Ortszeit fuer die Anzeige. UTC (Halsband, GPX, *Utc-Felder) und Serverzeit werden umgerechnet;
        /// unbestimmte Werte sind in dieser Anwendung bereits Ortszeit (so liefert sie ITimeService) und bleiben.
        /// </summary>
        public static DateTime AusUtc(DateTime wert) => wert.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(wert, Zone),
            DateTimeKind.Local => TimeZoneInfo.ConvertTime(wert, Zone),
            _ => wert
        };

        public static DateTime? AusUtc(DateTime? wert) => wert is { } w ? AusUtc(w) : null;

        /// <summary>Ortszeit (z. B. aus ITimeService.Now) nach UTC. Unbestimmte Werte gelten als Ortszeit.</summary>
        public static DateTime NachUtc(DateTime wert)
        {
            switch (wert.Kind)
            {
                case DateTimeKind.Utc:
                    return wert;
                case DateTimeKind.Local:
                    return wert.ToUniversalTime();
                default:
                    var zone = Zone;
                    // Zeit in der Luecke der Sommerzeitumstellung: mit dem Versatz davor rechnen statt abzubrechen
                    var versatz = zone.IsInvalidTime(wert) ? zone.GetUtcOffset(wert.AddHours(-1)) : zone.GetUtcOffset(wert);
                    return DateTime.SpecifyKind(wert - versatz, DateTimeKind.Utc);
            }
        }
    }
}
