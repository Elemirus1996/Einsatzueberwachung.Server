namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// QR-Code für die Anmeldung der EL-App (Idee 6.3, docs/APP_API.md „native App“ Abschnitt 4): nur die interne Adresse
/// mit <c>/el</c>, ohne Passwort und ohne Token. Die App fragt danach das Staffel-Passwort ab.
/// </summary>
public static class ElAppQr
{
    /// <summary>„https://host[:port]/el“ aus der Adresse, unter der die Einstellungen geöffnet sind; null ohne HTTPS</summary>
    public static string? Inhalt(string basisAdresse)
    {
        if (!Uri.TryCreate(basisAdresse, UriKind.Absolute, out var basis) || basis.Scheme != Uri.UriSchemeHttps) return null;
        return new UriBuilder(Uri.UriSchemeHttps, basis.Host, basis.IsDefaultPort ? -1 : basis.Port, "el").Uri.ToString();
    }
}
