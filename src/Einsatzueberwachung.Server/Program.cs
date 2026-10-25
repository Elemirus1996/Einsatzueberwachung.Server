using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Extensions;
using Einsatzueberwachung.Server.Hubs;
using Einsatzueberwachung.Server.Middleware;
using Einsatzueberwachung.Server.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddForwardedHeadersForReverseProxy(builder.Configuration);
builder.Services.AddPublicTeamAccess(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddSwagger();

builder.Services.AddLeitungAuthentication();
builder.Services.AddTeamMobileAuthentication(builder.Configuration);

builder.Services.AddCompressionAndCaching();

builder.Services.AddHttpClient();

builder.Services.AddRuntimeStateDb();
builder.Services.AddSignalRForRealtime(builder.Environment);
builder.Services.AddCorsPolicies(builder.Configuration);

builder.Services.AddDomainServices();
builder.Services.AddStaticMapAndUpdateServices();

builder.Services.AddHealthChecks();

builder.Services.AddRelayHostedServices();

var app = builder.Build();

// Forwarded Headers MUSS als erstes kommen (Nginx!)
app.UseForwardedHeaders();

// Oeffentlicher Team-Zugang: nur Team-Pfade, noch vor statischen Dateien und Endpunkten
app.UseMiddleware<PublicAccessMiddleware>();

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=2592000");
    }
});

app.UseResponseCaching();

// Nur ausdruecklich eingetragene fremde Origins (Cors:AllowedOrigins), sonst keine CORS-Freigabe
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Ohne Anmeldung: statische Dateien (auch fuer Anmelde- und Team-Seite) und der lokale Healthcheck
app.MapStaticAssets().AllowAnonymous();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapLeitungEndpoints();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHub<EinsatzHub>("/hubs/einsatz");
app.MapHub<Einsatzueberwachung.Server.Hubs.TeamMobileHub>("/hubs/team-mobile");

app.MapDownloadEndpoints();

// Seiten und Blazor-Verbindung selbst sind frei – welche Seite gerendert wird, entscheidet Routes.razor
// (Team-Seiten ohne, alle anderen nur mit Anmeldung der Einsatzleitung)
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

var publicAccess = app.Services.GetRequiredService<PublicAccess>();
if (publicAccess.Port > 0)
{
    app.Logger.LogInformation("Oeffentlicher Team-Zugang auf Port {Port}: dort nur Team-Seiten erreichbar", publicAccess.Port);
}

var leitungAuth = app.Services.GetRequiredService<LeitungAuthStore>();
if (leitungAuth.SetupCode is { } setupCode)
{
    app.Logger.LogWarning("Passwort der Einsatzleitung noch nicht eingerichtet. Einrichtungscode: {Code} – im Browser unter {Path} eingeben.",
        setupCode, LeitungAuth.LoginPath);
}

try
{
    app.Run();
}
catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    // Sonst steht im Journal nur ein langer Stacktrace und systemd startet den Dienst endlos neu
    app.Logger.LogCritical("Server startet nicht: {Message} Belegte Ports pruefen mit 'ss -ltnp' (5001 nutzt z.B. der alte " +
        "Dienst einsatzueberwachung-mobile) und ASPNETCORE_URLS / {PortVariable} anpassen.", ex.Message, PublicAccess.PortVariable);
    throw;
}
