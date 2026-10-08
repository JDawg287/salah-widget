using System.Text.Json.Nodes;
using SalahWidget.Services;
using Xunit;

namespace SalahWidget.Tests;

/// <summary>
/// All scenarios use Asia/Karachi (UTC+5, no DST) so the "current time" is fully determined by
/// the injected instant, independent of the machine's time zone. Wednesday 7 Oct 2026.
/// </summary>
[UseCulture("")] // invariant: times format as HH:mm
public class WidgetDataBuilderTests
{
    private const string Karachi = "Asia/Karachi";
    private static readonly TimeSpan Pkt = TimeSpan.FromHours(5);
    private static readonly GeoLocation Precise =
        new(24.86, 67.01, "Karachi", "Pakistan", LocationSource.Precise, DateTimeOffset.UnixEpoch);

    private static readonly (string Key, string Time)[] Wednesday =
    [
        ("Imsak", "05:01"), ("Fajr", "05:11"), ("Sunrise", "06:26"), ("Dhuhr", "12:20"),
        ("Asr", "15:43"), ("Maghrib", "18:13"), ("Isha", "19:29"), ("Midnight", "00:20"),
    ];

    private static PrayerDay Day(int day, params (string Key, string Time)[] timings) => new()
    {
        Date = new DateOnly(2026, 10, day),
        TimeZone = Karachi,
        HijriDay = "26",
        HijriMonth = "Rabīʿ al-thānī",
        HijriYear = "1448",
        MethodName = "University of Islamic Sciences, Karachi",
        Timings = timings.ToDictionary(t => t.Key, t => TimeSpan.Parse(t.Time)),
    };

    private static PrayerDay Today => Day(7, Wednesday);
    private static PrayerDay Tomorrow => Day(8, ("Fajr", "05:12"), ("Dhuhr", "12:20"));

    /// <summary>Karachi wall-clock time on the given October 2026 day.</summary>
    private static DateTimeOffset At(int day, int h, int m, int s = 0) => new(2026, 10, day, h, m, s, Pkt);

    private static JsonObject Build(
        DateTimeOffset now, PrayerDay? today = null, PrayerDay? tomorrow = null,
        GeoLocation? location = null, WidgetSettings? settings = null) =>
        WidgetDataBuilder.Build(today ?? Today, tomorrow, location ?? Precise, settings ?? new WidgetSettings(), now);

    private static JsonObject Row(JsonObject data, string name) =>
        data["prayers"]!.AsArray().Select(r => r!.AsObject()).Single(r => (string)r["name"]! == name);

    private static string Next(JsonObject data, string field) => (string)data["next"]![field]!;

    // ---------------------------------------------------------------- next prayer selection

    [Fact]
    public void MidAfternoon_NextIsAsr()
    {
        var data = Build(At(7, 15, 0), tomorrow: Tomorrow);

        Assert.Equal("Asr", Next(data, "name"));
        Assert.Equal("15:43", Next(data, "time"));
        Assert.Equal("in 43 min", Next(data, "countdown"));
        Assert.Equal("Next prayer", Next(data, "label"));
    }

    [Fact]
    public void BeforeFajr_NextIsTodaysFajr_AndNothingIsPast()
    {
        var data = Build(At(7, 3, 0), tomorrow: Tomorrow);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("05:11", Next(data, "time"));
        Assert.Equal("in 2h 11m", Next(data, "countdown"));
        Assert.All(data["prayers"]!.AsArray(), r => Assert.False((bool)r!["subtle"]!));
    }

    [Fact]
    public void JustAfterMidnight_NextIsTodaysFajr()
    {
        var data = Build(At(7, 0, 1), tomorrow: Tomorrow);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("Next prayer", Next(data, "label"));
    }

    [Fact]
    public void BetweenFajrAndSunrise_SunriseIsNeverTheNextPrayer()
    {
        var data = Build(At(7, 5, 30), tomorrow: Tomorrow);

        Assert.Equal("Dhuhr", Next(data, "name"));
        Assert.Equal("Default", (string)Row(data, "Sunrise")["color"]!);
        Assert.False((bool)Row(data, "Sunrise")["subtle"]!); // still upcoming, just not a prayer
    }

