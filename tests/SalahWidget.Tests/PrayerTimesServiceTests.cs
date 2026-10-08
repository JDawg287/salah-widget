using System.Net;
using SalahWidget.Services;
using Xunit;

namespace SalahWidget.Tests;

public class PrayerTimesServiceTests
{
    private static readonly DateOnly Date = new(2026, 10, 7);

    private const string OkBody = """
        {
          "code": 200, "status": "OK",
          "data": {
            "timings": { "Fajr": "05:11", "Sunrise": "06:26", "Dhuhr": "12:20", "Asr": "15:43", "Maghrib": "18:13", "Isha": "19:29" },
            "date": { "hijri": { "day": "26", "month": { "en": "Rabīʿ al-thānī" }, "year": "1448" } },
            "meta": { "timezone": "Asia/Karachi", "method": { "id": 1, "name": "Karachi" } }
          }
        }
        """;

    private static (PrayerTimesService Service, FakeHttp Http) Create(TempFile cache, Func<Uri, HttpResponseMessage>? respond = null)
    {
        var http = new FakeHttp(respond ?? (_ => FakeHttp.Json(OkBody)));
        return (new PrayerTimesService(http.Client(), cache.Path), http);
    }

    // ---------------------------------------------------------------- request URL

    [Fact]
    public async Task Url_AutoMethod_OmitsMethodParameter()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings());

        var url = http.Requests.Single().ToString();
        Assert.StartsWith("https://api.aladhan.com/v1/timings/07-10-2026?", url);
        Assert.Contains("latitude=24.86", url);
        Assert.Contains("longitude=67.01", url);
        Assert.Contains("school=0", url);
        Assert.DoesNotContain("method=", url);
    }

    [Fact]
    public async Task Url_ExplicitMethodAndHanafi()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings { Method = 0, School = 1 });

        var url = http.Requests.Single().ToString();
        Assert.Contains("method=0", url);
        Assert.Contains("school=1", url);
    }

    [Fact]
    public async Task Url_NegativeCoordinates()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await svc.GetDayAsync(Date, -33.8688, 151.2093, new WidgetSettings());

        var url = http.Requests.Single().ToString();
        Assert.Contains("latitude=-33.8688", url);
        Assert.Contains("longitude=151.2093", url);
    }

    [Theory]
    [InlineData("de-DE")]   // comma decimal separator
    [InlineData("ar-SA")]   // Umm al-Qura (Hijri) calendar by default
    [InlineData("th-TH")]   // Buddhist calendar (year 2569)
    [InlineData("fa-IR")]   // Persian calendar
    public async Task Url_IsCultureIndependent(string culture)
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await WithCulture(culture, () => svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings()));

        var url = http.Requests.Single().ToString();
        Assert.StartsWith("https://api.aladhan.com/v1/timings/07-10-2026?", url);
        Assert.Contains("latitude=24.86&longitude=67.01", url);
    }

    // ---------------------------------------------------------------- caching

    [Fact]
    public async Task SecondRequest_ForSameDayAndSettings_IsServedFromCache()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        var a = await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings());
        var b = await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings());

        Assert.Single(http.Requests);
        Assert.Same(a, b);
    }

    [Fact]
    public async Task NearbyCoordinates_ShareTheCache()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await svc.GetDayAsync(Date, 24.8601, 67.0101, new WidgetSettings());
        await svc.GetDayAsync(Date, 24.8649, 67.0149, new WidgetSettings()); // same 0.01° cell

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task DifferentSettingsOrDates_AreFetchedSeparately()
    {
        using var cache = new TempFile();
        var (svc, http) = Create(cache);

        await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings());
        await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings { School = 1 });
        await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings { Method = 3 });
        await svc.GetDayAsync(Date.AddDays(1), 24.86, 67.01, new WidgetSettings());

        Assert.Equal(4, http.Requests.Count);
    }

    [Fact]
    public async Task Cache_IsPersisted_AndReloadedByANewInstance()
    {
        using var cache = new TempFile();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (first, _) = Create(cache);
        await first.GetDayAsync(today, 24.86, 67.01, new WidgetSettings());

        var (second, http) = Create(cache, _ => FakeHttp.Throw());
        var cached = second.GetCached(today, 24.86, 67.01, new WidgetSettings());
        var fetched = await second.GetDayAsync(today, 24.86, 67.01, new WidgetSettings());

        Assert.NotNull(cached);
        Assert.Equal(new TimeSpan(15, 43, 0), cached!.Timings["Asr"]);
        Assert.Equal("Asia/Karachi", fetched.TimeZone);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task CacheKey_IsCultureIndependent()
    {
        using var cache = new TempFile();
        var (svc, _) = Create(cache);
        await WithCulture("en-US", () => svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings()));

        var hit = await WithCulture("de-DE", () => Task.FromResult(svc.GetCached(Date, 24.86, 67.01, new WidgetSettings())));

        Assert.NotNull(hit);
    }

    [Fact]
    public void GetCached_Miss_ReturnsNull()
    {
        using var cache = new TempFile();
        var (svc, _) = Create(cache);

        Assert.Null(svc.GetCached(Date, 24.86, 67.01, new WidgetSettings()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task CorruptCacheFile_IsIgnored(string contents)
    {
        using var cache = new TempFile();
        File.WriteAllText(cache.Path, contents);

        var (svc, http) = Create(cache);
        var day = await svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings());

        Assert.Equal("Asia/Karachi", day.TimeZone);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task OldDays_ArePrunedWhenTheCacheIsSaved()
    {
        using var cache = new TempFile();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (svc, _) = Create(cache);
        await svc.GetDayAsync(today.AddDays(-10), 24.86, 67.01, new WidgetSettings());

        await svc.GetDayAsync(today, 24.86, 67.01, new WidgetSettings());

        var reloaded = Create(cache).Service;
        Assert.Null(reloaded.GetCached(today.AddDays(-10), 24.86, 67.01, new WidgetSettings()));
        Assert.NotNull(reloaded.GetCached(today, 24.86, 67.01, new WidgetSettings()));
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task HttpError_Throws_AndCachesNothing()
    {
        using var cache = new TempFile();
        var (svc, _) = Create(cache, _ => FakeHttp.Fail(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings()));
        Assert.Null(svc.GetCached(Date, 24.86, 67.01, new WidgetSettings()));
    }

    [Fact]
    public async Task ApiErrorCode_Throws()
    {
        using var cache = new TempFile();
        var (svc, _) = Create(cache, _ => FakeHttp.Json("""{ "code": 400, "status": "Bad Request", "data": "Invalid" }"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings()));
    }

    [Fact]
    public async Task NonJsonResponse_Throws()
    {
        using var cache = new TempFile();
        var (svc, _) = Create(cache, _ => FakeHttp.Json("<html>maintenance</html>"));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => svc.GetDayAsync(Date, 24.86, 67.01, new WidgetSettings()));
    }

    private static async Task<T> WithCulture<T>(string name, Func<Task<T>> action)
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
        try
        {
            return await action();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
