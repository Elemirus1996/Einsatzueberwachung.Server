using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>Mitteilungen fuer die Einsatzleitung im Hintergrund ohne Push (#79, docs/APP_API.md native EL Abschnitt 11)</summary>
public sealed partial class ElAppController
{
    [HttpGet("mitteilungen")]
    public IActionResult GetMitteilungen([FromQuery] string? seit)
    {
        if (publicAccess.IsPublicRequest(HttpContext) || mitteilungen is null) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _)) return Gone();
        var (stand, liste) = mitteilungen.Seit(seit);
        return Ok(new
        {
            stand,
            mitteilungen = liste.Select(m => new { id = m.Id, art = m.Art, stufe = m.Stufe, teamId = m.TeamId, text = m.Text, zeit = m.Zeit })
        });
    }
}
