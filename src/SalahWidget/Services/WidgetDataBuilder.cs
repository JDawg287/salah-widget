using System.Globalization;
using System.Text.Json.Nodes;

namespace SalahWidget.Services;

/// <summary>Turns prayer data into the JSON consumed by the Adaptive Card templates.</summary>
public static class WidgetDataBuilder
{
    private static readonly (string Key, string Arabic, bool IsPrayer)[] Rows =
    [
        ("Fajr", "الفجر", true),
        ("Sunrise", "الشروق", false),
        ("Dhuhr", "الظهر", true),
        ("Asr", "العصر", true),
        ("Maghrib", "المغرب", true),
        ("Isha", "العشاء", true),
    ];

    public static readonly string[] Methods =
    [
        "auto|Auto (best for your location)",
        "3|Muslim World League",
        "2|Islamic Society of North America (ISNA)",
        "1|University of Islamic Sciences, Karachi",
        "4|Umm Al-Qura University, Makkah",
        "5|Egyptian General Authority of Survey",
        "8|Gulf Region",
        "9|Kuwait",
        "10|Qatar",
        "16|Dubai",
        "11|Majlis Ugama Islam Singapura",
        "17|JAKIM (Malaysia)",
        "20|KEMENAG (Indonesia)",
        "13|Diyanet İşleri Başkanlığı (Turkey)",
        "12|Union des Organisations Islamiques de France",
        "14|Spiritual Administration of Muslims of Russia",
        "15|Moonsighting Committee Worldwide",
        "18|Tunisia",
        "19|Algeria",
        "21|Morocco",
        "22|Comunidade Islâmica de Lisboa",
        "23|Ministry of Awqaf, Jordan",
        "7|Institute of Geophysics, University of Tehran",
        "0|Shia Ithna-Ashari (Jafari)",
    ];

    public static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        if (!string.IsNullOrEmpty(ianaId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch (Exception)
            {
                // Fall through to local.
            }
        }

        return TimeZoneInfo.Local;
    }

    /// <summary>Current date/time at the prayer location.</summary>
    public static DateTimeOffset NowAt(PrayerDay? day, DateTimeOffset? utcNow = null) =>
        TimeZoneInfo.ConvertTime(utcNow ?? DateTimeOffset.UtcNow, ResolveTimeZone(day?.TimeZone));

