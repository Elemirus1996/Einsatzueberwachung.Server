using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components.Pages;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Extensions;
using Einsatzueberwachung.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// Hotfix 1.9.3 – Funde aus dem Audit vom 24.10.2026
// ------------------------------------------------------------

public class PublicAccessTests
{
    private static PublicAccess Configured(int port) => new(port);

    [Theory]
    [InlineData("/team")]
    [InlineData("/team/login")]
    [InlineData("/team/login/ABCDEF")]
    [InlineData("/api/team-mobile/location")]
    [InlineData("/hubs/team-mobile")]
    [InlineData("/_blazor")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/css/app.css")]
    [InlineData("/app.css")]
    [InlineData("/leaflet-interop.js")]
    [InlineData("/favicon.png")]
    public void OeffentlichePfade_SindErlaubt(string path) =>
        Assert.True(PublicAccess.IsPublicPath(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/einstellungen")]
    [InlineData("/einsatz-monitor")]
    [InlineData("/el")]
    [InlineData("/teams")]
    [InlineData("/team-x")]
    [InlineData("/api/einsatz/current")]
    [InlineData("/api/update/install")]
    [InlineData("/api/collarwebhook/location")]
    [InlineData("/hubs/einsatz")]
    [InlineData("/downloads/app-settings.json")]
    [InlineData("/downloads/data-backup.zip")]
    [InlineData("/downloads/einsatz-bericht.pdf")]
    [InlineData("/swagger/index.html")]
    [InlineData("/health")]
    [InlineData("/css/../downloads/x.css")]
    public void InternePfade_SindGesperrt(string path) =>
        Assert.False(PublicAccess.IsPublicPath(path));

    [Fact]
    public void NurTeamSeiten_DuerfenImOeffentlichenCircuitRendern()
    {
        Assert.True(PublicAccess.IsPublicPage(typeof(TeamLogin)));
        Assert.True(PublicAccess.IsPublicPage(typeof(TeamMobile)));
        Assert.False(PublicAccess.IsPublicPage(typeof(Einstellungen)));
        Assert.False(PublicAccess.IsPublicPage(typeof(EinsatzLeitung)));
        Assert.False(PublicAccess.IsPublicPage(null));
    }

    [Fact]
    public void OeffentlicheAnfrage_WirdAmLokalenPortErkannt()
    {
        var access = Configured(5001);
        Assert.True(access.IsPublicRequest(Context(5001, "/team")));
        Assert.False(access.IsPublicRequest(Context(5000, "/team")));
        Assert.False(access.IsPublicRequest(null));
        Assert.False(Configured(0).IsPublicRequest(Context(5001, "/team")));
    }

    [Theory]
    [InlineData("5001", 5001)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("70000", 0)]
    public void Port_KommtAusDerKonfiguration(string value, int expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PublicAccess.PortVariable] = value })
            .Build();
        Assert.Equal(expected, PublicAccess.FromConfiguration(config).Port);
    }

    [Theory]
    [InlineData(5001, "/einstellungen", 404, false)]
    [InlineData(5001, "/downloads/app-settings.json", 404, false)]
    [InlineData(5001, "/team/login", 200, true)]
    [InlineData(5000, "/einstellungen", 200, true)]
    public async Task Middleware_SperrtInternePfadeNurAmOeffentlichenPort(int localPort, string path, int status, bool nextCalled)
    {
        var called = false;
        var middleware = new PublicAccessMiddleware(_ => { called = true; return Task.CompletedTask; }, Configured(5001));
        var http = Context(localPort, path);

        await middleware.InvokeAsync(http);

        Assert.Equal(nextCalled, called);
        Assert.Equal(status, http.Response.StatusCode);
    }

    private static DefaultHttpContext Context(int localPort, string path)
    {
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = localPort;
        http.Request.Path = path;
        return http;
    }
}

public class CorsTests
{
    private static CorsPolicy Policy(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var provider = new ServiceCollection().AddCorsPolicies(config).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(ServiceCollectionExtensions.CorsPolicyName)!;
    }

