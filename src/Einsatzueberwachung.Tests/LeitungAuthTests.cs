using System.Security.Claims;
using Einsatzueberwachung.Server.Components.Pages;
using Einsatzueberwachung.Server.Extensions;
using Einsatzueberwachung.Server.Security;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// 1.10 – Anmeldung der Einsatzleitung (Staffel-Passwort)
// ------------------------------------------------------------
public sealed class LeitungAuthTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-leitung-" + Guid.NewGuid().ToString("N"));

    public LeitungAuthTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private LeitungAuthStore NewStore() => new(_dir);

    private LeitungAuthStore SetUpStore(string password = "Rettungshund2026")
    {
        var store = NewStore();
        Assert.Null(store.Setup(store.SetupCode!, password, password));
        return store;
    }

    [Fact]
    public void OhnePasswort_GibtEsEinenEinrichtungscode_AuchAlsDatei()
    {
        var store = NewStore();

        Assert.False(store.IsSetUp);
        Assert.Matches("^[A-Z2-9]{8}$", store.SetupCode!);
        Assert.Equal(store.SetupCode, File.ReadAllText(Path.Combine(_dir, LeitungAuthStore.SetupCodeFileName)).Trim());
    }

    [Fact]
    public void Einrichten_MitFalschemCode_ScheitertMitBekannterMeldung()
    {
        var store = NewStore();
        var error = store.Setup("FALSCH12", "Rettungshund2026", "Rettungshund2026");

        Assert.NotNull(error);
        Assert.True(LeitungAuthStore.IsKnownSetupError(error));
        Assert.False(store.IsSetUp);
    }

    [Theory]
    [InlineData("kurz", "kurz")]
    [InlineData("Rettungshund2026", "Rettungshund2027")]
    public void Einrichten_PrueftLaengeUndWiederholung(string password, string repeat)
    {
        var store = NewStore();
        var error = store.Setup(store.SetupCode!, password, repeat);

        Assert.True(LeitungAuthStore.IsKnownSetupError(error));
        Assert.False(store.IsSetUp);
    }

    [Fact]
    public void Einrichten_CodeOhneGrossschreibung_Funktioniert_UndLoeschtCodeDatei()
    {
        var store = NewStore();
        Assert.Null(store.Setup(store.SetupCode!.ToLowerInvariant(), "Rettungshund2026", "Rettungshund2026"));

        Assert.True(store.IsSetUp);
        Assert.Null(store.SetupCode);
        Assert.False(File.Exists(Path.Combine(_dir, LeitungAuthStore.SetupCodeFileName)));
        Assert.True(store.Verify("Rettungshund2026"));
        Assert.False(store.Verify("rettungshund2026"));
    }

    [Fact]
    public void Passwort_WirdNurAlsHashGespeichert_UndUeberstehtNeustart()
    {
        SetUpStore("Rettungshund2026");

        var file = File.ReadAllText(Path.Combine(_dir, LeitungAuthStore.FileName));
        Assert.DoesNotContain("Rettungshund2026", file);
        Assert.Contains("pbkdf2-sha256$", file);

        var afterRestart = NewStore();
        Assert.True(afterRestart.IsSetUp);
        Assert.Null(afterRestart.SetupCode);
        Assert.True(afterRestart.Verify("Rettungshund2026"));
    }

    [Fact]
    public void Passwortaenderung_MeldetAlleAnmeldungenAb()
    {
        var store = SetUpStore();
        var oldLogin = LeitungAuth.CreatePrincipal(store.Generation);
        Assert.True(LeitungAuth.IsCurrent(oldLogin, store));

        Assert.NotNull(store.ChangePassword("falsch", "Suchhund-Neu-2026", "Suchhund-Neu-2026"));
        Assert.True(LeitungAuth.IsCurrent(oldLogin, store));

        Assert.Null(store.ChangePassword("Rettungshund2026", "Suchhund-Neu-2026", "Suchhund-Neu-2026"));
        Assert.False(LeitungAuth.IsCurrent(oldLogin, store));
        Assert.True(LeitungAuth.IsCurrent(LeitungAuth.CreatePrincipal(store.Generation), store));
        Assert.True(store.Verify("Suchhund-Neu-2026"));
        Assert.False(store.Verify("Rettungshund2026"));
    }

    [Fact]
    public void TeamCookie_GiltNichtAlsLeitung()
    {
        var store = SetUpStore();
        var team = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamMobileAuth.TeamIdClaim, "t1")], TeamMobileAuth.AuthenticationScheme));

        Assert.False(LeitungAuth.IsLeitung(team));
        Assert.False(LeitungAuth.IsCurrent(team, store));
        Assert.False(LeitungAuth.IsLeitung(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void FremderTextInDerAdresse_WirdNichtAngezeigt()
    {
        Assert.False(LeitungAuthStore.IsKnownSetupError("Bitte hier klicken: https://boese.example"));
        Assert.False(LeitungAuthStore.IsKnownSetupError(null));
    }

    [Fact]
    public void NurTeamUndAnmeldeseiten_SindOhneLoginErreichbar()
    {
        Assert.True(LeitungAccess.IsAnonymousPage(typeof(TeamLogin)));
        Assert.True(LeitungAccess.IsAnonymousPage(typeof(TeamMobile)));
        Assert.True(LeitungAccess.IsAnonymousPage(typeof(LeitungAnmelden)));
        Assert.False(LeitungAccess.IsAnonymousPage(typeof(Einstellungen)));
        Assert.False(LeitungAccess.IsAnonymousPage(typeof(EinsatzLeitung)));
        Assert.False(LeitungAccess.IsAnonymousPage(typeof(EinsatzMonitor)));
        Assert.False(LeitungAccess.IsAnonymousPage(null));
    }

    [Theory]
    [InlineData("/einstellungen", "/einstellungen")]
    [InlineData("/el?tab=karte", "/el?tab=karte")]
    [InlineData("//boese.example", "/")]
    [InlineData("/\\boese.example", "/")]
    [InlineData("https://boese.example", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Ruecksprung_NurAufEigenePfade(string? value, string expected) =>
        Assert.Equal(expected, LeitungEndpoints.SafeReturnUrl(value));

    [Fact]
    public void Sperre_NachFuenfFehlversuchen_FuerFuenfMinuten()
    {
        var lockout = new LoginLockout();
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < LoginLockout.MaxFailures - 1; i++) lockout.Fail("10.10.0.5", now);
        Assert.False(lockout.IsLocked("10.10.0.5", now));

        lockout.Fail("10.10.0.5", now);
        Assert.True(lockout.IsLocked("10.10.0.5", now));
        Assert.False(lockout.IsLocked("10.10.0.6", now));
        Assert.False(lockout.IsLocked("10.10.0.5", now + LoginLockout.LockTime + TimeSpan.FromSeconds(1)));

        lockout.Success("10.10.0.5");
        Assert.False(lockout.IsLocked("10.10.0.5", now));
    }
}
