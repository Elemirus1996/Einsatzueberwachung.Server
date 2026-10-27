using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/truemmer")]
public class TruemmerController : ControllerBase
{
    private readonly IEinsatzService _einsatzService;
    private readonly TruemmerKartenSpeicher _speicher;

    public TruemmerController(IEinsatzService einsatzService, TruemmerKartenSpeicher speicher)
    {
        _einsatzService = einsatzService;
        _speicher = speicher;
    }

    [HttpPost("karten")]
    [RequestSizeLimit(TruemmerKartenSpeicher.MaxImageBytes + 1024)]
    public async Task<IActionResult> UploadKarte([FromForm] IFormFile file, [FromForm] string? title)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei übermittelt." });
        if (file.Length > TruemmerKartenSpeicher.MaxImageBytes)
            return BadRequest(new { error = $"Datei zu groß (max. {TruemmerKartenSpeicher.MaxImageBytes / (1024 * 1024)} MB)." });

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        var (karte, fehler) = await _speicher.SpeichernAsync(memory.ToArray(), file.FileName, title);
        return karte is null ? BadRequest(new { error = fehler }) : Ok(karte);
    }

    [HttpGet("karten/{id:guid}/image")]
    public IActionResult GetImage(Guid id)
    {
        var karte = _einsatzService.CurrentEinsatz.TruemmerKarten?.FirstOrDefault(k => k.Id == id);
        if (karte is null) return NotFound();

        var fullPath = Path.Combine(AppPathResolver.GetDataDirectory(), karte.ImageRelativePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(fullPath, mime);
    }

    [HttpDelete("karten/{id:guid}")]
    public async Task<IActionResult> DeleteKarte(Guid id) =>
        await _speicher.LoeschenAsync(id) ? NoContent() : NotFound();
}
