using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>QR-Code für die Anmeldung der EL-App (Idee 6.3): nur die interne HTTPS-Adresse mit /el</summary>
public sealed class ElAppQrTests
{
    [Theory]
    [InlineData("https://einsatz.intern/", "https://einsatz.intern/el")]
    [InlineData("https://Einsatz.Intern/einstellungen?tab=system", "https://einsatz.intern/el")]
    [InlineData("https://el.dein-einsatz.mywire.org:8443/", "https://el.dein-einsatz.mywire.org:8443/el")]
    public void Inhalt_HttpsAdresseMitEl(string basis, string erwartet) => Assert.Equal(erwartet, ElAppQr.Inhalt(basis));

    [Theory]
    [InlineData("http://einsatz.intern/")]
    [InlineData("kein Link")]
    public void Inhalt_OhneHttps_Null(string basis) => Assert.Null(ElAppQr.Inhalt(basis));

    [Theory]
    [InlineData("https://10.10.0.1/")]
    [InlineData("https://10.10.0.1:8443/einstellungen")]
    [InlineData("https://[fd00::1]/")]
    public void Inhalt_IpAdresse_KeinCode(string basis)
    {
        // Das Zertifikat gilt nur für den Namen: Die App bekäme -1202 (Board-Karte el-qr-ip, 15.11.)
        Assert.Null(ElAppQr.Inhalt(basis));
        Assert.True(ElAppQr.IstIpAdresse(basis));
    }

    [Fact]
    public void Name_IstKeineIpAdresse() => Assert.False(ElAppQr.IstIpAdresse("https://einsatz.intern/"));
}
