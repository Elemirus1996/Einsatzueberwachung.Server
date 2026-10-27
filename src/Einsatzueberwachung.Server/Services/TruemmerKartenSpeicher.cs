using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Speichert und loescht Truemmer-Lagebilder. Gemeinsam fuer API (TruemmerController) und die Seite /truemmer –
/// die Seite rief frueher die eigene API ueber die Browser-Adresse auf und scheiterte damit am Zertifikat
/// (VPN-Zertifikat, dem der Server selbst nicht vertraut, oder abgelaufen).
/// </summary>
public sealed class TruemmerKartenSpeicher
{
    public const long MaxImageBytes = 20 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IEinsatzService _einsatzService;

    public TruemmerKartenSpeicher(IEinsatzService einsatzService)
    {
        _einsatzService = einsatzService;
    }

    /// <summary>Prueft und speichert ein Bild. Liefert die Karte oder eine Fehlermeldung fuer die Anzeige.</summary>
    public async Task<(TruemmerKarte? Karte, string? Fehler)> SpeichernAsync(byte[] bytes, string fileName, string? title)
    {
        if (bytes.Length == 0)
            return (null, "Keine Datei übermittelt.");

        if (bytes.Length > MaxImageBytes)
            return (null, $"Datei zu groß (max. {MaxImageBytes / (1024 * 1024)} MB).");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (null, "Nur JPG, PNG oder WEBP erlaubt.");

        // Magic-Bytes + Codec pruefen, BEVOR auf Disk geschrieben wird — keine getarnten Dateien auf der Platte
        if (!HasValidImageMagicBytes(bytes, ext))
            return (null, "Bildinhalt passt nicht zur Dateiendung.");

        int width, height;
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(memory);
            if (codec is null)
                return (null, "Bilddatei konnte nicht gelesen werden.");
            width = codec.Info.Width;
            height = codec.Info.Height;
        }
        catch
        {
            return (null, "Bilddatei ungültig.");
        }

        var einsatzId = ResolveEinsatzId();
        var dir = Path.Combine(AppPathResolver.GetTruemmerDirectory(), einsatzId);
        Directory.CreateDirectory(dir);

        var karteId = Guid.NewGuid();
        var storedName = karteId.ToString() + ext;
        await File.WriteAllBytesAsync(Path.Combine(dir, storedName), bytes);

        var karte = new TruemmerKarte
        {
            Id = karteId,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title!,
            ImageRelativePath = Path.Combine("truemmer", einsatzId, storedName).Replace('\\', '/'),
            ImageWidthPx = width,
            ImageHeightPx = height,
            UploadedAt = Ortszeit.Jetzt
        };
        await _einsatzService.AddTruemmerKarteAsync(karte);
        return (karte, null);
    }

    /// <summary>Loescht Bild und Karte. false, wenn es die Karte nicht gibt.</summary>
    public async Task<bool> LoeschenAsync(Guid id)
    {
        var karte = _einsatzService.CurrentEinsatz.TruemmerKarten?.FirstOrDefault(k => k.Id == id);
        if (karte is null) return false;

        var fullPath = Path.Combine(AppPathResolver.GetDataDirectory(), karte.ImageRelativePath);
        if (File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch { /* swallow */ }
        }

        await _einsatzService.RemoveTruemmerKarteAsync(id);
        return true;
    }

    private string ResolveEinsatzId()
    {
        // Aktueller Einsatz hat keinen eigenen Identifier — wir nutzen die EinsatzNummer falls vorhanden,
        // sonst eine stabile Datums-Repräsentation der Alarmierungszeit.
        var einsatz = _einsatzService.CurrentEinsatz;
        if (!string.IsNullOrWhiteSpace(einsatz.EinsatzNummer))
            return SanitizeFolderName(einsatz.EinsatzNummer);
        if (einsatz.AlarmierungsZeit.HasValue)
            return einsatz.AlarmierungsZeit.Value.ToString("yyyyMMddHHmm");
        return einsatz.EinsatzDatum.ToString("yyyyMMddHHmm");
    }

    private static string SanitizeFolderName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = raw.Where(c => !invalid.Contains(c)).ToArray();
        return new string(arr);
    }

    private static bool HasValidImageMagicBytes(byte[] bytes, string extension)
    {
        if (bytes.Length < 12) return false;

        // JPEG: FF D8 FF
        bool isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        bool isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                  && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        // WEBP: 'RIFF' .... 'WEBP'
        bool isWebp = bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                   && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

        return extension switch
        {
            ".jpg" or ".jpeg" => isJpeg,
            ".png" => isPng,
            ".webp" => isWebp,
            _ => false
        };
    }
}
