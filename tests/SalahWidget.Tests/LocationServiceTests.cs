using System.Net;
using SalahWidget.Services;
using Xunit;

namespace SalahWidget.Tests;

/// <summary>
/// Covers the HTTP-based parts of location lookup: the approximate (IP) fallback chain and the
/// reverse geocoder. The Windows Geolocator path depends on device permissions and isn't unit tested.
/// </summary>
public class LocationServiceTests
{
    private const string IpWhoOk = """
        { "success": true, "latitude": 51.5072, "longitude": -0.1276, "city": "London", "country": "United Kingdom" }
        """;

    private const string IpApiOk = """
        { "latitude": 48.8566, "longitude": 2.3522, "city": "Paris", "country_name": "France" }
        """;

    private static Func<Uri, HttpResponseMessage> Route(Func<HttpResponseMessage>? ipWho = null, Func<HttpResponseMessage>? ipApi = null, Func<HttpResponseMessage>? geo = null) =>
        uri => uri.Host switch
        {
            "ipwho.is" => (ipWho ?? (() => FakeHttp.Json(IpWhoOk)))(),
            "ipapi.co" => (ipApi ?? (() => FakeHttp.Json(IpApiOk)))(),
            "api.bigdatacloud.net" => (geo ?? (() => FakeHttp.Fail()))(),
            _ => throw new InvalidOperationException($"Unexpected request to {uri}"),
        };

    // ---------------------------------------------------------------- IP fallback chain

    [Fact]
    public async Task Approximate_UsesIpWhoIs_First()
    {
        var http = new FakeHttp(Route());

        var loc = await new LocationService(http.Client()).GetApproximateAsync(default);

        Assert.NotNull(loc);
        Assert.Equal("London", loc!.City);
        Assert.Equal("United Kingdom", loc.Country);
        Assert.Equal(51.5072, loc.Latitude);
        Assert.Equal(LocationSource.Approximate, loc.Source);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Approximate_IpWhoIsReportsFailure_FallsBackToIpApi()
    {
        var http = new FakeHttp(Route(ipWho: () => FakeHttp.Json("""{ "success": false, "message": "Reserved range" }""")));

        var loc = await new LocationService(http.Client()).GetApproximateAsync(default);

        Assert.Equal("Paris", loc!.City);
        Assert.Equal("France", loc.Country);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Approximate_IpWhoIsNetworkError_FallsBackToIpApi()
    {
        var http = new FakeHttp(Route(ipWho: FakeHttp.Throw));

        var loc = await new LocationService(http.Client()).GetApproximateAsync(default);

        Assert.Equal("Paris", loc!.City);
    }

    [Fact]
    public async Task Approximate_IpWhoIsGarbage_FallsBackToIpApi()
    {
        var http = new FakeHttp(Route(ipWho: () => FakeHttp.Json("<html>blocked</html>")));

        var loc = await new LocationService(http.Client()).GetApproximateAsync(default);

        Assert.Equal("Paris", loc!.City);
    }

    [Fact]
    public async Task Approximate_IpApiRateLimited_ReturnsNull()
    {
        var http = new FakeHttp(Route(
            ipWho: () => FakeHttp.Fail(HttpStatusCode.TooManyRequests),
            ipApi: () => FakeHttp.Json("""{ "error": true, "reason": "RateLimited" }""")));

        Assert.Null(await new LocationService(http.Client()).GetApproximateAsync(default));
    }

    [Fact]
    public async Task Approximate_BothServicesDown_ReturnsNull()
    {
        var http = new FakeHttp(Route(ipWho: FakeHttp.Throw, ipApi: FakeHttp.Throw));

        Assert.Null(await new LocationService(http.Client()).GetApproximateAsync(default));
    }

    [Fact]
    public async Task Approximate_MissingCity_StillReturnsCoordinates()
    {
        var http = new FakeHttp(Route(ipWho: () => FakeHttp.Json("""{ "success": true, "latitude": 1.5, "longitude": 2.5, "city": "", "country": null }""")));

        var loc = await new LocationService(http.Client()).GetApproximateAsync(default);

        Assert.Null(loc!.City);
        Assert.Null(loc.Country);
        Assert.Equal(1.5, loc.Latitude);
    }

    // ---------------------------------------------------------------- reverse geocoding

    [Theory]
    [InlineData("""{ "city": "Lahore", "locality": "Gulberg", "principalSubdivision": "Punjab", "countryName": "Pakistan" }""", "Lahore", "Pakistan")]
    [InlineData("""{ "city": "", "locality": "Gulberg", "principalSubdivision": "Punjab", "countryName": "Pakistan" }""", "Gulberg", "Pakistan")]
    [InlineData("""{ "city": "", "locality": "", "principalSubdivision": "Punjab", "countryName": "Pakistan" }""", "Punjab", "Pakistan")]
    [InlineData("""{ "countryName": "Pakistan" }""", null, "Pakistan")]
    [InlineData("""{}""", null, null)]
    public async Task ReverseGeocode_PicksMostSpecificName(string body, string? city, string? country)
    {
        var http = new FakeHttp(Route(geo: () => FakeHttp.Json(body)));

        var result = await new LocationService(http.Client()).TryReverseGeocodeAsync(31.52, 74.35, default);

        Assert.Equal(city, result.City);
        Assert.Equal(country, result.Country);
    }

    [Fact]
    [UseCulture("de-DE")]
    public async Task ReverseGeocode_SendsInvariantCoordinates()
    {
        var http = new FakeHttp(Route(geo: () => FakeHttp.Json("{}")));

        await new LocationService(http.Client()).TryReverseGeocodeAsync(31.5204, -74.3587, default);

        var url = http.Requests.Single().ToString();
        Assert.Contains("latitude=31.5204", url);
        Assert.Contains("longitude=-74.3587", url);
    }

    [Fact]
    public async Task ReverseGeocode_Failure_ReturnsNoNames()
    {
        var http = new FakeHttp(Route(geo: FakeHttp.Throw));

        var result = await new LocationService(http.Client()).TryReverseGeocodeAsync(31.52, 74.35, default);

        Assert.Null(result.City);
        Assert.Null(result.Country);
    }
}
