using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Security;
using Microsoft.AspNetCore.Authentication;

namespace Einsatzueberwachung.Server.Extensions;

/// <summary>Anmelden, erstmalig einrichten und abmelden der Einsatzleitung (Formular-POST von /leitung/anmelden).</summary>
internal static class LeitungEndpoints
{
    public static WebApplication MapLeitungEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/leitung").AllowAnonymous().DisableAntiforgery();

        // Nicht /leitung/anmelden: dort nimmt die Blazor-Seite selbst POST an (mehrdeutige Route)
        group.MapPost("/anmeldung", async (HttpContext http, LeitungAuthStore store, LoginLockout lockout,
            IAuditLogService audit, LeitungsGeraetNamen geraete) =>
        {
            var form = await http.Request.ReadFormAsync();
            var returnUrl = SafeReturnUrl(form["returnUrl"]);
            var client = http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";
            var now = DateTime.UtcNow;

            if (lockout.IsLocked(client, now))
                return Results.Redirect(LoginUrl("gesperrt", returnUrl));

            if (!store.Verify(form["passwort"].ToString()))
            {
                lockout.Fail(client, now);
                // Etwas bremsen, damit Ausprobieren auch ohne Sperre langsam ist
                await Task.Delay(500);
                return Results.Redirect(LoginUrl("falsch", returnUrl));
            }

            lockout.Success(client);
            await http.SignInAsync(LeitungAuth.AuthenticationScheme, LeitungAuth.CreatePrincipal(store.Generation),
                new AuthenticationProperties { IsPersistent = true });
            var geraet = geraete.NameFuer(http.Connection.RemoteIpAddress);
            audit.Log("Anmeldung Einsatzleitung", geraet is null ? client : $"{geraet} ({client})");
            return Results.Redirect(returnUrl);
        });

        group.MapPost("/einrichten", async (HttpContext http, LeitungAuthStore store, LoginLockout lockout, ILogger<LeitungAuthStore> logger) =>
        {
            var form = await http.Request.ReadFormAsync();
            var client = http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";
            var now = DateTime.UtcNow;

            if (lockout.IsLocked(client, now))
                return Results.Redirect(LoginUrl("gesperrt", "/"));

            if (store.Setup(form["code"].ToString(), form["passwort"].ToString(), form["wiederholung"].ToString()) is { } error)
            {
                if (error.Contains("Einrichtungscode", StringComparison.Ordinal)) lockout.Fail(client, now);
                return Results.Redirect($"{LeitungAuth.LoginPath}?einrichtung={Uri.EscapeDataString(error)}");
            }

            lockout.Success(client);
            logger.LogInformation("Passwort der Einsatzleitung eingerichtet");
            await http.SignInAsync(LeitungAuth.AuthenticationScheme, LeitungAuth.CreatePrincipal(store.Generation),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect("/");
        });

        group.MapPost("/abmelden", async (HttpContext http) =>
        {
            await http.SignOutAsync(LeitungAuth.AuthenticationScheme);
            return Results.Redirect(LeitungAuth.LoginPath);
        });

        return app;
    }

    // Nur Pfade dieser Anwendung, nie fremde Adressen (sonst offene Weiterleitung nach der Anmeldung)
    internal static string SafeReturnUrl(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith('/') && !value.StartsWith("//") && !value.StartsWith("/\\")
            ? value
            : "/";

    private static string LoginUrl(string fehler, string returnUrl) =>
        $"{LeitungAuth.LoginPath}?fehler={fehler}&returnUrl={Uri.EscapeDataString(returnUrl)}";
}
