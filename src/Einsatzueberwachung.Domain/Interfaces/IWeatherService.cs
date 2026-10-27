// Service-Interface fuer Wetter-Abfragen vom Deutschen Wetterdienst (DWD)

using System;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface IWeatherService
    {
        /// <summary>
        /// Holt aktuelle Wetterdaten fuer eine Position
        /// </summary>
        Task<WeatherData?> GetCurrentWeatherAsync(double latitude, double longitude);

        /// <summary>
        /// Holt aktuelle Wetterdaten fuer eine Adresse/Ort
        /// </summary>
        Task<WeatherData?> GetCurrentWeatherByAddressAsync(string address);

        /// <summary>
        /// Ermittelt Koordinaten zu einer Adresse/Ort
        /// </summary>
        Task<(double Latitude, double Longitude)?> GeocodeAddressAsync(string address);

        /// <summary>
        /// Holt Wettervorhersage fuer die naechsten Stunden
        /// </summary>
        Task<WeatherForecast?> GetForecastAsync(double latitude, double longitude);
        
        /// <summary>
        /// Holt Flugwetter-Daten (METAR/Synop) fuer eine Position
        /// </summary>
        Task<FlugwetterData?> GetFlugwetterAsync(double latitude, double longitude);
    }

    /// <summary>
    /// Flugwetter-Daten (basierend auf DWD Open Data)
    /// </summary>
    public class FlugwetterData
    {
        public DateTime Zeitpunkt { get; set; } = Ortszeit.Jetzt;
        public string StationsName { get; set; } = string.Empty;
        public string StationsId { get; set; } = string.Empty;
        public double Entfernung { get; set; } // km zur angefragten Position
        
        // Wind
        public double Windgeschwindigkeit { get; set; } // km/h
        public double Windboeen { get; set; } // km/h
        public int Windrichtung { get; set; } // Grad
        public string WindrichtungText { get; set; } = string.Empty;
        
        // Sicht und Wolken
        public double Sichtweite { get; set; } // km
        public double SichtweiteRoh { get; set; } // Meter (für Kategorieberechnung)
        public int? Wolkenuntergrenze { get; set; } // m (null = CAVOK/wolkenfrei)
        public int WolkenuntergrenzeFuss { get; set; } // Fuß (für METAR-Anzeige)
        public int Wolkenbedeckung { get; set; } // Prozent
        public string WolkenbedeckungText => GetWolkenbedeckungText();
        
        // Temperatur
        public double Temperatur { get; set; } // Celsius
        public double Taupunkt { get; set; } // Celsius
        public double Spread { get; set; } // Temperatur - Taupunkt
        
        // Niederschlag
        public double Niederschlag { get; set; } // mm/h
        public string Niederschlagsart { get; set; } = string.Empty; // RA, SN, DZ, etc.
        
        // Luftdruck
        public int QNH { get; set; } // hPa (auf Meereshoehe reduziert)
        
        // Flugbedingungen
        public FlugKategorie Kategorie { get; set; } = FlugKategorie.VFR;
        public bool IstDrohnenflugMoeglich { get; set; }
        public string DrohnenflugHinweis { get; set; } = string.Empty;
        
        // Rohdaten
        public string? MetarRaw { get; set; }
        
        private string GetWolkenbedeckungText()
        {
            return Wolkenbedeckung switch
            {
                <= 12 => "SKC", // Sky Clear
                <= 25 => "FEW", // Few (1-2 Oktas)
                <= 50 => "SCT", // Scattered (3-4 Oktas)
                <= 87 => "BKN", // Broken (5-7 Oktas)
                _ => "OVC"      // Overcast (8 Oktas)
            };
        }
        
        public string GetKategorieBadgeClass()
        {
            return Kategorie switch
            {
                FlugKategorie.VFR => "bg-success",
                FlugKategorie.MVFR => "bg-info",
                FlugKategorie.IFR => "bg-warning text-dark",
                FlugKategorie.LIFR => "bg-danger",
                _ => "bg-secondary"
            };
        }
        
        public string GetKategorieText()
        {
            return Kategorie switch
            {
                FlugKategorie.VFR => "VFR - Sichtflugbedingungen",
                FlugKategorie.MVFR => "MVFR - Marginale Sichtflugbedingungen",
                FlugKategorie.IFR => "IFR - Instrumentenflugbedingungen",
                FlugKategorie.LIFR => "LIFR - Geringe Instrumentenflugbedingungen",
                _ => "Unbekannt"
            };
        }
    }
    
    /// <summary>
    /// Flugkategorien nach internationaler Klassifikation
    /// </summary>
    public enum FlugKategorie
    {
        VFR,    // Visual Flight Rules: Sicht > 8km, Wolken > 1000m
        MVFR,   // Marginal VFR: Sicht 5-8km oder Wolken 500-1000m
        IFR,    // Instrument Flight Rules: Sicht 1.5-5km oder Wolken 200-500m
        LIFR    // Low IFR: Sicht < 1.5km oder Wolken < 200m
    }

    /// <summary>
    /// Aktuelle Wetterdaten
    /// </summary>
    public class WeatherData
    {
        public DateTime Zeitpunkt { get; set; } = Ortszeit.Jetzt;
        public double Temperatur { get; set; } // in Celsius
        public double GefuehlteTemperatur { get; set; } // Wind Chill / Heat Index
        public int Luftfeuchtigkeit { get; set; } // in Prozent
        public double Windgeschwindigkeit { get; set; } // in km/h
        public double Windboeen { get; set; } // in km/h - Boeen
        public int Windrichtung { get; set; } // in Grad (0-360)
        public string WindrichtungText { get; set; } = string.Empty; // N, NE, E, etc.
        public double Niederschlag { get; set; } // mm in letzter Stunde
        public int Bewoelkung { get; set; } // in Prozent
        public double Sichtweite { get; set; } // in km
        public double Luftdruck { get; set; } // in hPa
        public double Taupunkt { get; set; } // in Celsius
        public string Wetterlage { get; set; } = string.Empty; // Beschreibung
        public string WetterIcon { get; set; } = string.Empty; // Icon-Code
        public bool IstTag { get; set; } = true;
        
        // DWD-spezifische Warnungen
        public bool HatWarnung { get; set; }
        public string Warnung { get; set; } = string.Empty;
        public WarnungsStufe WarnungsStufe { get; set; } = WarnungsStufe.Keine;

        // Berechnete Eigenschaften
        public string TemperaturFormatiert => $"{Temperatur:0.0}°C";
        public string WindFormatiert => $"{Windgeschwindigkeit:0} km/h {WindrichtungText}";
        public string LuftfeuchtigkeitFormatiert => $"{Luftfeuchtigkeit}%";
        
        public string GetBootstrapIcon()
        {
            // Mapping von Wetterlage zu Bootstrap Icons
            var wetter = Wetterlage.ToLowerInvariant();
            
            if (wetter.Contains("gewitter")) return "bi-cloud-lightning-rain";
            if (wetter.Contains("regen") || wetter.Contains("schauer")) return "bi-cloud-rain";
            if (wetter.Contains("schnee")) return "bi-cloud-snow";
            if (wetter.Contains("nebel")) return "bi-cloud-fog";
            if (wetter.Contains("bewoelkt") || wetter.Contains("wolkig")) return "bi-cloud";
            if (wetter.Contains("sonnig") || wetter.Contains("klar"))
            {
                return IstTag ? "bi-sun" : "bi-moon-stars";
            }
            if (Bewoelkung > 70) return "bi-clouds";
            if (Bewoelkung > 30) return "bi-cloud-sun";
            
            return IstTag ? "bi-sun" : "bi-moon";
        }

        public string GetWarnungBadgeClass()
        {
            return WarnungsStufe switch
            {
                WarnungsStufe.Vorabwarnung => "bg-info",
                WarnungsStufe.Markant => "bg-warning text-dark",
                WarnungsStufe.Unwetter => "bg-danger",
                WarnungsStufe.ExtremesUnwetter => "bg-danger",
                _ => "bg-secondary"
            };
        }
    }

    /// <summary>
    /// Wettervorhersage fuer mehrere Stunden
    /// </summary>
    public class WeatherForecast
    {
        public WeatherData[] StundenVorhersage { get; set; } = Array.Empty<WeatherData>();
        public DateTime LetzteAktualisierung { get; set; } = Ortszeit.Jetzt;
    }

    /// <summary>
    /// DWD Warnungsstufen
    /// </summary>
    public enum WarnungsStufe
    {
        Keine = 0,
        Vorabwarnung = 1,   // Gelb
        Markant = 2,        // Orange
        Unwetter = 3,       // Rot
        ExtremesUnwetter = 4 // Dunkelrot/Violett
    }
}
