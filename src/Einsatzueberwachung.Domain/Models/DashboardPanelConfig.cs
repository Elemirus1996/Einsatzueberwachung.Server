namespace Einsatzueberwachung.Domain.Models
{
    public class DashboardPanelConfig
    {
        public string PanelId { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public static class KnownPanels
    {
        public const string EinsatzInfo  = "einsatz-info";
        public const string Teams        = "teams";
        public const string Notizen      = "notizen";
        public const string Wetter       = "wetter";
        public const string Suchgebiete  = "suchgebiete";
        public const string Vermissten   = "vermissten";
        public const string Ressourcen   = "ressourcen";

        public static readonly Dictionary<string, string> Labels = new()
        {
            [EinsatzInfo]  = "Einsatz-Info",
            [Teams]        = "Teams",
            [Notizen]      = "Notizen & Funk",
            [Wetter]       = "Wetter",
            [Suchgebiete]  = "Suchgebiete",
            [Vermissten]   = "Vermissteninfo",
            [Ressourcen]   = "Ressourcen",
        };

        /// <summary>Feste Reihenfolge der Panels im Layout (beeinflusst Panel-Picker-Reihenfolge).</summary>
        public static readonly string[] FixedOrder =
        [
            EinsatzInfo, Vermissten, Wetter, Teams, Ressourcen, Suchgebiete, Notizen
        ];
    }
}