    [Fact]
    public void AtExactPrayerTime_ThatPrayerCountsAsStarted()
    {
        var data = Build(At(7, 15, 43), tomorrow: Tomorrow);

        Assert.Equal("Maghrib", Next(data, "name"));
        Assert.True((bool)Row(data, "Asr")["subtle"]!);
    }

    [Fact]
    public void OneSecondBeforePrayer_CountdownSaysNow()
    {
        var data = Build(At(7, 15, 42, 59), tomorrow: Tomorrow);

        Assert.Equal("Asr", Next(data, "name"));
        Assert.Equal("now", Next(data, "countdown"));
    }

    [Fact]
    public void AfterIsha_NextIsTomorrowsFajr()
    {
        var data = Build(At(7, 21, 0), tomorrow: Tomorrow);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("05:12", Next(data, "time"));
        Assert.Equal("in 8h 12m", Next(data, "countdown"));
        Assert.Equal("Next · tomorrow", Next(data, "label"));
    }

    [Fact]
    public void AfterIsha_NoRowIsHighlighted_AndAllArePast()
    {
        var data = Build(At(7, 23, 30), tomorrow: Tomorrow);

        Assert.All(data["prayers"]!.AsArray(), r =>
        {
            Assert.Equal("Default", (string)r!["color"]!);
            Assert.True((bool)r["subtle"]!);
        });
    }

    [Fact]
    public void AfterIsha_WithoutTomorrowsData_FallsBackToTodaysFajr()
    {
        var data = Build(At(7, 21, 0), tomorrow: null);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("05:11", Next(data, "time"));
        Assert.Equal("in 8h 11m", Next(data, "countdown"));
    }

    [Fact]
    public void AtMidnightBoundary_LastMinuteOfDay_CountsToTomorrowsFajr()
    {
        var data = Build(At(7, 23, 59, 59), tomorrow: Tomorrow);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("in 5h 13m", Next(data, "countdown"));
    }

    // ---------------------------------------------------------------- row flags

    [Fact]
    public void Rows_NextIsAccentedAndBold_PastAreSubtle_FutureArePlain()
    {
        var data = Build(At(7, 15, 0), tomorrow: Tomorrow);

        foreach (var name in new[] { "Fajr", "Sunrise", "Dhuhr" })
        {
            Assert.True((bool)Row(data, name)["subtle"]!);
            Assert.Equal("Default", (string)Row(data, name)["color"]!);
        }

        var asr = Row(data, "Asr");
        Assert.Equal("Accent", (string)asr["color"]!);
        Assert.Equal("Bolder", (string)asr["weight"]!);
        Assert.Equal("emphasis", (string)asr["style"]!);
        Assert.False((bool)asr["subtle"]!);

        foreach (var name in new[] { "Maghrib", "Isha" })
        {
            Assert.False((bool)Row(data, name)["subtle"]!);
            Assert.Equal("Default", (string)Row(data, name)["color"]!);
        }
    }

    [Fact]
    public void Rows_AreInChronologicalOrder_AndSplitIntoTwoGridRows()
    {
        var data = Build(At(7, 15, 0), tomorrow: Tomorrow);

        var names = data["prayers"]!.AsArray().Select(r => (string)r!["name"]!).ToArray();
        Assert.Equal(["Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha"], names);
        Assert.Equal(["Fajr", "Sunrise", "Dhuhr"], data["row1"]!.AsArray().Select(r => (string)r!["name"]!));
        Assert.Equal(["Asr", "Maghrib", "Isha"], data["row2"]!.AsArray().Select(r => (string)r!["name"]!));
    }

    [Fact]
    public void MissingTiming_IsSkippedForRowsAndNextPrayer()
    {
        var today = Day(7, Wednesday.Where(t => t.Key != "Asr").ToArray());

        var data = Build(At(7, 15, 0), today, Tomorrow);

        Assert.Equal("Maghrib", Next(data, "name"));
        Assert.Equal(5, data["prayers"]!.AsArray().Count);
        Assert.Equal(3, data["row1"]!.AsArray().Count);
        Assert.Equal(2, data["row2"]!.AsArray().Count);
    }

    [Fact]
    public void MissingImsakAndMidnight_ShowDash()
    {
        var today = Day(7, Wednesday.Where(t => t.Key is not ("Imsak" or "Midnight")).ToArray());

        var data = Build(At(7, 15, 0), today, Tomorrow);

        Assert.Equal("—", (string)data["imsak"]!);
        Assert.Equal("—", (string)data["midnight"]!);
    }

