using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SalahWidget;

public enum LocationSource
{
    Precise,
    Approximate,
}

public sealed record GeoLocation(
    double Latitude,
    double Longitude,
    string? City,
    string? Country,
    LocationSource Source,
    DateTimeOffset FetchedAt)
{
    public string DisplayName =>
        (City, Country) switch
        {
            ({ Length: > 0 } c, { Length: > 0 } k) => $"{c}, {k}",
            ({ Length: > 0 } c, _) => c,
            (_, { Length: > 0 } k) => k,
            _ => string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.00}, {Longitude:0.00}"),
        };
}

/// <summary>Prayer times for one day at one location, as returned by Aladhan.</summary>
public sealed class PrayerDay
{
    public DateOnly Date { get; set; }
    public Dictionary<string, TimeSpan> Timings { get; set; } = new();
    public string TimeZone { get; set; } = "";
    public string HijriDay { get; set; } = "";
    public string HijriMonth { get; set; } = "";
    public string HijriYear { get; set; } = "";
    public string MethodName { get; set; } = "";
    public int School { get; set; }
}

/// <summary>Per-widget user settings, persisted in the widget's CustomState.</summary>
public sealed class WidgetSettings
{
    /// <summary>Aladhan method id, or null to let Aladhan pick one for the location.</summary>
    public int? Method { get; set; }

    /// <summary>0 = Standard (Shafi'i, Maliki, Hanbali), 1 = Hanafi.</summary>
    public int School { get; set; }

    public string CacheKey => $"{Method?.ToString() ?? "auto"}-{School}";

    /// <summary>Restores settings from a widget's CustomState; defaults when missing or corrupt.</summary>
    public static WidgetSettings FromCustomState(string? customState)
    {
        if (string.IsNullOrWhiteSpace(customState))
            return new WidgetSettings();
        try
        {
            return JsonSerializer.Deserialize<WidgetSettings>(customState) ?? new WidgetSettings();
        }
        catch (JsonException)
        {
            return new WidgetSettings();
        }
    }

    /// <summary>
    /// Parses the inputs posted by the Customize card ({"method":"auto"|"&lt;id&gt;","school":"0"|"1"}).
    /// Returns null when the payload isn't a JSON object.
    /// </summary>
    public static WidgetSettings? FromCustomizeInputs(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;
        try
        {
            if (JsonNode.Parse(data) is not JsonObject node)
                return null;
            var method = node["method"]?.ToString();
            var school = node["school"]?.ToString();
            return new WidgetSettings
            {
                Method = int.TryParse(method, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : null,
                School = school == "1" ? 1 : 0,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
