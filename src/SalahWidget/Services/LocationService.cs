using System.Globalization;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace SalahWidget.Services;

/// <summary>
/// Resolves the device location the same way the Weather widget does: Windows Location
/// Services first, falling back to an approximate IP-based location when access is off.
/// </summary>
public sealed class LocationService
{
    private readonly HttpClient _http;

    public LocationService(HttpClient http) => _http = http;

    public async Task<GeoLocation> GetLocationAsync(CancellationToken ct = default)
    {
        var precise = await TryGetPreciseAsync(ct);
        if (precise is not null)
            return precise;

        return await GetApproximateAsync(ct)
            ?? throw new InvalidOperationException("Unable to determine location.");
    }

    private async Task<GeoLocation?> TryGetPreciseAsync(CancellationToken ct)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                Log.Write($"Geolocator access: {access}");
                return null;
            }

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator
                .GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10))
                .AsTask(ct);

            var point = position.Coordinate.Point.Position;
            var (city, country) = await TryReverseGeocodeAsync(point.Latitude, point.Longitude, ct);
            return new GeoLocation(point.Latitude, point.Longitude, city, country,
                LocationSource.Precise, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Write($"Geolocator failed: {ex.Message}");
            return null;
        }
    }

    internal async Task<(string? City, string? Country)> TryReverseGeocodeAsync(
        double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url = "https://api.bigdatacloud.net/data/reverse-geocode-client" +
                      $"?latitude={Inv(lat)}&longitude={Inv(lon)}&localityLanguage=en";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            var root = doc.RootElement;
            var city = Str(root, "city") ?? Str(root, "locality") ?? Str(root, "principalSubdivision");
            return (city, Str(root, "countryName"));
        }
        catch (Exception ex)
        {
            Log.Write($"Reverse geocode failed: {ex.Message}");
            return (null, null);
        }
    }

    internal async Task<GeoLocation?> GetApproximateAsync(CancellationToken ct)
    {
        // Primary: ipwho.is
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync("https://ipwho.is/", ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return new GeoLocation(
                    root.GetProperty("latitude").GetDouble(),
                    root.GetProperty("longitude").GetDouble(),
                    Str(root, "city"), Str(root, "country"),
                    LocationSource.Approximate, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ipwho.is failed: {ex.Message}");
        }

        // Backup: ipapi.co
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync("https://ipapi.co/json/", ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number)
            {
                return new GeoLocation(
                    lat.GetDouble(),
                    root.GetProperty("longitude").GetDouble(),
                    Str(root, "city"), Str(root, "country_name"),
                    LocationSource.Approximate, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ipapi.co failed: {ex.Message}");
        }

        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static string Inv(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}
