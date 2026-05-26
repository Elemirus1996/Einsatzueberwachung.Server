using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Hubs;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Services.Radio;
using Einsatzueberwachung.Server.Training;
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
    public static IServiceCollection AddForwardedHeadersForReverseProxy(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                     | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
        return services;
    }

    public static IServiceCollection AddSwaggerWithTrainingSchema(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Einsatzueberwachung.Server API",
                Version = "v1",
                Description = "REST API fuer Mobile- und externe Integrationen inkl. Trainings-Endpoints."
            });

            options.SchemaFilter<TrainingOpenApiSchemaFilter>();

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });
        return services;
    }

    public static IServiceCollection AddTrainingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TrainingApiOptions>(
            configuration.GetSection(TrainingApiOptions.SectionName));
        services.AddSingleton<ITrainingExerciseService, TrainingExerciseService>();
        services.AddSingleton<ITrainingScenarioSuggestionService, TrainingScenarioSuggestionService>();
        services.AddSingleton<TrainerNotificationService>();
        services.Configure<TrainerAuthOptions>(
            configuration.GetSection(TrainerAuthOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddTrainerCookieAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "einsatz.trainer.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/einstellungen";
                options.AccessDeniedPath = "/einstellungen";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("TrainerOnly", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("Trainer");
            });
        });

        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        return services;
    }

    public static IServiceCollection AddTeamMobileAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TeamMobileOptions>(configuration.GetSection(TeamMobileOptions.SectionName));

        services.AddAuthentication()
            .AddCookie(TeamMobileAuth.AuthenticationScheme, options =>
            {
                options.Cookie.Name = TeamMobileAuth.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Path = "/";
                options.LoginPath = "/team/login";
                options.AccessDeniedPath = "/team/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(24);
                options.SlidingExpiration = true;
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

    public static IServiceCollection AddCorsPolicies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            var trainingOrigins = configuration
                .GetSection("TrainingApi:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

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

            options.AddPolicy("TrainingApi", policy =>
            {
                if (trainingOrigins.Length == 0)
                {
                    policy.SetIsOriginAllowed(_ => true);
                }
                else
                {
                    policy.WithOrigins(trainingOrigins);
                }

                policy.AllowAnyMethod()
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
        services.AddHttpClient<IDiveraService, DiveraService>();
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