    public static JsonObject Build(
        PrayerDay today, PrayerDay? tomorrow, GeoLocation location, WidgetSettings settings,
        DateTimeOffset? utcNow = null)
    {
        var tz = ResolveTimeZone(today.TimeZone);
        var now = TimeZoneInfo.ConvertTime(utcNow ?? DateTimeOffset.UtcNow, tz);
        var nowTime = now.TimeOfDay;
        var isFriday = now.DayOfWeek == DayOfWeek.Friday;

        // Next prayer: first of the five daily prayers still ahead today, else tomorrow's Fajr.
        string? nextKey = null;
        DateTime nextAt = default;
        foreach (var (key, _, isPrayer) in Rows)
        {
            if (isPrayer && today.Timings.TryGetValue(key, out var t) && t > nowTime)
            {
                nextKey = key;
                nextAt = now.Date + t;
                break;
            }
        }

        var nextIsTomorrow = false;
        if (nextKey is null)
        {
            nextKey = "Fajr";
            nextIsTomorrow = true;
            var fajr = (tomorrow ?? today).Timings.GetValueOrDefault("Fajr");
            nextAt = now.Date.AddDays(1) + fajr;
        }

        var prayers = new JsonArray();
        var cells = new List<JsonObject>();
        foreach (var (key, arabic, _) in Rows)
        {
            if (!today.Timings.TryGetValue(key, out var t))
                continue;

            var isNext = !nextIsTomorrow && key == nextKey;
            var isPast = t <= nowTime;
            var name = DisplayName(key, isFriday);
            var row = new JsonObject
            {
                ["name"] = name,
                ["arabic"] = key == "Dhuhr" && isFriday ? "الجمعة" : arabic,
                ["time"] = FormatTime(t),
                ["color"] = isNext ? "Accent" : "Default",
                ["weight"] = isNext ? "Bolder" : "Default",
                ["subtle"] = isPast && !isNext,
                ["style"] = isNext ? "emphasis" : "default",
            };
            prayers.Add(row);
            cells.Add((JsonObject)row.DeepClone());
        }

        var hijri = $"{today.HijriDay.TrimStart('0')} {today.HijriMonth} {today.HijriYear} AH";

        return new JsonObject
        {
            ["city"] = location.DisplayName,
            ["hijri"] = hijri,
            ["gregorian"] = now.ToString("dddd, d MMMM", GregorianCulture()),
            ["next"] = new JsonObject
            {
                ["name"] = DisplayName(nextKey, nextIsTomorrow ? now.AddDays(1).DayOfWeek == DayOfWeek.Friday : isFriday),
                ["time"] = FormatTime(nextAt.TimeOfDay),
                ["countdown"] = Countdown(Until(nextAt, now, tz)),
                ["label"] = nextIsTomorrow ? "Next · tomorrow" : "Next prayer",
            },
            ["prayers"] = prayers,
            ["row1"] = new JsonArray(cells.Take(3).Select(c => (JsonNode)c).ToArray()),
            ["row2"] = new JsonArray(cells.Skip(3).Take(3).Select(c => (JsonNode)c.DeepClone()).ToArray()),
            ["imsak"] = today.Timings.TryGetValue("Imsak", out var imsak) ? FormatTime(imsak) : "—",
            ["midnight"] = today.Timings.TryGetValue("Midnight", out var mid) ? FormatTime(mid) : "—",
            ["method"] = string.IsNullOrEmpty(today.MethodName) ? "Auto" : today.MethodName,
            ["school"] = settings.School == 1 ? "Hanafi" : "Standard",
            ["approximate"] = location.Source == LocationSource.Approximate,
            ["locationNote"] = location.Source == LocationSource.Approximate
                ? "Approximate location (location access is off)"
                : "Using your device location",
        };
    }

    public static JsonObject BuildMessage(string message, bool showRetry) => new()
    {
        ["message"] = message,
        ["showRetry"] = showRetry,
    };

    public static JsonObject BuildCustomize(WidgetSettings settings)
    {
        var choices = new JsonArray();
        foreach (var m in Methods)
        {
            var parts = m.Split('|', 2);
            choices.Add(new JsonObject { ["title"] = parts[1], ["value"] = parts[0] });
        }

        return new JsonObject
        {
            ["method"] = settings.Method?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            ["school"] = settings.School.ToString(CultureInfo.InvariantCulture),
            ["methods"] = choices,
        };
    }

    /// <summary>
    /// The user's culture, but with the Gregorian calendar: cultures such as ar-SA, fa-IR and th-TH
    /// default to the Hijri, Persian or Buddhist calendar, which would mislabel the Gregorian date.
    /// </summary>
    private static CultureInfo GregorianCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        if (culture.DateTimeFormat.Calendar is GregorianCalendar)
            return culture;

        var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
        if (gregorian is null)
            return CultureInfo.InvariantCulture;

        var clone = (CultureInfo)culture.Clone();
        clone.DateTimeFormat.Calendar = gregorian;
        return clone;
    }

    /// <summary>Real time remaining until a wall-clock time at the location (correct across DST changes).</summary>
    private static TimeSpan Until(DateTime wallClock, DateTimeOffset now, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
            local = local.AddHours(1); // falls in the hour skipped by a spring-forward change
        return new DateTimeOffset(local, tz.GetUtcOffset(local)) - now;
    }

    private static string DisplayName(string key, bool isFriday) =>
        key == "Dhuhr" && isFriday ? "Jumuʿah" : key;

    internal static string FormatTime(TimeSpan t) =>
        DateTime.Today.Add(TimeSpan.FromMinutes(Math.Floor(t.TotalMinutes) % (24 * 60)))
            .ToString("t", CultureInfo.CurrentCulture);

    internal static string Countdown(TimeSpan span)
    {
        if (span <= TimeSpan.FromMinutes(1))
            return "now";
        var total = (int)Math.Ceiling(span.TotalMinutes);
        var h = total / 60;
        var m = total % 60;
        return h > 0 ? $"in {h}h {m:00}m" : $"in {m} min";
    }
}
