namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed class MonitorEditEinsatzForm
{
    public bool IstEinsatz { get; set; } = true;
    public string EinsatzNummer { get; set; } = string.Empty;
    public string PolizeiKontaktName { get; set; } = string.Empty;
    public string PolizeiDienstnummer { get; set; } = string.Empty;
    public string PolizeiTelefon { get; set; } = string.Empty;
    public bool PolizeiVermisstenmeldungAufgenommen { get; set; }
    public bool PolizeiKoordinationBesprochen { get; set; }
    public bool PolizeiSuchabschnittAbgestimmt { get; set; }
    public bool PolizeiRueckmeldepflichtVereinbart { get; set; }
    public bool PolizeiDatenschutzGeklaert { get; set; }
    public string BosEinheit { get; set; } = string.Empty;
    public string BosZugfuehrer { get; set; } = string.Empty;
    public string BosFunkrufname { get; set; } = string.Empty;
    public string BosAufgabenteilung { get; set; } = string.Empty;
    public bool BosAbschnittAbgestimmt { get; set; }
    public bool BosRessourcenBesprochen { get; set; }
    public string AlarmierungsZeit { get; set; } = string.Empty;
    public string Einsatzort { get; set; } = string.Empty;
    public string MapAddress { get; set; } = string.Empty;
    public string Alarmiert { get; set; } = string.Empty;
    public int AnzahlTeams { get; set; }
    public string Einsatzleiter { get; set; } = string.Empty;
    public string Fuehrungsassistent { get; set; } = string.Empty;
    public string Bemerkungen { get; set; } = string.Empty;
}
