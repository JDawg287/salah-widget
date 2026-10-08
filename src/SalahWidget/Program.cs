using System.Runtime.InteropServices;
using SalahWidget;
using SalahWidget.Com;
using SalahWidget.Services;

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int dwProcessId);

if (args.Contains("--selftest"))
{
    AttachConsole(-1);
    await SelfTest.RunAsync();
    return;
}

if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
{
    WinRT.ComWrappersSupport.InitializeComWrappers();
    Log.Write("COM server starting");

    var hr = Ole32.CoRegisterClassObject(
        typeof(WidgetProvider).GUID,
        new WidgetProviderFactory<WidgetProvider>(),
        Ole32.CLSCTX_LOCAL_SERVER,
        Ole32.REGCLS_MULTIPLEUSE,
        out var cookie);

    if (hr < 0)
    {
        Log.Write($"CoRegisterClassObject failed: 0x{hr:X8}");
        Marshal.ThrowExceptionForHR(hr);
    }

    // Stay alive until every widget instance has been removed from the board.
    WidgetProvider.EmptyWidgetListEvent.WaitOne();

    Ole32.CoRevokeClassObject(cookie);
    Log.Write("COM server exiting");
}

/// <summary>Command-line check of location + Aladhan without the Widgets host.</summary>
internal static class SelfTest
{
    public static async Task RunAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SalahWidget/1.0 (selftest)");

        var location = await new LocationService(http).GetLocationAsync();
        Console.WriteLine($"Location : {location.DisplayName} [{location.Source}] {location.Latitude:0.0000}, {location.Longitude:0.0000}");

        var settings = new WidgetSettings();
        var service = new PrayerTimesService(http);
        var date = DateOnly.FromDateTime(DateTime.Now);
        var today = await service.GetDayAsync(date, location.Latitude, location.Longitude, settings);
        var tomorrow = await service.GetDayAsync(date.AddDays(1), location.Latitude, location.Longitude, settings);

        Console.WriteLine($"Method   : {today.MethodName}");
        Console.WriteLine($"TimeZone : {today.TimeZone}");
        foreach (var (k, v) in today.Timings)
            Console.WriteLine($"  {k,-9}{v:hh\\:mm}");

        var data = WidgetDataBuilder.Build(today, tomorrow, location, settings);
        Console.WriteLine();
        Console.WriteLine(data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
