using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace SalahWidget.Services;

/// <summary>Fetches daily prayer times from the Aladhan API (https://aladhan.com/prayer-times-api).</summary>
public sealed class PrayerTimesService
{
    private static readonly string[] Keys =
        ["Imsak", "Fajr", "Sunrise", "Dhuhr", "Asr", "Sunset", "Maghrib", "Isha", "Midnight"];

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, PrayerDay> _cache;

    /// <param name="cachePath">Where the day cache is persisted; defaults to the app data folder.</param>
    public PrayerTimesService(HttpClient http, string? cachePath = null)
    {
        _http = http;
        _cachePath = cachePath ?? Path.Combine(AppPaths.DataDir, "prayer-cache.json");
        _cache = LoadCache();
    }

    public async Task<PrayerDay> GetDayAsync(
        DateOnly date, double latitude, double longitude, WidgetSettings settings, CancellationToken ct = default)
    {
        var key = CacheKey(date, latitude, longitude, settings);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Invariant culture: the API expects a Gregorian dd-MM-yyyy date and "." decimals, whatever the user's region.
        var url = string.Create(CultureInfo.InvariantCulture, $"https://api.aladhan.com/v1/timings/{date:dd-MM-yyyy}") +
                  $"?latitude={Inv(latitude)}&longitude={Inv(longitude)}&school={settings.School}" +
                  (settings.Method is int m ? $"&method={m}" : "");

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
        var day = Parse(doc.RootElement, date, settings.School);

        _cache[key] = day;
        SaveCache();
        return day;
    }

    /// <summary>Last cached day for these settings, used to render immediately / offline.</summary>
    public PrayerDay? GetCached(DateOnly date, double latitude, double longitude, WidgetSettings settings) =>
        _cache.TryGetValue(CacheKey(date, latitude, longitude, settings), out var d) ? d : null;

    private static string CacheKey(DateOnly date, double latitude, double longitude, WidgetSettings settings) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd}|{latitude:0.00}|{longitude:0.00}|{settings.CacheKey}");

    internal static PrayerDay Parse(JsonElement root, DateOnly date, int school)
    {
        if (root.GetProperty("code").GetInt32() != 200)
            throw new InvalidOperationException($"Aladhan error: {root.GetProperty("status").GetString()}");

        var data = root.GetProperty("data");
        var timings = data.GetProperty("timings");
        var hijri = data.GetProperty("date").GetProperty("hijri");
        var meta = data.GetProperty("meta");

        var day = new PrayerDay
        {
            Date = date,
            School = school,
            TimeZone = meta.GetProperty("timezone").GetString() ?? "",
            MethodName = meta.TryGetProperty("method", out var method) && method.TryGetProperty("name", out var n)
                ? n.GetString() ?? ""
                : "",
            HijriDay = hijri.GetProperty("day").GetString() ?? "",
            HijriMonth = hijri.GetProperty("month").GetProperty("en").GetString() ?? "",
            HijriYear = hijri.GetProperty("year").GetString() ?? "",
        };

        foreach (var key in Keys)
        {
            if (timings.TryGetProperty(key, out var v) && ParseTime(v.GetString()) is TimeSpan t)
                day.Timings[key] = t;
        }

        return day;
    }

    /// <summary>Parses "05:11" or "05:11 (PKT)".</summary>
    internal static TimeSpan? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var hhmm = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    private ConcurrentDictionary<string, PrayerDay> LoadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, PrayerDay>>(File.ReadAllText(_cachePath));
                if (dict is not null)
                    return new ConcurrentDictionary<string, PrayerDay>(dict);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Cache load failed: {ex.Message}");
        }

        return new ConcurrentDictionary<string, PrayerDay>();
    }

    private void SaveCache()
    {
        try
        {
            // Keep only recent days.
            var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-2));
            foreach (var (k, v) in _cache)
            {
                if (v.Date < cutoff)
                    _cache.TryRemove(k, out _);
            }

            File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache));
        }
        catch (Exception ex)
        {
            Log.Write($"Cache save failed: {ex.Message}");
        }
    }

    private static string Inv(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}
