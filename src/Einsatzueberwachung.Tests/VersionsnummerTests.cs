using System.Reflection;
using System.Text.RegularExpressions;

namespace Einsatzueberwachung.Tests;

/// <summary>
/// Anzeige (Startseite, Einstellungen) und Updater lesen die AssemblyVersion. Sie muss zur &lt;Version&gt; im csproj
/// passen – in 1.11.7/1.11.8 stand sie noch auf 1.11.6, der Updater meldete deshalb dauerhaft „Update verfügbar“.
/// </summary>
public class VersionsnummerTests
{
    [Theory]
    [InlineData("Einsatzueberwachung.Server", typeof(Einsatzueberwachung.Server.Services.ElPositionService))]
    [InlineData("Einsatzueberwachung.Domain", typeof(Einsatzueberwachung.Domain.Services.Ortszeit))]
    public void AssemblyVersion_EntsprichtDerVersionImProjekt(string projekt, Type typAusDemProjekt)
    {
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var csproj = File.ReadAllText(Path.Combine(src, projekt, projekt + ".csproj"));
        var version = Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(version));
        Assert.DoesNotContain("<AssemblyVersion>", csproj); // wird aus <Version> abgeleitet
        Assert.Equal(version, typAusDemProjekt.Assembly.GetName().Version!.ToString(3));
    }
}
