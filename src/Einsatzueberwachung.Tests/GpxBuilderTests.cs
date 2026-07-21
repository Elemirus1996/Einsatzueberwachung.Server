using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public class GpxBuilderTests
{
    [Fact]
    public void SearchAreaFileName_StartsWithDayAndMonthAndFitsGarminLimit()
    {
        var area = new SearchArea { Name = "Sehr langes Suchgebiet mit Zusatz" };
        var date = new DateTime(2026, 7, 21);

        var fileName = GpxBuilder.SearchAreaFileName(area, date);

        Assert.StartsWith("2107_", fileName);
        Assert.Equal("2107_Sehr_langes_Suc.gpx", fileName);
        Assert.Equal(".gpx", Path.GetExtension(fileName));
        Assert.Equal(20, Path.GetFileNameWithoutExtension(fileName).Length);
    }

    [Fact]
    public void SearchAreaFileName_PreservesShortAreaNameAfterDate()
    {
        var area = new SearchArea { Name = "Suchgebiet1" };
        var date = new DateTime(2026, 7, 21);

        var fileName = GpxBuilder.SearchAreaFileName(area, date);

        Assert.Equal("2107_Suchgebiet1.gpx", fileName);
    }

    [Fact]
    public void SearchAreaFileName_PreservesSpecialCharactersDuringTruncation()
    {
        var area = new SearchArea { Name = "Waldstück Überhang" };

        var fileName = GpxBuilder.SearchAreaFileName(area, new DateTime(2026, 7, 21));

        Assert.Equal("2107_Waldstück_Überh.gpx", fileName);
    }
}
