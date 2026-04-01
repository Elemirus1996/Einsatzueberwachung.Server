using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Hubs;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json;
using System.IO.Compression;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Forwarded Headers für Nginx Reverse Proxy (WICHTIG für Linux!)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor 
                             | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Blazor Server Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Response Compression für bessere Performance

// API Controllers für REST (Mobile & externe Clients)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Response Compression für bessere Performance
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "text/css", "text/javascript", "image/svg+xml" });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// Response Caching
builder.Services.AddResponseCaching();

// HttpClient für externe API-Calls
builder.Services.AddHttpClient();

var runtimeDbPath = Path.Combine(AppPathResolver.GetDataDirectory(), "runtime-state.db");
builder.Services.AddDbContextFactory<RuntimeDbContext>(options =>
    options.UseSqlite($"Data Source={runtimeDbPath}"));

// SignalR für Echtzeit-Updates
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 32 * 1024; // 32 KB
    options.StreamBufferCapacity = 10;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("VpnPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    options.AddPolicy("RestApi", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
// Registriere Domain-Services als Singletons (für den laufenden Einsatz)
builder.Services.AddSingleton<IMasterDataService, MasterDataService>();
builder.Services.AddSingleton<IEinsatzService, EinsatzService>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();
builder.Services.AddSingleton<IExcelExportService, ExcelExportService>();
builder.Services.AddSingleton<IArchivService, ArchivService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddScoped<IRadioService, RadioService>();

builder.Services.AddSingleton<GitHubUpdateService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<GitHubUpdateService>>();
    var settingsService = sp.GetRequiredService<ISettingsService>();
    return new GitHubUpdateService(factory.CreateClient(nameof(GitHubUpdateService)), logger, settingsService);
});

// Wetter-Service (DWD via BrightSky API)
builder.Services.AddHttpClient<IWeatherService, DwdWeatherService>();

// FluentValidation Validators registrieren
builder.Services.AddValidatorsFromAssembly(typeof(Einsatzueberwachung.Domain.Models.PersonalEntry).Assembly);

// Health Checks
builder.Services.AddHealthChecks();

// Relay Domain-Events an SignalR Clients
builder.Services.AddHostedService<EinsatzHubRelayService>();
builder.Services.AddHostedService<TeamTimerTickService>();
builder.Services.AddHostedService<UpdateAutoCheckService>();
builder.Services.AddHostedService<RuntimeStatePersistenceService>();

var app = builder.Build();

// Forwarded Headers MUSS als erstes kommen (Nginx!)
app.UseForwardedHeaders();

// Response Compression aktivieren
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Static Files mit Caching
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=2592000");
    }
});

// Response Caching
app.UseResponseCaching();

// CORS
app.UseCors("VpnPolicy");

app.UseAntiforgery();

app.MapStaticAssets();

// Health Check Endpoint
app.MapHealthChecks("/health");

// API Controllers mappen
app.MapControllers().RequireCors("RestApi");

// Swagger UI (nur in Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health Check Endpoint
app.MapHub<EinsatzHub>("/hubs/einsatz");

app.MapGet("/downloads/einsatz-bericht.pdf", async (IEinsatzService einsatzService, IPdfExportService pdfExportService) =>
{
    var einsatz = einsatzService.CurrentEinsatz;
    var fileNamePart = string.IsNullOrWhiteSpace(einsatz.EinsatzNummer)
        ? $"einsatzbericht-{DateTime.Now:yyyyMMdd-HHmmss}"
        : $"einsatzbericht-{einsatz.EinsatzNummer}";

    var bytes = await pdfExportService.ExportEinsatzToPdfBytesAsync(
        einsatz,
        einsatzService.Teams,
        einsatzService.GlobalNotes);

    return Results.File(bytes, "application/pdf", $"{fileNamePart}.pdf");
});

app.MapGet("/downloads/einsatz-archiv/{id}.pdf", async (string id, IArchivService archivService, IPdfExportService pdfExportService) =>
{
    var archivedEinsatz = await archivService.GetByIdAsync(id);
    if (archivedEinsatz is null)
    {
        return Results.NotFound();
    }

    var bytes = await pdfExportService.ExportArchivedEinsatzToPdfBytesAsync(archivedEinsatz);
    var fileNamePart = string.IsNullOrWhiteSpace(archivedEinsatz.EinsatzNummer)
        ? $"einsatz-archiv-{archivedEinsatz.EinsatzDatum:yyyyMMdd-HHmmss}"
        : $"einsatz-archiv-{archivedEinsatz.EinsatzNummer}";

    return Results.File(bytes, "application/pdf", $"{fileNamePart}.pdf");
});

app.MapGet("/downloads/einsatz-archiv.json", async (IArchivService archivService) =>
{
    var bytes = await archivService.ExportAllAsJsonAsync();
    return Results.File(bytes, "application/json", $"einsatz-archiv-{DateTime.Now:yyyyMMdd-HHmmss}.json");
});

app.MapGet("/downloads/app-settings.json", async (ISettingsService settingsService) =>
{
    var settings = await settingsService.GetAppSettingsAsync();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
    return Results.File(bytes, "application/json", "app-settings.json");
});

app.MapGet("/downloads/staffel-settings.json", async (ISettingsService settingsService) =>
{
    var settings = await settingsService.GetStaffelSettingsAsync();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
    return Results.File(bytes, "application/json", "staffel-settings.json");
});

app.MapGet("/downloads/staffel-logo", async (ISettingsService settingsService) =>
{
    var settings = await settingsService.GetStaffelSettingsAsync();
    if (string.IsNullOrWhiteSpace(settings.StaffelLogoPfad))
    {
        return Results.NotFound();
    }

    var logoPath = settings.StaffelLogoPfad;
    if (!Path.IsPathRooted(logoPath))
    {
        logoPath = Path.Combine(AppPathResolver.GetDataDirectory(), logoPath.TrimStart('/', '\\'));
    }

    if (!File.Exists(logoPath))
    {
        return Results.NotFound();
    }

    var contentTypeProvider = new FileExtensionContentTypeProvider();
    if (!contentTypeProvider.TryGetContentType(logoPath, out var contentType))
    {
        contentType = "application/octet-stream";
    }

    return Results.File(await File.ReadAllBytesAsync(logoPath), contentType);
});

app.MapGet("/downloads/session-data.json", async (IMasterDataService masterDataService) =>
{
    var sessionData = await masterDataService.LoadSessionDataAsync();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(sessionData, new JsonSerializerOptions { WriteIndented = true });
    return Results.File(bytes, "application/json", "session-data.json");
});

app.MapGet("/downloads/stammdaten.xlsx", async (IExcelExportService excelExportService) =>
{
    var bytes = await excelExportService.ExportStammdatenAsync();
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "stammdaten.xlsx");
});

app.MapGet("/downloads/stammdaten-template.xlsx", async (IExcelExportService excelExportService) =>
{
    var bytes = await excelExportService.CreateImportTemplateAsync();
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "stammdaten-template.xlsx");
});

app.MapGet("/downloads/data-backup.zip", () =>
{
    var dataDirectory = AppPathResolver.GetDataDirectory();
    if (!Directory.Exists(dataDirectory))
    {
        return Results.NotFound();
    }

    using var memoryStream = new MemoryStream();
    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var files = Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories);
        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(dataDirectory, filePath);
            archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }
    }

    var fileName = $"einsatzueberwachung-data-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    return Results.File(memoryStream.ToArray(), "application/zip", fileName);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();