    [Fact]
    public void EmptyDay_DoesNotThrow_AndRollsToTomorrow()
    {
        var data = Build(At(7, 15, 0), Day(7), Tomorrow);

        Assert.Empty(data["prayers"]!.AsArray());
        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("Next · tomorrow", Next(data, "label"));
    }

    // ---------------------------------------------------------------- Friday / Jumu'ah

    [Fact]
    public void Friday_DhuhrIsShownAsJumuah()
    {
        var friday = Day(9, Wednesday);

        var data = Build(At(9, 10, 0), friday, Tomorrow);

        Assert.Equal("Jumuʿah", Next(data, "name"));
        var row = Row(data, "Jumuʿah");
        Assert.Equal("الجمعة", (string)row["arabic"]!);
        Assert.DoesNotContain(data["prayers"]!.AsArray(), r => (string)r!["name"]! == "Dhuhr");
    }

    [Fact]
    public void Thursday_DhuhrIsNotRenamed()
    {
        var thursday = Day(8, Wednesday);

        var data = Build(At(8, 10, 0), thursday, Tomorrow);

        Assert.Equal("Dhuhr", Next(data, "name"));
    }

    [Fact]
    public void ThursdayNight_NextIsFridayFajr_NotJumuah()
    {
        var thursday = Day(8, Wednesday);

        var data = Build(At(8, 22, 0), thursday, Day(9, Wednesday));

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("Next · tomorrow", Next(data, "label"));
    }

    // ---------------------------------------------------------------- header / footer fields

    [Fact]
    public void Hijri_DropsLeadingZeroFromDay()
    {
        var today = Today;
        today.HijriDay = "05";

        var data = Build(At(7, 15, 0), today, Tomorrow);

        Assert.Equal("5 Rabīʿ al-thānī 1448 AH", (string)data["hijri"]!);
    }

    [Fact]
    public void EmptyMethodName_ShowsAuto()
    {
        var today = Today;
        today.MethodName = "";

        Assert.Equal("Auto", (string)Build(At(7, 15, 0), today)["method"]!);
    }

    [Theory]
    [InlineData(0, "Standard")]
    [InlineData(1, "Hanafi")]
    public void SchoolLabel(int school, string expected)
    {
        var data = Build(At(7, 15, 0), settings: new WidgetSettings { School = school });

        Assert.Equal(expected, (string)data["school"]!);
    }

    [Fact]
    public void ApproximateLocation_FlagsTheHint()
    {
        var approx = Precise with { Source = LocationSource.Approximate };

        var data = Build(At(7, 15, 0), location: approx);

        Assert.True((bool)data["approximate"]!);
        Assert.Contains("Approximate", (string)data["locationNote"]!);
    }

