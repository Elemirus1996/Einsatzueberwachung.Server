using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Einsatzueberwachung.Server.Security;

/// <summary>
/// Anmeldung der Einsatzleitung mit einem gemeinsamen Staffel-Passwort (1.10, Audit 2026-10-24: bisher war jedes
/// VPN-Geraet ohne Anmeldung Vollzugriff-Admin). Alle Seiten und Schnittstellen ausser der Team-Ansicht verlangen sie.
/// </summary>
public static class LeitungAuth
{
    public const string AuthenticationScheme = "Leitung";
    public const string CookieName = "einsatz.leitung.auth";
    public const string RoleClaim = "leitung";
    public const string GenerationClaim = "leitung-generation";
    public const string LoginPath = "/leitung/anmelden";

    public static ClaimsPrincipal CreatePrincipal(int generation) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Name, "Einsatzleitung"),
        new Claim(RoleClaim, "true"),
        new Claim(GenerationClaim, generation.ToString()),
    ], AuthenticationScheme));

    public static bool IsLeitung(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.HasClaim(c => c.Type == RoleClaim);

    /// <summary>Nach einer Passwortaenderung sind alle frueher ausgestellten Anmeldungen ungueltig.</summary>
    public static bool IsCurrent(ClaimsPrincipal? user, LeitungAuthStore store) =>
        IsLeitung(user)
        && int.TryParse(user!.FindFirst(GenerationClaim)?.Value, out var generation)
        && generation == store.Generation
        && store.IsSetUp;

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var store = ctx.HttpContext.RequestServices.GetRequiredService<LeitungAuthStore>();
        if (!IsCurrent(ctx.Principal, store))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AuthenticationScheme);
        }
    }
}

/// <summary>Seiten, die ohne Anmeldung der Einsatzleitung erreichbar sind.</summary>
public static class LeitungAccess
{
    private static readonly HashSet<Type> AnonymousPages =
    [
        typeof(Components.Pages.TeamLogin),
        typeof(Components.Pages.TeamMobile),
        typeof(Components.Pages.LeitungAnmelden),
        typeof(Components.Pages.Error),
    ];

    public static bool IsAnonymousPage(Type? pageType) => pageType is not null && AnonymousPages.Contains(pageType);
}

/// <summary>
/// Staffel-Passwort (PBKDF2-Hash) und Einrichtungscode. Liegt in einer eigenen Datei im Datenverzeichnis –
/// nicht in den AppSettings, die heruntergeladen und im Backup weitergegeben werden.
/// </summary>
public sealed class LeitungAuthStore
{
    public const string FileName = "leitung-auth.json";
    public const string SetupCodeFileName = "LEITUNG-EINRICHTUNGSCODE.txt";
    public const int MinPasswordLength = 10;
    private const int Iterations = 210_000;

    private readonly string _filePath;
    private readonly string _setupCodePath;
    private readonly object _lock = new();
    private StoredData _data;

    public LeitungAuthStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, FileName);
        _setupCodePath = Path.Combine(dataDirectory, SetupCodeFileName);
        _data = Load(_filePath);

        if (!IsSetUp)
        {
            // Code nur auf dem Server sichtbar (Journal und Datei) – wer ihn kennt, legt das Passwort fest
            SetupCode = RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 8);
            File.WriteAllText(_setupCodePath, SetupCode + Environment.NewLine);
        }
        else if (File.Exists(_setupCodePath))
        {
            File.Delete(_setupCodePath);
        }
    }

    public static LeitungAuthStore ForDataDirectory() => new(AppPathResolver.GetDataDirectory());

    public bool IsSetUp { get { lock (_lock) return _data.PasswordHash.Length > 0; } }

    public int Generation { get { lock (_lock) return _data.Generation; } }

    /// <summary>Einrichtungscode, solange noch kein Passwort festgelegt ist.</summary>
    public string? SetupCode { get; private set; }

    public string? Setup(string code, string password, string repeat)
    {
        lock (_lock)
        {
            if (_data.PasswordHash.Length > 0) return "Das Passwort ist bereits eingerichtet.";
            if (SetupCode is null || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()),
                    System.Text.Encoding.UTF8.GetBytes(SetupCode)))
                return "Der Einrichtungscode ist falsch.";
            if (CheckNewPassword(password, repeat) is { } error) return error;

            _data = new StoredData { PasswordHash = Hash(password), Generation = _data.Generation + 1 };
            Save();
            SetupCode = null;
            if (File.Exists(_setupCodePath)) File.Delete(_setupCodePath);
            return null;
        }
    }

    public bool Verify(string password)
    {
        string hash;
        lock (_lock) hash = _data.PasswordHash;
        return hash.Length > 0 && VerifyHash(password, hash);
    }

    /// <summary>Neues Passwort setzen; alle bestehenden Anmeldungen werden ungueltig.</summary>
    public string? ChangePassword(string current, string password, string repeat)
    {
        lock (_lock)
        {
            if (!VerifyHash(current, _data.PasswordHash)) return "Das aktuelle Passwort ist falsch.";
            if (CheckNewPassword(password, repeat) is { } error) return error;

            _data = new StoredData { PasswordHash = Hash(password), Generation = _data.Generation + 1 };
            Save();
            return null;
        }
    }

    private static readonly string TooShort = $"Das Passwort muss mindestens {MinPasswordLength} Zeichen lang sein.";
    private const string Mismatch = "Die beiden Passwörter stimmen nicht überein.";

    private static string? CheckNewPassword(string password, string repeat)
    {
        if (password.Length < MinPasswordLength) return TooShort;
        if (password != repeat) return Mismatch;
        return null;
    }

    /// <summary>Die Anmeldeseite zeigt nur eigene Meldungen an, keinen beliebigen Text aus der Adresse.</summary>
    public static bool IsKnownSetupError(string? message) =>
        message is "Das Passwort ist bereits eingerichtet." or "Der Einrichtungscode ist falsch." or Mismatch
        || message == TooShort;

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyHash(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Save()
    {
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _filePath, overwrite: true);
    }

    private static StoredData Load(string path)
    {
        if (!File.Exists(path)) return new StoredData();
        return JsonSerializer.Deserialize<StoredData>(File.ReadAllText(path)) ?? new StoredData();
    }

    private sealed class StoredData
    {
        public string PasswordHash { get; set; } = string.Empty;
        public int Generation { get; set; }
    }
}

/// <summary>Sperre nach Fehlversuchen: 5 falsche Passwoerter je Adresse sperren 5 Minuten.</summary>
public sealed class LoginLockout
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Failures, DateTime Until)> _state = new();

    public bool IsLocked(string client, DateTime nowUtc) =>
        _state.TryGetValue(client, out var s) && s.Failures >= MaxFailures && s.Until > nowUtc;

    public void Fail(string client, DateTime nowUtc) =>
        _state.AddOrUpdate(client,
            _ => (1, nowUtc + LockTime),
            (_, s) => (s.Until <= nowUtc ? 1 : s.Failures + 1, nowUtc + LockTime));

    public void Success(string client) => _state.TryRemove(client, out _);
}

/// <summary>
/// Prueft offene Blazor-Verbindungen jede Minute: Nach einer Passwortaenderung landen auch bereits geoeffnete
/// Seiten (z.B. ein Tablet im Feld) wieder auf der Anmeldung.
/// </summary>
public sealed class LeitungRevalidatingAuthStateProvider(ILoggerFactory loggerFactory, LeitungAuthStore store)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken) =>
        Task.FromResult(!LeitungAuth.IsLeitung(state.User) || LeitungAuth.IsCurrent(state.User, store));
}
