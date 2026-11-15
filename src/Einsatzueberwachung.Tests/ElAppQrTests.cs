using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>QR-Code für die Anmeldung der EL-App (Idee 6.3): nur die interne HTTPS-Adresse mit /el</summary>
public sealed class ElAppQrTests
{
    [Theory]
    [InlineData("https://einsatz.intern/", "https://einsatz.intern/el")]
    [InlineData("https://Einsatz.Intern/einstellungen?tab=system", "https://einsatz.intern/el")]
    [InlineData("https://10.10.0.1:8443/", "https://10.10.0.1:8443/el")]
    public void Inhalt_HttpsAdresseMitEl(string basis, string erwartet) => Assert.Equal(erwartet, ElAppQr.Inhalt(basis));

    [Theory]
    [InlineData("http://einsatz.intern/")]
    [InlineData("kein Link")]
    public void Inhalt_OhneHttps_Null(string basis) => Assert.Null(ElAppQr.Inhalt(basis));
}
