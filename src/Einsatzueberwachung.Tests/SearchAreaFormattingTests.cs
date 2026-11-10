using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Tests;

public class SearchAreaFormattingTests
{
    [Theory]
    [InlineData(0.000001)]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    public void FormattedArea_AlwaysUsesSquareMeters(double sideDegrees)
    {
        var area = new SearchArea
        {
            Coordinates =
            [
                (0, 0),
                (0, sideDegrees),
                (sideDegrees, sideDegrees),
                (sideDegrees, 0)
            ]
        };

        var formattedArea = area.FormattedArea;

        Assert.EndsWith("m²", formattedArea);
        Assert.DoesNotContain("ha", formattedArea);
        Assert.DoesNotContain("km²", formattedArea);
    }
}
