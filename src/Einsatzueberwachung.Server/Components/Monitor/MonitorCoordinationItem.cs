namespace Einsatzueberwachung.Server.Components.Monitor;

public enum MonitorCoordinationItemId
{
    PoliceVermisstenmeldungAufgenommen,
    PoliceKoordinationBesprochen,
    PoliceSuchabschnittAbgestimmt,
    PoliceRueckmeldepflichtVereinbart,
    PoliceDatenschutzGeklaert,
    BosAbschnittAbgestimmt,
    BosRessourcenBesprochen
}

public sealed record MonitorCoordinationItem(
    MonitorCoordinationItemId Id,
    string Label,
    bool IsBos,
    bool IsConfirmed);

public sealed record MonitorCoordinationToggle(MonitorCoordinationItemId Id, bool IsConfirmed);
