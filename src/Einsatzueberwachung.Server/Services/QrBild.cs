using QRCoder;

namespace Einsatzueberwachung.Server.Services;

/// <summary>QR-Code als PNG-Data-URL fuer &lt;img src&gt; (Team-Anmeldung, VPN, Traccar-Test, Navigationspunkt).</summary>
public static class QrBild
{
    public static string DataUrl(string inhalt, int pixelProModul = 6)
    {
        using var generator = new QRCodeGenerator();
        using var daten = generator.CreateQrCode(inhalt, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(daten).GetGraphic(pixelProModul);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
