namespace Einsatzueberwachung.Domain.Models;

public sealed record EinsatzKontaktDaten(
    string PolizeiKontaktName,
    string PolizeiDienstnummer,
    string PolizeiTelefon,
    bool PolizeiVermisstenmeldungAufgenommen,
    bool PolizeiKoordinationBesprochen,
    bool PolizeiSuchabschnittAbgestimmt,
    bool PolizeiRueckmeldepflichtVereinbart,
    bool PolizeiDatenschutzGeklaert,
    string BosEinheit,
    string BosZugfuehrer,
    string BosFunkrufname,
    string BosAufgabenteilung,
    bool BosAbschnittAbgestimmt,
    bool BosRessourcenBesprochen)
{
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(PolizeiKontaktName)
        || !string.IsNullOrWhiteSpace(PolizeiDienstnummer)
        || !string.IsNullOrWhiteSpace(PolizeiTelefon)
        || PolizeiVermisstenmeldungAufgenommen
        || PolizeiKoordinationBesprochen
        || PolizeiSuchabschnittAbgestimmt
        || PolizeiRueckmeldepflichtVereinbart
        || PolizeiDatenschutzGeklaert
        || !string.IsNullOrWhiteSpace(BosEinheit)
        || !string.IsNullOrWhiteSpace(BosZugfuehrer)
        || !string.IsNullOrWhiteSpace(BosFunkrufname)
        || !string.IsNullOrWhiteSpace(BosAufgabenteilung)
        || BosAbschnittAbgestimmt
        || BosRessourcenBesprochen;

    public static EinsatzKontaktDaten From(EinsatzData einsatz) => new(
        einsatz.PolizeiKontaktName ?? string.Empty,
        einsatz.PolizeiDienstnummer ?? string.Empty,
        einsatz.PolizeiTelefon ?? string.Empty,
        einsatz.PolizeiVermisstenmeldungAufgenommen,
        einsatz.PolizeiKoordinationBesprochen,
        einsatz.PolizeiSuchabschnittAbgestimmt,
        einsatz.PolizeiRueckmeldepflichtVereinbart,
        einsatz.PolizeiDatenschutzGeklaert,
        einsatz.BosEinheit ?? string.Empty,
        einsatz.BosZugfuehrer ?? string.Empty,
        einsatz.BosFunkrufname ?? string.Empty,
        einsatz.BosAufgabenteilung ?? string.Empty,
        einsatz.BosAbschnittAbgestimmt,
        einsatz.BosRessourcenBesprochen);

    public static EinsatzKontaktDaten From(ArchivedEinsatz einsatz) => new(
        einsatz.PolizeiKontaktName ?? string.Empty,
        einsatz.PolizeiDienstnummer ?? string.Empty,
        einsatz.PolizeiTelefon ?? string.Empty,
        einsatz.PolizeiVermisstenmeldungAufgenommen,
        einsatz.PolizeiKoordinationBesprochen,
        einsatz.PolizeiSuchabschnittAbgestimmt,
        einsatz.PolizeiRueckmeldepflichtVereinbart,
        einsatz.PolizeiDatenschutzGeklaert,
        einsatz.BosEinheit ?? string.Empty,
        einsatz.BosZugfuehrer ?? string.Empty,
        einsatz.BosFunkrufname ?? string.Empty,
        einsatz.BosAufgabenteilung ?? string.Empty,
        einsatz.BosAbschnittAbgestimmt,
        einsatz.BosRessourcenBesprochen);
}