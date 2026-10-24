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

app.MapStaticAssets();

app.MapHealthChecks("/health");

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHub<EinsatzHub>("/hubs/einsatz");
app.MapHub<Einsatzueberwachung.Server.Hubs.TeamMobileHub>("/hubs/team-mobile");

app.MapDownloadEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var publicAccess = app.Services.GetRequiredService<PublicAccess>();
if (publicAccess.Port > 0)
{
    app.Logger.LogInformation("Oeffentlicher Team-Zugang auf Port {Port}: dort nur Team-Seiten erreichbar", publicAccess.Port);
}

app.Run();