    [Fact]
    public void OhneKonfiguration_IstKeineFremdeOriginErlaubt()
    {
        var policy = Policy([]);
        Assert.False(policy.IsOriginAllowed("https://boese.example"));
        Assert.False(policy.SupportsCredentials);
    }

    [Fact]
    public void EingetrageneOrigin_IstErlaubt_OhneCookies()
    {
        var policy = Policy(new() { ["Cors:AllowedOrigins:0"] = "https://partner.example" });
        Assert.True(policy.IsOriginAllowed("https://partner.example"));
        Assert.False(policy.IsOriginAllowed("https://boese.example"));
        Assert.False(policy.SupportsCredentials);
    }
}

public class SettingsSecretsTests
{
    [Fact]
    public void Download_EnthaeltKeineZugangsdaten()
    {
        var settings = new AppSettings { GitHubToken = "ghp_geheim", DiveraAccessKey = "divera-geheim", DiveraBaseUrl = "https://app.divera247.com" };

        var json = Encoding.UTF8.GetString(SettingsSecrets.SerializeWithoutSecrets(settings));

        Assert.DoesNotContain("ghp_geheim", json);
        Assert.DoesNotContain("divera-geheim", json);
        Assert.Contains("https://app.divera247.com", json);
    }

    [Fact]
    public void MaskJson_LeertNurZugangsdaten()
    {
        var input = Encoding.UTF8.GetBytes("{\"GitHubToken\":\"t\",\"DiveraAccessKey\":\"k\",\"IsDarkMode\":true,\"ThemePreset\":\"ruhr\"}");

        var node = JsonNode.Parse(SettingsSecrets.MaskJson(input))!.AsObject();

        Assert.Equal("", (string?)node["GitHubToken"]);
        Assert.Equal("", (string?)node["DiveraAccessKey"]);
        Assert.True((bool?)node["IsDarkMode"]);
        Assert.Equal("ruhr", (string?)node["ThemePreset"]);
    }

    [Fact]
    public void Import_OhneZugangsdaten_BehaeltVorhandene()
    {
        var current = new AppSettings { GitHubToken = "alt-token", DiveraAccessKey = "alt-key" };
        var imported = new AppSettings { GitHubToken = "", DiveraAccessKey = "neu-key" };

        SettingsSecrets.KeepExistingSecrets(imported, current);

        Assert.Equal("alt-token", imported.GitHubToken);
        Assert.Equal("neu-key", imported.DiveraAccessKey);
    }

    [Theory]
    [InlineData("team-mobile-secret.bin", true)]
    [InlineData("TEAM-MOBILE-SECRET.BIN", true)]
    [InlineData("runtime-state.db", false)]
    [InlineData("AppSettings.json", false)]
    public void Backup_LaesstCookieSchluesselWeg(string file, bool excluded) =>
        Assert.Equal(excluded, SettingsSecrets.IsExcludedFromBackup(file));

    [Theory]
    [InlineData("AppSettings.json", true)]
    [InlineData("berichte/AppSettings.json", false)]
    [InlineData("StaffelSettings.json", false)]
    public void Backup_ErkenntEinstellungsdatei(string file, bool expected) =>
        Assert.Equal(expected, SettingsSecrets.IsAppSettingsFile(file));
}

public class CollarWebhookValidationTests
{
    private static CollarWebhookController.CollarLocationRequest Request(string id, string? name = "Rex") =>
        new(id, name, new CollarWebhookController.CoordinatesDto(51.0, 7.0), 80);

    [Theory]
    [InlineData("12345", true)]
    [InlineData("C-OOB_1.a:b", true)]
    [InlineData("<img src=x onerror=alert(1)>", false)]
    [InlineData("a b", false)]
    [InlineData("\"onmouseover=", false)]
    public void Id_NurErlaubteZeichen(string id, bool valid) =>
        Assert.Equal(valid, CollarWebhookController.IsValidCollarId(id));

