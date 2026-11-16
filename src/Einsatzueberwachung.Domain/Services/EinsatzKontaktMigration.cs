using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services;

internal static class EinsatzKontaktMigration
{
    internal static void MigrateLegacyContacts(EinsatzData einsatz)
    {
        if (einsatz.EinsatzkontakteMigriert)
            return;

        var legacyContacts = einsatz.Vermisste ?? new List<VermisstenInfo>();

        string FirstNonEmpty(string current, Func<VermisstenInfo, string> legacyValue) =>
            !string.IsNullOrWhiteSpace(current)
                ? current
                : legacyContacts.Select(legacyValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                  ?? string.Empty;

        bool IsConfirmed(bool current, Func<VermisstenInfo, bool> legacyValue) =>
            current || legacyContacts.Any(legacyValue);

        einsatz.PolizeiKontaktName = FirstNonEmpty(einsatz.PolizeiKontaktName, person => person.PolizeiKontaktName);
        einsatz.PolizeiDienstnummer = FirstNonEmpty(einsatz.PolizeiDienstnummer, person => person.PolizeiDienstnummer);
        einsatz.PolizeiTelefon = FirstNonEmpty(einsatz.PolizeiTelefon, person => person.PolizeiTelefon);
        einsatz.PolizeiVermisstenmeldungAufgenommen = IsConfirmed(einsatz.PolizeiVermisstenmeldungAufgenommen, person => person.PolizeiVermisstenmeldungAufgenommen);
        einsatz.PolizeiKoordinationBesprochen = IsConfirmed(einsatz.PolizeiKoordinationBesprochen, person => person.PolizeiKoordinationBesprochen);
        einsatz.PolizeiSuchabschnittAbgestimmt = IsConfirmed(einsatz.PolizeiSuchabschnittAbgestimmt, person => person.PolizeiSuchabschnittAbgestimmt);
        einsatz.PolizeiRueckmeldepflichtVereinbart = IsConfirmed(einsatz.PolizeiRueckmeldepflichtVereinbart, person => person.PolizeiRueckmeldepflichtVereinbart);
        einsatz.PolizeiDatenschutzGeklaert = IsConfirmed(einsatz.PolizeiDatenschutzGeklaert, person => person.PolizeiDatenschutzGeklaert);
        einsatz.BosEinheit = FirstNonEmpty(einsatz.BosEinheit, person => person.BosEinheit);
        einsatz.BosZugfuehrer = FirstNonEmpty(einsatz.BosZugfuehrer, person => person.BosZugfuehrer);
        einsatz.BosFunkrufname = FirstNonEmpty(einsatz.BosFunkrufname, person => person.BosFunkrufname);
        einsatz.BosAufgabenteilung = FirstNonEmpty(einsatz.BosAufgabenteilung, person => person.BosAufgabenteilung);
        einsatz.BosAbschnittAbgestimmt = IsConfirmed(einsatz.BosAbschnittAbgestimmt, person => person.BosAbschnittAbgestimmt);
        einsatz.BosRessourcenBesprochen = IsConfirmed(einsatz.BosRessourcenBesprochen, person => person.BosRessourcenBesprochen);
        einsatz.EinsatzkontakteMigriert = true;
    }
}
