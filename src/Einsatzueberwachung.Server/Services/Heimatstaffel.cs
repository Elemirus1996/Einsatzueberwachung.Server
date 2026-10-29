using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Feste Kennung dieses Servers fuer die App (#121): einmal erzeugt, im Datenordner gespeichert, aendert sich nicht bei neuer
/// Adresse oder neuem Staffelnamen. Nicht geheim. Bewusst nicht in den Einstellungen, damit ein Einstellungs-Import auf einem
/// anderen Server die Kennung nicht mitkopiert.
/// </summary>
public sealed class ServerKennung
{
    public const string DateiName = "server-kennung.txt";

    public ServerKennung(string datenOrdner)
    {
        var pfad = Path.Combine(datenOrdner, DateiName);
        if (File.Exists(pfad) && Guid.TryParse(File.ReadAllText(pfad).Trim(), out var vorhanden))
        {
            Kennung = vorhanden.ToString();
            return;
        }
        Kennung = Guid.NewGuid().ToString();
        Directory.CreateDirectory(datenOrdner);
        File.WriteAllText(pfad, Kennung);
    }

    public static ServerKennung ForDataDirectory() => new(AppPathResolver.GetDataDirectory());

    public string Kennung { get; }
}

/// <summary>
/// Staffellogo fuer die App als PNG (hoechstens 512 × 512). PNG, JPG und WEBP werden umgerechnet, SVG wird gerastert
/// (Svg.Skia) – vorher bereinigt: keine Skripte, keine Ereignis-Attribute, keine externen Verweise, keine DTD.
/// Das Ergebnis wird zwischengespeichert und nur bei geaendertem Logo neu erzeugt.
/// </summary>
public sealed class AppLogo(ISettingsService settings)
{
    public const int MaxKante = 512;
    private readonly SemaphoreSlim _sperre = new(1, 1);
    private (string Pfad, DateTime Geaendert, string Version, byte[] Png)? _zwischenspeicher;

    public sealed record Logo(string Version, byte[] Png);

    /// <summary>Das Logo fuer die App oder null (keins hinterlegt, Datei fehlt, SVG oder nicht lesbar)</summary>
    public async Task<Logo?> HolenAsync()
    {
        var pfad = (await settings.GetStaffelSettingsAsync())?.StaffelLogoPfad;
        if (string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad)) return null;

        var geaendert = File.GetLastWriteTimeUtc(pfad);
        await _sperre.WaitAsync();
        try
        {
            if (_zwischenspeicher is { } z && z.Pfad == pfad && z.Geaendert == geaendert)
                return new Logo(z.Version, z.Png);

            var original = await File.ReadAllBytesAsync(pfad);
            var png = IstSvg(pfad) ? SvgAlsPng(original) : AlsPng(original);
            if (png is null) return null;
            var version = Convert.ToHexString(SHA256.HashData(original))[..16].ToLowerInvariant();
            _zwischenspeicher = (pfad, geaendert, version, png);
            return new Logo(version, png);
        }
        finally
        {
            _sperre.Release();
        }
    }

    public static bool IstSvg(string pfad) => Path.GetExtension(pfad).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>SVG bereinigen und auf hoechstens 512 × 512 rastern; null, wenn es nicht lesbar ist</summary>
    public static byte[]? SvgAlsPng(byte[] svg)
    {
        var sauber = SvgBereinigen(svg);
        if (sauber is null) return null;
        try
        {
            using var dokument = new Svg.Skia.SKSvg();
            using var bild = dokument.FromSvg(sauber);
            if (bild is null) return null;
            var rahmen = bild.CullRect;
            if (rahmen.Width <= 0 || rahmen.Height <= 0) return null;

            var faktor = (float)MaxKante / Math.Max(rahmen.Width, rahmen.Height);
            var breite = Math.Max(1, (int)Math.Round(rahmen.Width * faktor));
            var hoehe = Math.Max(1, (int)Math.Round(rahmen.Height * faktor));
            using var flaeche = SKSurface.Create(new SKImageInfo(breite, hoehe, SKColorType.Rgba8888, SKAlphaType.Premul));
            var leinwand = flaeche.Canvas;
            leinwand.Clear(SKColors.Transparent);
            leinwand.Scale(faktor);
            leinwand.Translate(-rahmen.Left, -rahmen.Top);
            leinwand.DrawPicture(bild);
            using var ergebnis = flaeche.Snapshot();
            using var png = ergebnis.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        catch (Exception)
        {
            return null; // unlesbares SVG → wie kein Logo
        }
    }

    /// <summary>
    /// Entfernt aus einem SVG alles, was ausgefuehrt oder nachgeladen werden koennte: DTD/Entitaeten (XXE), script,
    /// foreignObject, on…-Attribute, Verweise ausser „#…“ und „data:image/…“, CSS mit @import oder url(…) nach aussen.
    /// </summary>
    public static string? SvgBereinigen(byte[] svg)
    {
        XDocument doc;
        try
        {
            var einstellungen = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 };
            using var leser = XmlReader.Create(new MemoryStream(svg), einstellungen);
            doc = XDocument.Load(leser);
        }
        catch (XmlException)
        {
            return null;
        }
        if (doc.Root is null || doc.Root.Name.LocalName != "svg") return null;

        static bool Intern(string wert) =>
            wert.TrimStart().StartsWith('#') || wert.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
        static bool ExterneUrl(string wert) =>
            wert.Contains("@import", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(wert, @"url\(\s*['""]?(?!#)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName is "script" or "foreignObject").ToList())
            element.Remove();
        foreach (var stil in doc.Descendants().Where(e => e.Name.LocalName == "style" && ExterneUrl(e.Value)).ToList())
            stil.Remove();
        foreach (var attribut in doc.Descendants().SelectMany(e => e.Attributes()).ToList())
        {
            var name = attribut.Name.LocalName;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || (name == "href" && !Intern(attribut.Value))
                || ExterneUrl(attribut.Value))
                attribut.Remove();
        }
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Rastergrafik auf hoechstens 512 × 512 verkleinern (Seitenverhaeltnis bleibt) und als PNG ausgeben</summary>
    public static byte[]? AlsPng(byte[] bild)
    {
        // SKBitmap.Decode wirft bei unlesbaren Daten – erst pruefen, ob es ein bekanntes Bildformat ist
        using var codec = SKCodec.Create(new SKMemoryStream(bild));
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;

        var faktor = Math.Min(1.0, (double)MaxKante / Math.Max(bitmap.Width, bitmap.Height));
        var breite = Math.Max(1, (int)Math.Round(bitmap.Width * faktor));
        var hoehe = Math.Max(1, (int)Math.Round(bitmap.Height * faktor));

        using var ziel = faktor < 1.0
            ? bitmap.Resize(new SKImageInfo(breite, hoehe, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : bitmap.Copy();
        if (ziel is null) return null;
        using var bildDaten = SKImage.FromBitmap(ziel);
        using var png = bildDaten.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}

/// <summary>Gemeinsame Antwort von GET /logo (Team und Einsatzleitung)</summary>
public static class AppLogoAntwort
{
    public static async Task<Microsoft.AspNetCore.Mvc.IActionResult> ErstellenAsync(AppLogo appLogo, HttpRequest anfrage, HttpResponse antwort)
    {
        var logo = await appLogo.HolenAsync();
        if (logo is null) return new Microsoft.AspNetCore.Mvc.NotFoundObjectResult(new { error = "Kein Logo für die App hinterlegt (PNG oder JPG)." });

        var etag = $"\"{logo.Version}\"";
        antwort.Headers.ETag = etag;
        antwort.Headers.CacheControl = "private, max-age=86400";
        if (anfrage.Headers.IfNoneMatch.ToString().Split(',').Any(e => e.Trim() == etag))
            return new Microsoft.AspNetCore.Mvc.StatusCodeResult(StatusCodes.Status304NotModified);
        return new Microsoft.AspNetCore.Mvc.FileContentResult(logo.Png, "image/png");
    }
}
