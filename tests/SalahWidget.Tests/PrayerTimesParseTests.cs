using System.Text.Json;
using SalahWidget.Services;
using Xunit;

namespace SalahWidget.Tests;

public class PrayerTimesParseTests
{
    private static readonly DateOnly Date = new(2026, 10, 7);

    // Trimmed copy of a real /v1/timings response (Karachi, 7 Oct 2026).
    private const string KarachiJson = """
        {
          "code": 200,
          "status": "OK",
          "data": {
            "timings": {
              "Fajr": "05:11", "Sunrise": "06:26", "Dhuhr": "12:20", "Asr": "15:43",
              "Sunset": "18:13", "Maghrib": "18:13", "Isha": "19:29 (PKT)",
              "Imsak": "05:01", "Midnight": "00:20", "Firstthird": "22:17", "Lastthird": "02:22"
            },
            "date": {
              "readable": "07 Oct 2026",
              "hijri": { "day": "26", "month": { "number": 4, "en": "Rabīʿ al-thānī" }, "year": "1448" }
            },
            "meta": {
              "timezone": "Asia/Karachi",
              "method": { "id": 1, "name": "University of Islamic Sciences, Karachi" },
              "school": "STANDARD"
            }
          }
        }
        """;

    private static PrayerDay Parse(string json, int school = 0)
    {
        using var doc = JsonDocument.Parse(json);
        return PrayerTimesService.Parse(doc.RootElement, Date, school);
    }

    [Fact]
    public void Parse_ReadsTimingsDatesAndMeta()
    {
        var day = Parse(KarachiJson, school: 1);

        Assert.Equal(Date, day.Date);
        Assert.Equal(1, day.School);
        Assert.Equal("Asia/Karachi", day.TimeZone);
        Assert.Equal("University of Islamic Sciences, Karachi", day.MethodName);
        Assert.Equal("26", day.HijriDay);
        Assert.Equal("Rabīʿ al-thānī", day.HijriMonth);
        Assert.Equal("1448", day.HijriYear);

        Assert.Equal(new TimeSpan(5, 11, 0), day.Timings["Fajr"]);
        Assert.Equal(new TimeSpan(6, 26, 0), day.Timings["Sunrise"]);
        Assert.Equal(new TimeSpan(12, 20, 0), day.Timings["Dhuhr"]);
        Assert.Equal(new TimeSpan(15, 43, 0), day.Timings["Asr"]);
        Assert.Equal(new TimeSpan(18, 13, 0), day.Timings["Maghrib"]);
        Assert.Equal(new TimeSpan(5, 1, 0), day.Timings["Imsak"]);
        Assert.Equal(new TimeSpan(0, 20, 0), day.Timings["Midnight"]);
    }

    [Fact]
    public void Parse_StripsTimeZoneSuffixFromTimes()
    {
        var day = Parse(KarachiJson);

        Assert.Equal(new TimeSpan(19, 29, 0), day.Timings["Isha"]);
    }

    [Fact]
    public void Parse_IgnoresTimingsTheWidgetDoesNotUse()
    {
        var day = Parse(KarachiJson);

        Assert.False(day.Timings.ContainsKey("Firstthird"));
        Assert.False(day.Timings.ContainsKey("Lastthird"));
    }

    [Fact]
    public void Parse_NonOkCode_Throws()
    {
        const string json = """{ "code": 400, "status": "Bad Request", "data": "Invalid latitude" }""";

        var ex = Assert.Throws<InvalidOperationException>(() => Parse(json));
        Assert.Contains("Bad Request", ex.Message);
    }

    [Fact]
    public void Parse_MissingMethod_LeavesMethodNameEmpty()
    {
        var json = KarachiJson.Replace(
            "\"method\": { \"id\": 1, \"name\": \"University of Islamic Sciences, Karachi\" },", "");

        var day = Parse(json);

        Assert.Equal("", day.MethodName);
    }

    [Fact]
    public void Parse_MalformedTime_SkipsOnlyThatTiming()
    {
        var json = KarachiJson.Replace("\"Asr\": \"15:43\"", "\"Asr\": \"--:--\"");

        var day = Parse(json);

        Assert.False(day.Timings.ContainsKey("Asr"));
        Assert.Equal(new TimeSpan(12, 20, 0), day.Timings["Dhuhr"]);
        Assert.Equal(new TimeSpan(18, 13, 0), day.Timings["Maghrib"]);
    }

    [Fact]
    public void Parse_MissingTimingKey_IsOmitted()
    {
        var json = KarachiJson.Replace("\"Imsak\": \"05:01\", ", "");

        var day = Parse(json);

        Assert.False(day.Timings.ContainsKey("Imsak"));
        Assert.Equal(new TimeSpan(5, 11, 0), day.Timings["Fajr"]);
    }

    [Theory]
    [InlineData("05:11", 5, 11)]
    [InlineData("00:00", 0, 0)]
    [InlineData("23:59", 23, 59)]
    [InlineData("19:29 (PKT)", 19, 29)]
    [InlineData("04:02 (+03)", 4, 2)]
    [InlineData("  06:15  ", 6, 15)]
    public void ParseTime_ValidValues(string input, int hours, int minutes)
    {
        Assert.Equal(new TimeSpan(hours, minutes, 0), PrayerTimesService.ParseTime(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("(PKT) 05:11")]
    public void ParseTime_InvalidValues_ReturnNull(string? input)
    {
        Assert.Null(PrayerTimesService.ParseTime(input));
    }
}
