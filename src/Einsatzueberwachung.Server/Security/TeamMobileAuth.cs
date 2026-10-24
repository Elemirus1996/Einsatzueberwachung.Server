using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Einsatzueberwachung.Server.Security;

public static class TeamMobileAuth
{
    public const string AuthenticationScheme = "TeamMobile";
    public const string AuthorizationPolicy = "TeamMobileOnly";
    public const string CookieName = "einsatz.team.auth";
    public const string TeamIdClaim = "team-id";
    public const string GenerationClaim = "team-generation";
    /// <summary>Signierter Cookie-Wert aus <c>ITeamMobileTokenService.IssueTeamCookieValue</c>, wird bei jeder Anfrage geprueft.</summary>
    public const string CookieValueClaim = "team-cookie";

    /// <summary>
    /// Bei jeder Anfrage pruefen, ob das Team-Cookie noch zum laufenden Einsatz gehoert und das Team noch existiert.
    /// Nach Einsatzende oder bei einem neuen Einsatz ist es damit sofort ungueltig (Audit 2026-10-24).
    /// </summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var tokenService = ctx.HttpContext.RequestServices.GetRequiredService<ITeamMobileTokenService>();
        var einsatzService = ctx.HttpContext.RequestServices.GetRequiredService<IEinsatzService>();
        var cookieValue = ctx.Principal?.FindFirst(CookieValueClaim)?.Value ?? string.Empty;
        var claimedTeamId = ctx.Principal?.FindFirst(TeamIdClaim)?.Value;

        if (!tokenService.TryValidateTeamCookie(cookieValue, out var teamId)
            || teamId != claimedTeamId
            || !einsatzService.Teams.Any(t => t.TeamId == teamId))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AuthenticationScheme);
        }
    }
}

public sealed class TeamMobileOptions
{
    public const string SectionName = "TeamMobile";

    /// <summary>
    /// Öffentliche Basis-URL der Mobile-Ansicht (z.B. https://dein-einsatz.mywire.org).
    /// Wenn leer, wird die URL aus dem Request abgeleitet.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
