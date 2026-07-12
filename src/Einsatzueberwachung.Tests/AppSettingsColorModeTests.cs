using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Tests;

public class AppSettingsColorModeTests
{
    [Fact]
    public void CollarColorModes_DefaultAndInvalidValuesUseConfiguredDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal("black", settings.CollarTrackColorModeOrDefault);
        Assert.Equal("area-black-outline", settings.CollarMarkerColorModeOrDefault);

        settings.CollarTrackColorMode = "invalid";
        settings.CollarMarkerColorMode = "contrast";

        Assert.Equal("black", settings.CollarTrackColorModeOrDefault);
        Assert.Equal("contrast", settings.CollarMarkerColorModeOrDefault);

        settings.CollarMarkerColorMode = "area-white-outline";
        Assert.Equal("area-white-outline", settings.CollarMarkerColorModeOrDefault);

        settings.CollarMarkerColorMode = "black-white-outline";
        Assert.Equal("black-white-outline", settings.CollarMarkerColorModeOrDefault);

        settings.CollarTrackColorMode = "area-dots";

        Assert.Equal("area-dots", settings.CollarTrackColorModeOrDefault);

        settings.CollarTrackColorMode = "area-cased";
        Assert.Equal("area-cased", settings.CollarTrackColorModeOrDefault);

        settings.CollarTrackColorMode = "black-cased";
        Assert.Equal("black-cased", settings.CollarTrackColorModeOrDefault);
    }
}