    [Fact]
    public void PreciseLocation_DoesNotFlagTheHint()
    {
        Assert.False((bool)Build(At(7, 15, 0))["approximate"]!);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-SA")] // defaults to the Umm al-Qura (Hijri) calendar
    [InlineData("fa-IR")] // defaults to the Persian calendar
    [InlineData("th-TH")] // defaults to the Buddhist calendar
    public void GregorianDate_StaysGregorian_InCulturesWithOtherDefaultCalendars(string name)
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        var culture = new System.Globalization.CultureInfo(name);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        try
        {
            var data = Build(At(7, 15, 0), tomorrow: Tomorrow);

            var gregorian = (System.Globalization.CultureInfo)culture.Clone();
            gregorian.DateTimeFormat.Calendar = culture.OptionalCalendars.OfType<System.Globalization.GregorianCalendar>().First();
            Assert.Equal(new DateTime(2026, 10, 7).ToString("dddd, d MMMM", gregorian), (string)data["gregorian"]!);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    // ---------------------------------------------------------------- time zones

    [Fact]
    public void UsesTheLocationsTimeZone_NotTheMachines()
    {
        // 10:00 UTC is 15:00 in Karachi, whatever zone the test machine is in.
        var data = Build(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero), tomorrow: Tomorrow);

        Assert.Equal("Asr", Next(data, "name"));
        Assert.Equal("in 43 min", Next(data, "countdown"));
    }

    [Fact]
    public void ResolveTimeZone_KnownIanaId()
    {
        var tz = WidgetDataBuilder.ResolveTimeZone(Karachi);

        Assert.Equal(Pkt, tz.GetUtcOffset(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void ResolveTimeZone_UnknownOrMissing_FallsBackToLocal(string? id)
    {
        Assert.Equal(TimeZoneInfo.Local, WidgetDataBuilder.ResolveTimeZone(id));
    }

    [Fact]
    public void DstTransition_CountdownUsesRealElapsedTime()
    {
        // London springs forward at 01:00 GMT on 29 Mar 2026; at 00:30 GMT Fajr (05:00 BST) is 3h30m away, not 4h30m.
        var london = new PrayerDay
        {
            Date = new DateOnly(2026, 3, 29),
            TimeZone = "Europe/London",
            Timings = new() { ["Fajr"] = new TimeSpan(5, 0, 0), ["Dhuhr"] = new TimeSpan(13, 0, 0) },
        };
        var now = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);

        var data = Build(now, london);

        Assert.Equal("Fajr", Next(data, "name"));
        Assert.Equal("in 3h 30m", Next(data, "countdown"));
    }

    [Fact]
    public void DstTransition_TimeInsideSkippedHour_DoesNotThrow()
    {
        // 01:30 doesn't exist in London on 29 Mar 2026; treat it as 02:30 BST.
        var london = new PrayerDay
        {
            Date = new DateOnly(2026, 3, 29),
            TimeZone = "Europe/London",
            Timings = new() { ["Fajr"] = new TimeSpan(1, 30, 0) },
        };
        var now = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);

        var data = Build(now, london);

        Assert.Equal("in 1h 00m", Next(data, "countdown"));
    }

    [Fact]
    public void DstFallBack_CountdownIncludesRepeatedHour()
    {
        // London clocks go back at 02:00 BST on 25 Oct 2026; from 00:30 BST to 06:00 GMT is 6h30m of real time.
        var london = new PrayerDay
        {
            Date = new DateOnly(2026, 10, 25),
            TimeZone = "Europe/London",
            Timings = new() { ["Fajr"] = new TimeSpan(6, 0, 0) },
        };
        var now = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.FromHours(1));

        var data = Build(now, london);

        Assert.Equal("in 6h 30m", Next(data, "countdown"));
    }

    // ---------------------------------------------------------------- formatting helpers

    [Theory]
    [InlineData(0, "now")]
    [InlineData(-300, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "now")]
    [InlineData(61, "in 2 min")]
    [InlineData(43 * 60, "in 43 min")]
    [InlineData(59 * 60 + 30, "in 1h 00m")]
    [InlineData(60 * 60, "in 1h 00m")]
    [InlineData(65 * 60, "in 1h 05m")]
    [InlineData(23 * 3600 + 59 * 60, "in 23h 59m")]
    public void Countdown(int seconds, string expected)
    {
        Assert.Equal(expected, WidgetDataBuilder.Countdown(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("05:11:00", "05:11")]
    [InlineData("05:11:59", "05:11")]
    [InlineData("00:00:00", "00:00")]
    [InlineData("23:59:00", "23:59")]
    [InlineData("1.00:20:00", "00:20")]
    public void FormatTime_Invariant(string span, string expected)
    {
        Assert.Equal(expected, WidgetDataBuilder.FormatTime(TimeSpan.Parse(span)));
    }

    [Theory]
    [UseCulture("en-US")]
    [InlineData("05:11", "5:11 AM")]
    [InlineData("12:00", "12:00 PM")]
    [InlineData("00:20", "12:20 AM")]
    [InlineData("19:29", "7:29 PM")]
    public void FormatTime_FollowsUserCulture_12Hour(string span, string expected)
    {
        // ICU may use a narrow no-break space before AM/PM.
        var actual = WidgetDataBuilder.FormatTime(TimeSpan.Parse(span)).Replace('\u202F', ' ').Replace('\u00A0', ' ');
        Assert.Equal(expected, actual);
    }

    [Theory]
    [UseCulture("en-GB")]
    [InlineData("05:11", "05:11")]
    [InlineData("19:29", "19:29")]
    public void FormatTime_FollowsUserCulture_24Hour(string span, string expected)
    {
        Assert.Equal(expected, WidgetDataBuilder.FormatTime(TimeSpan.Parse(span)));
    }
}