    [Fact]
    public void Id_HoechstensMaximalLaenge()
    {
        Assert.True(CollarWebhookController.IsValidCollarId(new string('a', CollarWebhookController.MaxIdLength)));
        Assert.False(CollarWebhookController.IsValidCollarId(new string('a', CollarWebhookController.MaxIdLength + 1)));
    }

    [Fact]
    public async Task UngueltigeId_WirdAbgelehnt()
    {
        var controller = new CollarWebhookController(new CollarTrackingService(new EinsatzService()));
        var result = await controller.ReceiveLocation(Request("<script>"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ZuLangerOderSteuerzeichenName_WirdAbgelehnt()
    {
        var controller = new CollarWebhookController(new CollarTrackingService(new EinsatzService()));
        Assert.IsType<BadRequestObjectResult>(await controller.ReceiveLocation(Request("1", new string('x', 65))));
        Assert.IsType<BadRequestObjectResult>(await controller.ReceiveLocation(Request("1", "Rex\u0000")));
    }

    [Fact]
    public async Task NeueHalsbaender_SindBegrenzt_BekannteBleibenMoeglich()
    {
        var tracking = new CollarTrackingService(new EinsatzService());
        var controller = new CollarWebhookController(tracking);
        for (var i = 0; i < CollarWebhookController.MaxCollars; i++)
            Assert.IsType<OkObjectResult>(await controller.ReceiveLocation(Request($"c{i}")));

        var tooMany = Assert.IsType<ObjectResult>(await controller.ReceiveLocation(Request("neu")));
        Assert.Equal(StatusCodes.Status429TooManyRequests, tooMany.StatusCode);
        Assert.IsType<OkObjectResult>(await controller.ReceiveLocation(Request("c0")));
    }
}

public class TeamCookieRevocationTests
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);

    private static async Task<(EinsatzService Einsatz, Team Team)> ActiveEinsatzAsync(DateTime start)
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = start });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team 1" });
        return (einsatz, team);
    }

    [Fact]
    public async Task Cookie_GiltImLaufendenEinsatz()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);

        Assert.True(tokens.TryValidateTeamCookie(tokens.IssueTeamCookieValue(team.TeamId), out var teamId));
        Assert.Equal(team.TeamId, teamId);
    }

    [Fact]
    public async Task Cookie_IstNachEinsatzendeUngueltig()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        var cookie = tokens.IssueTeamCookieValue(team.TeamId);

        await einsatz.EndEinsatzAsync();

        Assert.False(tokens.TryValidateTeamCookie(cookie, out _));
    }

    // Neustart wie im Server: Token-Dienst entsteht mit leerem EinsatzService, danach stellt
    // RuntimeStatePersistenceService den Einsatz aus dem JSON-Snapshot wieder her.
    private static async Task<TeamMobileTokenService> RestartAsync(EinsatzService before)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new Einsatzueberwachung.Server.Services.CoordinateTupleConverter());
        var json = System.Text.Json.JsonSerializer.Serialize(before.ExportRuntimeSnapshot(), options);

        var restored = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, restored);
        await restored.ImportRuntimeSnapshotAsync(
            System.Text.Json.JsonSerializer.Deserialize<EinsatzRuntimeSnapshot>(json, options)!);
        return tokens;
    }

    [Fact]
    public void OhneEinsatz_GibtEsKeinenMasterToken()
    {
        // Das leere Platzhalter-Objekt beim Start hat IstEinsatz = true – es darf trotzdem nicht als aktiv gelten
        var tokens = new TeamMobileTokenService(Secret, new EinsatzService());
        Assert.Null(tokens.CurrentMasterToken);
    }

    [Fact]
    public async Task Cookie_UeberlebtNeustartImSelbenEinsatz()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0, 211));
        var cookie = new TeamMobileTokenService(Secret, einsatz).IssueTeamCookieValue(team.TeamId);

        var afterRestart = await RestartAsync(einsatz);

        Assert.NotNull(afterRestart.CurrentMasterToken);
        Assert.True(afterRestart.TryValidateTeamCookie(cookie, out _));
    }

    [Fact]
    public async Task Cookie_AusFruehererEinsatz_GiltNachNeustartNicht()
    {
        // Frueher begann die Generation nach jedem Neustart bei 0 – ein altes Cookie passte dann wieder
        var (oldEinsatz, oldTeam) = await ActiveEinsatzAsync(new DateTime(2026, 10, 20, 18, 0, 0));
        var oldCookie = new TeamMobileTokenService(Secret, oldEinsatz).IssueTeamCookieValue(oldTeam.TeamId);

        var (newEinsatz, _) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var afterRestart = await RestartAsync(newEinsatz);

        Assert.False(afterRestart.TryValidateTeamCookie(oldCookie, out _));
    }

    [Fact]
    public async Task NeuerEinsatzOhneEndeDesAlten_EntwertetCookieUndToken()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        var oldToken = tokens.CurrentMasterToken;
        var cookie = tokens.IssueTeamCookieValue(team.TeamId);

        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Anderer Ort", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 24, 15, 0, 0) });

        Assert.NotEqual(oldToken, tokens.CurrentMasterToken);
        Assert.False(tokens.ValidateMasterToken(oldToken!));
        Assert.False(tokens.TryValidateTeamCookie(cookie, out _));
    }

    [Fact]
    public async Task EinsatzdatenAendern_LaesstCookieGueltig()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        var cookie = tokens.IssueTeamCookieValue(team.TeamId);

        var changed = einsatz.CurrentEinsatz;
        await einsatz.UpdateEinsatzAsync(new EinsatzData { Einsatzort = "Testwald Süd", EinsatzNummer = "E-NEU", IstEinsatz = true, EinsatzDatum = changed.EinsatzDatum });

        Assert.True(tokens.TryValidateTeamCookie(cookie, out _));
    }

    [Fact]
    public async Task ValidatePrincipal_LehntCookieNachEinsatzendeAb()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        var cookie = tokens.IssueTeamCookieValue(team.TeamId);

        var during = Context(einsatz, tokens, team.TeamId, cookie);
        await TeamMobileAuth.ValidatePrincipalAsync(during);
        Assert.NotNull(during.Principal);

        await einsatz.EndEinsatzAsync();
        var afterEnd = Context(einsatz, tokens, team.TeamId, cookie);
        await TeamMobileAuth.ValidatePrincipalAsync(afterEnd);
        Assert.Null(afterEnd.Principal);
    }

    [Fact]
    public async Task ValidatePrincipal_LehntFremdeTeamIdUndUnbekanntesTeamAb()
    {
        var (einsatz, team) = await ActiveEinsatzAsync(new DateTime(2026, 10, 24, 9, 0, 0));
        var tokens = new TeamMobileTokenService(Secret, einsatz);

        var otherTeamClaim = Context(einsatz, tokens, "anderes-team", tokens.IssueTeamCookieValue(team.TeamId));
        await TeamMobileAuth.ValidatePrincipalAsync(otherTeamClaim);
        Assert.Null(otherTeamClaim.Principal);

        // Korrekt signiert, aber das Team gibt es im Einsatz nicht
        var unknown = Context(einsatz, tokens, "geister-team", tokens.IssueTeamCookieValue("geister-team"));
        await TeamMobileAuth.ValidatePrincipalAsync(unknown);
        Assert.Null(unknown.Principal);
    }

    private static CookieValidatePrincipalContext Context(IEinsatzService einsatz, ITeamMobileTokenService tokens, string teamId, string cookie)
    {
        var services = new ServiceCollection()
            .AddSingleton(einsatz)
            .AddSingleton(tokens)
            .AddSingleton<IAuthenticationService, NoopAuthenticationService>()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TeamMobileAuth.TeamIdClaim, teamId),
            new Claim(TeamMobileAuth.CookieValueClaim, cookie),
        ], TeamMobileAuth.AuthenticationScheme));
        var scheme = new AuthenticationScheme(TeamMobileAuth.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, TeamMobileAuth.AuthenticationScheme);
        return new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket);
    }

    private sealed class NoopAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
