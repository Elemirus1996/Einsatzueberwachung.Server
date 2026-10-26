using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Hubs;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Services.Radio;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Einsatzueberwachung.Server.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForwardedHeadersForReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                     | ForwardedHeaders.XForwardedProto;
            // X-Forwarded-* nur vom Nginx auf demselben Rechner (Standard: 127.0.0.1 und ::1) uebernehmen.
            // Weitere Proxys (z.B. ein Docker-Netz) ausdruecklich eintragen: ReverseProxy:KnownNetworks = ["172.16.0.0/12"]
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            {
                if (System.Net.IPNetwork.TryParse(network, out var parsed))
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(parsed.BaseAddress, parsed.PrefixLength));
            }
        });
        return services;
    }

    public static IServiceCollection AddPublicTeamAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(PublicAccess.FromConfiguration(configuration));
        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Einsatzueberwachung.Server API",
                Version = "v1",
                Description = "REST API fuer Mobile- und externe Integrationen."
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });
        return services;
    }

    public static IServiceCollection AddLeitungAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(_ => LeitungAuthStore.ForDataDirectory());
        services.AddSingleton<LoginLockout>();
        services.AddSingleton(_ => LiveTrackingAccess.ForDataDirectory());

        // VPN-Geraete per QR-Code (1.10)
        services.AddSingleton<IVpnPeerTool, SudoVpnPeerTool>();
        services.AddSingleton(_ => VpnDeviceStore.ForDataDirectory());
        services.AddSingleton<VpnDeviceService>();
        services.AddHostedService<VpnDeviceCleanupService>();
        services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, LeitungRevalidatingAuthStateProvider>();

        // Standard-Anmeldung ist die der Einsatzleitung; die Team-Ansicht nutzt ihr eigenes Schema ausdruecklich
        services.AddAuthentication(LeitungAuth.AuthenticationScheme)
            .AddCookie(LeitungAuth.AuthenticationScheme, options =>
            {
                options.Cookie.Name = LeitungAuth.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.LoginPath = LeitungAuth.LoginPath;
                options.AccessDeniedPath = LeitungAuth.LoginPath;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = LeitungAuth.ValidatePrincipalAsync;
                options.Events.OnRedirectToLogin = ctx =>
                {
                    // Schnittstellen bekommen 401 statt einer Weiterleitung auf die Anmeldeseite
                    if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/hubs"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        // Alles, was nicht ausdruecklich freigegeben ist ([AllowAnonymous] bzw. Team-Richtlinie), verlangt die
        // Anmeldung der Einsatzleitung – Controller, Downloads, Hubs (Audit 2026-10-24)
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(LeitungAuth.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(LeitungAuth.RoleClaim)
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddTeamMobileAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TeamMobileOptions>(configuration.GetSection(TeamMobileOptions.SectionName));

        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();

        services.AddAuthentication()
            .AddCookie(TeamMobileAuth.AuthenticationScheme, options =>
            {
                options.Cookie.Name = TeamMobileAuth.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Nur ueber HTTPS senden. Nginx meldet HTTPS per X-Forwarded-Proto; unter localhost akzeptieren Browser
                // das Cookie auch ohne HTTPS (Entwicklung).
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.LoginPath = "/team/login";
                options.AccessDeniedPath = "/team/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(24);
                options.SlidingExpiration = true;
                // Bei jeder Anfrage pruefen, ob das Team-Cookie noch zum laufenden Einsatz gehoert und das Team noch existiert.
                // Nach Einsatzende oder bei einem neuen Einsatz ist es damit sofort ungueltig (Audit 2026-10-24).
                options.Events.OnValidatePrincipal = TeamMobileAuth.ValidatePrincipalAsync;
                options.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api/team-mobile"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(TeamMobileAuth.AuthorizationPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TeamMobileAuth.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TeamMobileAuth.TeamIdClaim);
            });
        });

        var secret = LoadOrCreateTeamMobileSecret();
        services.AddSingleton<ITeamMobileTokenService>(sp =>
            new TeamMobileTokenService(secret, sp.GetRequiredService<IEinsatzService>()));

        return services;
    }

    private static byte[] LoadOrCreateTeamMobileSecret()
    {
        var path = Path.Combine(AppPathResolver.GetDataDirectory(), "team-mobile-secret.bin");
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length >= 32) return existing;
        }
        var fresh = RandomNumberGenerator.GetBytes(64);
        File.WriteAllBytes(path, fresh);
        return fresh;
    }

    public static IServiceCollection AddCompressionAndCaching(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                new[] { "application/json", "text/css", "text/javascript", "image/svg+xml" });
        });

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });

        services.AddResponseCaching();
        return services;
    }

    public static IServiceCollection AddRuntimeStateDb(this IServiceCollection services)
    {
        var runtimeDbPath = Path.Combine(AppPathResolver.GetDataDirectory(), "runtime-state.db");
        services.AddDbContextFactory<RuntimeDbContext>(options =>
            options.UseSqlite($"Data Source={runtimeDbPath}"));
        return services;
    }

    public static IServiceCollection AddSignalRForRealtime(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = environment.IsDevelopment();
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
            options.MaximumReceiveMessageSize = 32 * 1024;
            options.StreamBufferCapacity = 10;
        });
        return services;
    }

    public const string CorsPolicyName = "ConfiguredOrigins";

    public static IServiceCollection AddCorsPolicies(this IServiceCollection services, IConfiguration configuration)
    {
        // Browser-Oberflaeche, Team-Mobile und API laufen unter derselben Adresse und brauchen kein CORS.
        // Frueher war jede Origin erlaubt (mit Cookies) – damit konnte jede Webseite im Browser eines VPN-Geraets
        // die API lesen und Aktionen ausloesen (Audit 2026-10-24). Fremde Origins nur noch per Cors:AllowedOrigins.
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.WithOrigins(origins)
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });
        return services;
    }

    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IMasterDataService, MasterDataService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITimeService, AppTimeService>();
        services.AddSingleton<IEinsatzService, EinsatzService>();
        services.AddSingleton<IDashboardLayoutService, DashboardLayoutService>();
        services.AddSingleton<ICollarTrackingService, CollarTrackingService>();
        services.AddSingleton<IPdfExportService>(sp => new PdfExportService(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ITimeService>(),
            sp.GetRequiredService<IStaticMapRenderer>()));
        services.AddSingleton<IExcelExportService, ExcelExportService>();
        services.AddSingleton<IArchivService, ArchivService>();
        services.AddSingleton<EinsatzAbschlussService>();
        services.AddSingleton<TeamSucheService>();
        services.AddSingleton<ElOutboxService>();
        services.AddSingleton<IEinsatzMergeService, EinsatzMergeService>();
        services.AddSingleton<IEinsatzExportService, EinsatzExportService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddHostedService<AuditLogRelayService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<IWarningService, WarningService>();
        services.AddSingleton<IHomeNotesService, HomeNotesService>();
        services.AddScoped<MissionTopbarService>();
        services.AddScoped<BrowserPreferencesService>();
        services.AddScoped<IRadioService, RadioService>();

        services.AddValidatorsFromAssembly(typeof(Einsatzueberwachung.Domain.Models.PersonalEntry).Assembly);
        return services;
    }

    public static IServiceCollection AddStaticMapAndUpdateServices(this IServiceCollection services)
    {
        services.AddHttpClient("OsmTiles", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Einsatzueberwachung/1.2 (+https://github.com/Elemirus1996/Einsatzueberwachung.Server)");
            client.DefaultRequestHeaders.Referrer = new Uri("https://github.com/Elemirus1996/Einsatzueberwachung.Server");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IStaticMapRenderer, OsmStaticMapRenderer>();

        services.AddSingleton<GitHubUpdateService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<GitHubUpdateService>>();
            var settingsService = sp.GetRequiredService<ISettingsService>();
            return new GitHubUpdateService(factory.CreateClient(nameof(GitHubUpdateService)), logger, settingsService);
        });

        services.AddHttpClient<IWeatherService, DwdWeatherService>();
        // Eine Instanz fuer alle Seiten: gemeinsamer Zwischenspeicher + Backoff (statt je Seite eigene Divera-Abfragen)
        services.AddSingleton<IDiveraService>(sp => new DiveraService(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IMasterDataService>(),
            sp.GetRequiredService<ILogger<DiveraService>>()));
        return services;
    }

    public static IServiceCollection AddRelayHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<EinsatzHubRelayService>();
        services.AddHostedService<CollarTrackingRelayService>();
        services.AddHostedService<TeamMobileHubRelayService>();
        services.AddHostedService<TeamTimerTickService>();
        services.AddHostedService<UpdateAutoCheckService>();
        services.AddHostedService<RuntimeStatePersistenceService>();
        return services;
    }
}
