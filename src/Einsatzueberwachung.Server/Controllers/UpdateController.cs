using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class UpdateController : ControllerBase
{
    private readonly GitHubUpdateService _updateService;

    public UpdateController(GitHubUpdateService updateService)
    {
        _updateService = updateService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(_updateService.GetStatusSnapshot());
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check()
    {
        var result = await _updateService.CheckForUpdatesAsync();
        if (!result.Success)
        {
            return StatusCode(502, result);
        }

        return Ok(result);
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install(CancellationToken cancellationToken)
    {
        var result = await _updateService.InstallLatestAsync(cancellationToken);
        if (!result.Success)
        {
            return StatusCode(500, result);
        }

        return Ok(result);
    }
}
