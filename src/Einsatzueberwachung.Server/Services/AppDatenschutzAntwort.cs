using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Datenschutzerklaerung der Staffel in den Antworten der App-Schnittstelle (#148, docs/APP_API.md Team-Rolle Abschnitt 12)</summary>
public static class AppDatenschutzAntwort
{
    /// <summary>Ganzes Objekt oder null, wenn die Staffel nichts hinterlegt hat</summary>
    public static async Task<object?> GanzAsync(ISettingsService? einstellungen) =>
        einstellungen is null ? null : Ganz(await einstellungen.GetAppSettingsAsync());

    public static object? Ganz(AppSettings? s)
    {
        if (s is null || AppDatenschutz.Version(s) is not { } version) return null;
        return new
        {
            version,
            titel = string.IsNullOrWhiteSpace(s.AppDatenschutzTitel) ? "Datenschutzerklärung" : s.AppDatenschutzTitel.Trim(),
            text = s.AppDatenschutzText!.Trim(),
            stand = s.AppDatenschutzStandUtc is { } stand ? DateTime.SpecifyKind(stand, DateTimeKind.Utc) : (DateTime?)null
        };
    }

    /// <summary>Nur die Version, damit die App eine neue Fassung bemerkt</summary>
    public static async Task<object?> VersionAsync(ISettingsService? einstellungen)
    {
        if (einstellungen is null) return null;
        var version = AppDatenschutz.Version(await einstellungen.GetAppSettingsAsync());
        return version is null ? null : new { version };
    }

    /// <summary>Vorlage fuer die Einstellungen (Knopf „Vorlage einfuegen“); Platzhalter in eckigen Klammern fuellt die Staffel aus</summary>
    public const string VorlageTitel = "Datenschutzerklärung zur App EinsatzSpur";

    public const string Vorlage = """
        > Entwurf, keine Rechtsberatung. Bitte alle Stellen in [eckigen Klammern] ausfüllen und den Text prüfen lassen.

        Stand: [Datum]

        ## 1. Wer ist verantwortlich?

        Die App EinsatzSpur sendet Daten ausschließlich an den Server unserer Staffel, mit dem sie verbunden wurde: als Team über den QR-Code der Einsatzleitung, als Einsatzleitung über die Adresse in unserem VPN. Verantwortlich ist:

        [Name der Staffel, Anschrift, E-Mail, Telefon; ggf. Datenschutzbeauftragte Person]

        Der Herausgeber der App erhält keine Daten aus der App. Es gibt keinen zentralen Server, kein Konto beim Herausgeber, keine Analyse und keine Absturzberichte an den Herausgeber.

        ## 2. Welche Daten die App wann sendet

        **Team (Hundeführer, Helfer)**

        - Standort (GPS) nur während einer Suche, gesammelt an unseren Server. In einer Kurzpause innerhalb der Suche bleibt die Ortung an; diese Positionen gelten nur als aktuelle Position, nicht als Laufweg.
        - Vor der ersten Suche und zwischen zwei Suchen nutzt die App nur eine grobe Ortung ohne GPS, damit das System sie nicht beendet. Diese Position wird nicht gesendet.
        - Akkustand des Geräts, Statusmeldungen (z. B. „im Suchgebiet eingetroffen“) und die Steuerung der Suche (Start, Pause, Ende).
        - Nach dem Ende des Einsatzes ortet und sendet die App nichts mehr; der Zugang ist dann ungültig.

        **Einsatzleitung**

        - Nur in unserem VPN, Anmeldung mit dem Staffel-Passwort.
        - Während eines Einsatzes oder einer Übung: Standort und Akkustand des Geräts sowie die Eingaben der Einsatzleitung (Notizen, Funk, Mitteilungen an Teams, Angaben zur vermissten Person, Start und Ende des Einsatzes). Ohne Einsatz keine Ortung.
        - Jede Aktion steht mit dem Gerätenamen im Protokoll des Servers.

        ## 3. Was unser Server speichert und wie lange

        - Aktuelle Position eines Team-Handys: während der Suche; bei „Suche beendet“ entfernt.
        - Position der Einsatzleitung: nur im Arbeitsspeicher, beim Einsatzende gelöscht.
        - Laufwege, Notizen, Statusmeldungen, Funk und Angaben zur vermissten Person: im Einsatz und danach im Archiv und im PDF-Bericht, Funk zusätzlich in der Funk-Datenbank. Aufbewahrung: [Frist, in den Einstellungen des Servers festgelegt]
        - Mitteilungen der Einsatzleitung an ein Team: höchstens 20 je Team, bis zum Einsatzende; nicht im Archiv.
        - Protokoll der Aktionen (ohne Positionen): nur im Arbeitsspeicher, höchstens 2000 Einträge, gelöscht beim Neustart.
        - Protokolle des Webservers (IP-Adresse, Zeit): [Einstellung unseres Servers]

        ## 4. Was auf dem Gerät bleibt

        - Zugang (Token) und Anmelde-Code des Einsatzes im Schlüsselbund (iOS) bzw. Keystore (Android); gelöscht beim Einsatzende oder mit „Vom Einsatz abmelden“. Bei der Einsatzleitung liegt dort auch der Gerätename; die interne Adresse des Servers bleibt nach dem Abmelden gespeichert, sie ist nicht geheim.
        - Kennungen gelesener Mitteilungen je Team, gelöscht beim Einsatzende.
        - Positionen sowie Statusmeldungen und Aktionen, die im Funkloch noch nicht gesendet wurden, bis zum Senden.
        - Name, Logo und Server-Kennung der Heimatstaffel; auch nicht gewählte Server-Kennungen, damit die App nicht erneut fragt.
        - Die Bestätigung dieser Erklärung je Server und Fassung, nur auf dem Gerät; unser Server führt kein Protokoll darüber.
        - Nur Android: zwischengespeicherte Kartenkacheln, höchstens 200 MB, nur Kartenbilder.

        ## 5. Berechtigungen

        - Standort „beim Verwenden der App“: für die Ortung während einer Suche bzw. eines Einsatzes.
        - Kamera: nur zum Scannen des QR-Codes; es werden keine Bilder gespeichert oder gesendet.
        - Mitteilungen: Hinweise der Einsatzleitung und Warnungen auf dem Sperrbildschirm, erzeugt auf dem Gerät, ohne Push-Dienst eines Dritten.
        - Hintergrundaktualisierung (iOS) und Vibration (Android): damit die Ortung während der Suche weiterläuft und Warnungen spürbar sind. Warnungen der Einsatzleitung erklingen in der geöffneten App als Ton bzw. Vibration.
        - Android zeigt während der Ortung eine dauerhafte Benachrichtigung (Vordergrunddienst); das schreibt das System vor.

        ## 6. Keine Analyse, keine Werbung, keine Weitergabe

        Die App enthält keine Analyse- oder Werbedienste und gibt keine Daten an Dritte weiter.

        ## 7. Kartendienste

        Zum Anzeigen der Karte lädt die App Kartenbilder direkt vom Anbieter; dabei erhält er die IP-Adresse und den Kartenausschnitt, aber keine Angaben zum Einsatz oder zur Person.

        - iOS: Apple Karten – [Datenschutzrichtlinie von Apple](https://www.apple.com/de/legal/privacy/)
        - Android: OpenStreetMap – [Datenschutzrichtlinie der OSMF](https://osmfoundation.org/wiki/Privacy_Policy); OpenTopoMap – [Angaben von OpenTopoMap](https://opentopomap.org/about). Unter Android sendet die App dabei eine Kennung mit App-Name und Version (User-Agent), wie OpenStreetMap es verlangt.

        ## 8. Rechtsgrundlage

        [Von der Staffel festzulegen, z. B. Art. 6 Abs. 1 lit. b oder f DSGVO; für Angaben zur vermissten Person ggf. Art. 9 Abs. 2 lit. c DSGVO]

        ## 9. Ihre Rechte

        Sie haben uns gegenüber das Recht auf Auskunft, Berichtigung, Löschung, Einschränkung der Verarbeitung, Datenübertragbarkeit und Widerspruch (Art. 15–21 DSGVO) sowie das Recht auf Beschwerde bei einer Aufsichtsbehörde (Art. 77 DSGVO), z. B. bei [zuständige Aufsichtsbehörde]. Anfragen bitte an uns (Abschnitt 1), nicht an den Herausgeber der App.
        """;
}
