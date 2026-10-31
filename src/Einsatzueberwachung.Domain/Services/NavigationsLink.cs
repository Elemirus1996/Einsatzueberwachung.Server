using System;
using System.Globalization;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Links fuer QR-Codes, mit denen ein Team einen Kartenpunkt in der Navigation seines Handys oeffnet.
    /// Koordinaten immer mit Punkt als Dezimaltrenner und 6 Nachkommastellen (ca. 10 cm).
    /// </summary>
    public static class NavigationsLink
    {
        /// <summary>
        /// <c>geo:</c>-URI (RFC 5870): Android bietet alle installierten Navi-Apps zur Auswahl an, funktioniert auch offline.
        /// Die Bezeichnung steht in <c>q=lat,lng(Bezeichnung)</c> und erscheint als Name des Ziels.
        /// </summary>
        public static string Geo(double lat, double lng, string? bezeichnung = null)
        {
            var punkt = Koordinaten(lat, lng);
            var name = Bereinigen(bezeichnung);
            return name is null
                ? $"geo:{punkt}?q={punkt}"
                : $"geo:{punkt}?q={punkt}({Uri.EscapeDataString(name)})";
        }

        /// <summary>Link auf Apple Karten: das iPhone oeffnet ihn aus der Kamera direkt in Apple Karten.</summary>
        public static string AppleKarten(double lat, double lng, string? bezeichnung = null)
        {
            var punkt = Koordinaten(lat, lng);
            var name = Bereinigen(bezeichnung);
            return name is null
                ? $"https://maps.apple.com/?ll={punkt}&q={punkt}"
                : $"https://maps.apple.com/?ll={punkt}&q={Uri.EscapeDataString(name)}";
        }

        private static string Koordinaten(double lat, double lng)
        {
            if (double.IsNaN(lat) || double.IsNaN(lng) || lat < -90 || lat > 90 || lng < -180 || lng > 180)
                throw new ArgumentOutOfRangeException(nameof(lat), "Koordinaten ausserhalb des gueltigen Bereichs.");
            return string.Create(CultureInfo.InvariantCulture, $"{lat:F6},{lng:F6}");
        }

        // Klammern wuerden die Bezeichnung im geo:-Link vorzeitig beenden; zu lange Namen machen den QR-Code unlesbar
        private static string? Bereinigen(string? bezeichnung)
        {
            var text = bezeichnung?.Replace('(', ' ').Replace(')', ' ').Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length > 60 ? text[..60] : text;
        }
    }
}
