using Xunit;

namespace SalahWidget.Tests;

public class GeoLocationTests
{
    private static GeoLocation Loc(string? city, string? country, double lat = 64.1466, double lon = -21.9426) =>
        new(lat, lon, city, country, LocationSource.Precise, DateTimeOffset.UnixEpoch);

    [Fact]
    public void DisplayName_CityAndCountry()
    {
        Assert.Equal("Karachi, Pakistan", Loc("Karachi", "Pakistan").DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DisplayName_CityOnly(string? country)
    {
        Assert.Equal("Karachi", Loc("Karachi", country).DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DisplayName_CountryOnly(string? city)
    {
        Assert.Equal("Pakistan", Loc(city, "Pakistan").DisplayName);
    }

    [Fact]
    [UseCulture("")]
    public void DisplayName_NoNames_FallsBackToCoordinates()
    {
        Assert.Equal("64.15, -21.94", Loc(null, null).DisplayName);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void DisplayName_Coordinates_UseInvariantDecimalSeparator()
    {
        // In cultures with a comma decimal separator "64,15, -21,94" would be ambiguous.
        Assert.Equal("64.15, -21.94", Loc("", "").DisplayName);
    }

    [Fact]
    [UseCulture("")]
    public void DisplayName_Coordinates_SouthernAndEasternHemispheres()
    {
        Assert.Equal("-33.87, 151.21", Loc(null, null, -33.8688, 151.2093).DisplayName);
    }
}
