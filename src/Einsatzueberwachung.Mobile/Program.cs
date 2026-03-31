using Einsatzueberwachung.Mobile.Components;
using Einsatzueberwachung.Mobile.Services;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IMasterDataService, MasterDataService>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();
builder.Services.AddSingleton<IExcelExportService, ExcelExportService>();
builder.Services.AddSingleton<IArchivService, ArchivService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddHttpClient<IWeatherService, DwdWeatherService>();
builder.Services.AddHttpClient<MobileApiClient>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
