using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using SalahWidget.Services;

namespace SalahWidget;

/// <summary>Process-wide widget state: tracks widget instances, fetches data and pushes card updates.</summary>
internal sealed class WidgetController
{
    private static readonly TimeSpan LocationMaxAge = TimeSpan.FromMinutes(30);

    /// <summary>How long a COM-activated process waits for a widget before exiting when none exist.</summary>
    private static readonly TimeSpan IdleGrace = TimeSpan.FromSeconds(60);

    private static readonly string LocationPath = Path.Combine(AppPaths.DataDir, "location.json");

    // Must stay below the static fields above: static initializers run in declaration order, and the
    // constructor reads IdleGrace and LocationPath.
    public static WidgetController Instance { get; } = new();

    private sealed class WidgetInstance
    {
        public required string Id { get; init; }
        public WidgetSize Size { get; set; }
        public bool IsActive { get; set; }
        public bool IsCustomizing { get; set; }
        public WidgetSettings Settings { get; set; } = new();
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, WidgetInstance> _widgets = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (PrayerDay Today, PrayerDay? Tomorrow)> _data = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly LocationService _locationService;
    private readonly PrayerTimesService _prayerService;
    private readonly Timer _timer;
    private readonly Timer _idleTimer;
    private GeoLocation? _location;
    private string? _lastError;
    private bool _recovered;

    public ManualResetEvent EmptyEvent { get; } = new(false);

    private WidgetController()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SalahWidget/1.0 (Windows Widgets)");
        _locationService = new LocationService(http);
        _prayerService = new PrayerTimesService(http);
        _location = LoadLocation();
        _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        ScheduleTick();

        // The process lives exactly as long as there are widgets: Windows starts it on demand (COM
        // activation) and it exits when the last widget is removed. If it was started but no widget
        // exists or arrives shortly, exit instead of idling forever.
        _idleTimer = new Timer(_ => ExitIfIdle(), null, IdleGrace, Timeout.InfiniteTimeSpan);
    }

    private void ExitIfIdle()
    {
        lock (_gate)
        {
            if (_widgets.Count == 0)
            {
                Log.Write("No widgets; exiting.");
                EmptyEvent.Set();
            }
        }
    }

    // ---------------------------------------------------------------- host callbacks

    public void RecoverExistingWidgets()
    {
        lock (_gate)
        {
            if (_recovered)
                return;
            _recovered = true;
        }

        try
        {
            var infos = WidgetManager.GetDefault().GetWidgetInfos();
            if (infos is null)
                return;
            foreach (var info in infos)
                OnCreate(info.WidgetContext, info.CustomState);
        }
        catch (Exception ex)
        {
            Log.Write($"RecoverExistingWidgets failed: {ex}");
        }
    }

    public void OnCreate(WidgetContext context, string? customState)
    {
        WidgetInstance widget;
        bool isNew, sizeChanged;
        lock (_gate)
        {
            isNew = !_widgets.TryGetValue(context.Id, out widget!);
            if (isNew)
            {
                widget = new WidgetInstance { Id = context.Id, Settings = WidgetSettings.FromCustomState(customState) };
                _widgets[context.Id] = widget;
            }

            sizeChanged = widget.Size != context.Size;
            widget.Size = context.Size;
            widget.IsActive = context.IsActive;
            EmptyEvent.Reset();
        }

        // Startup recovery and the host's own CreateWidget/Activate can report the same widget;
        // only the first sighting needs a render + data refresh.
        if (isNew)
        {
            Log.Write($"Widget added: {context.Id} ({context.Size})");
            Push(widget);
            QueueRefresh(forceLocation: false);
        }
        else if (sizeChanged)
        {
            Push(widget);
        }
    }

    public void OnDelete(string widgetId)
    {
        lock (_gate)
        {
            _widgets.Remove(widgetId);
            if (_widgets.Count == 0)
                EmptyEvent.Set();
        }

        Log.Write($"Widget deleted: {widgetId}");
    }

    public void OnActivate(WidgetContext context)
    {
        var widget = Find(context.Id);
        if (widget is null)
        {
            OnCreate(context, null);
            return;
        }

        widget.IsActive = true;
        widget.Size = context.Size;
        Push(widget);
        if (NeedsRefresh())
            QueueRefresh(forceLocation: false);
    }

    public void OnDeactivate(string widgetId)
    {
        if (Find(widgetId) is { } widget)
            widget.IsActive = false;
    }

    public void OnContextChanged(WidgetContext context)
    {
        if (Find(context.Id) is { } widget)
        {
            widget.Size = context.Size;
            Push(widget);
        }
    }

    public void OnCustomize(WidgetContext context)
    {
        if (Find(context.Id) is { } widget)
        {
            widget.IsCustomizing = true;
            Push(widget);
        }
    }

    public void OnAction(WidgetContext context, string verb, string data)
    {
        var widget = Find(context.Id);
        if (widget is null)
            return;

        switch (verb)
        {
            case WidgetVerbs.Refresh:
                QueueRefresh(forceLocation: true);
                break;

            case WidgetVerbs.Save:
                widget.Settings = ParseCustomizeInputs(data, widget.Settings);
                widget.IsCustomizing = false;
                Push(widget);
                QueueRefresh(forceLocation: false);
                break;

            case WidgetVerbs.Cancel:
                widget.IsCustomizing = false;
                Push(widget);
                break;

            default:
                Log.Write($"Unknown action verb '{verb}'");
                break;
        }
    }

    // ---------------------------------------------------------------- data refresh

    private void QueueRefresh(bool forceLocation) =>
        _ = Task.Run(() => RefreshAsync(forceLocation));

    private bool NeedsRefresh()
    {
        if (_location is null || DateTimeOffset.UtcNow - _location.FetchedAt > LocationMaxAge)
            return true;

        lock (_gate)
        {
            foreach (var w in _widgets.Values)
            {
                if (!_data.TryGetValue(w.Settings.CacheKey, out var d))
                    return true;
                var locationToday = DateOnly.FromDateTime(WidgetDataBuilder.NowAt(d.Today).Date);
                if (d.Today.Date != locationToday)
                    return true;
            }
        }

        return false;
    }

    private async Task RefreshAsync(bool forceLocation)
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (forceLocation || _location is null || DateTimeOffset.UtcNow - _location.FetchedAt > LocationMaxAge)
            {
                try
                {
                    _location = await _locationService.GetLocationAsync();
                    SaveLocation(_location);
                    // Coarse coordinates only (~1 km), so a shared log doesn't pinpoint the user.
                    Log.Write(string.Create(CultureInfo.InvariantCulture,
                        $"Location: {_location.DisplayName} ({_location.Source}) {_location.Latitude:0.00},{_location.Longitude:0.00}"));
                }
                catch (Exception ex)
                {
                    Log.Write($"Location failed: {ex.Message}");
                }
            }

            if (_location is null)
            {
                _lastError = "Couldn't determine your location. Check your internet connection.";
                PushAll();
                return;
            }

            List<WidgetSettings> settings;
            lock (_gate)
            {
                settings = _widgets.Values.Select(w => w.Settings)
                    .GroupBy(s => s.CacheKey).Select(g => g.First()).ToList();
            }

            foreach (var s in settings)
            {
                try
                {
                    _data[s.CacheKey] = await FetchDaysAsync(_location, s);
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    Log.Write($"Prayer times failed: {ex.Message}");
                    _lastError = "Couldn't reach the prayer times service.";
                }
            }

            PushAll();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<(PrayerDay, PrayerDay?)> FetchDaysAsync(GeoLocation loc, WidgetSettings s)
    {
        var guess = DateOnly.FromDateTime(DateTime.Now);
        var today = await _prayerService.GetDayAsync(guess, loc.Latitude, loc.Longitude, s);

        // The location's calendar day may differ from the device's (e.g. different time zone).
        var locationToday = DateOnly.FromDateTime(WidgetDataBuilder.NowAt(today).Date);
        if (locationToday != today.Date)
            today = await _prayerService.GetDayAsync(locationToday, loc.Latitude, loc.Longitude, s);

        PrayerDay? tomorrow = null;
        try
        {
            tomorrow = await _prayerService.GetDayAsync(locationToday.AddDays(1), loc.Latitude, loc.Longitude, s);
        }
        catch (Exception ex)
        {
            Log.Write($"Tomorrow's times failed: {ex.Message}");
        }

        return (today, tomorrow);
    }

    // ---------------------------------------------------------------- minute ticker

    private void ScheduleTick()
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1).AddSeconds(1);
        _timer.Change(next - now, Timeout.InfiniteTimeSpan);
    }

    private void OnTick()
    {
        try
        {
            bool anyActive;
            lock (_gate)
                anyActive = _widgets.Values.Any(w => w.IsActive);

            if (anyActive)
            {
                if (NeedsRefresh())
                    QueueRefresh(forceLocation: false);
                else
                    PushAll(activeOnly: true);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Tick failed: {ex}");
        }
        finally
        {
            ScheduleTick();
        }
    }

    // ---------------------------------------------------------------- rendering

    private void PushAll(bool activeOnly = false)
    {
        List<WidgetInstance> widgets;
        lock (_gate)
            widgets = _widgets.Values.Where(w => !activeOnly || w.IsActive).ToList();

        foreach (var w in widgets)
            Push(w);
    }

    private void Push(WidgetInstance widget)
    {
        try
        {
            var (template, data) = Render(widget);
            var options = new WidgetUpdateRequestOptions(widget.Id)
            {
                Template = template,
                Data = data.ToJsonString(),
                CustomState = JsonSerializer.Serialize(widget.Settings),
            };
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            Log.Write($"Push failed for {widget.Id}: {ex}");
        }
    }

    private (string Template, JsonObject Data) Render(WidgetInstance widget)
    {
        if (widget.IsCustomizing)
            return (CardTemplates.Customize, WidgetDataBuilder.BuildCustomize(widget.Settings));

        var location = _location;
        if (location is not null && TryGetDays(location, widget.Settings) is { } days)
        {
            var data = WidgetDataBuilder.Build(days.Today, days.Tomorrow, location, widget.Settings);
            return (CardTemplates.ForSize(widget.Size), data);
        }

        return _lastError is { } error
            ? (CardTemplates.Message, WidgetDataBuilder.BuildMessage(error, showRetry: true))
            : (CardTemplates.Message, WidgetDataBuilder.BuildMessage("Finding your location and prayer times…", showRetry: false));
    }

    private (PrayerDay Today, PrayerDay? Tomorrow)? TryGetDays(GeoLocation location, WidgetSettings settings)
    {
        if (_data.TryGetValue(settings.CacheKey, out var d))
            return d;

        // Fall back to the on-disk cache so the card renders instantly after a restart.
        var date = DateOnly.FromDateTime(DateTime.Now);
        var today = _prayerService.GetCached(date, location.Latitude, location.Longitude, settings);
        if (today is null)
            return null;
        return (today, _prayerService.GetCached(date.AddDays(1), location.Latitude, location.Longitude, settings));
    }

    // ---------------------------------------------------------------- helpers

    private WidgetInstance? Find(string id)
    {
        lock (_gate)
            return _widgets.GetValueOrDefault(id);
    }

    private static WidgetSettings ParseCustomizeInputs(string data, WidgetSettings current)
    {
        if (WidgetSettings.FromCustomizeInputs(data) is { } settings)
            return settings;

        Log.Write($"Bad customize data '{data}'");
        return current;
    }

    private static GeoLocation? LoadLocation()
    {
        try
        {
            return File.Exists(LocationPath)
                ? JsonSerializer.Deserialize<GeoLocation>(File.ReadAllText(LocationPath))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void SaveLocation(GeoLocation location)
    {
        try
        {
            File.WriteAllText(LocationPath, JsonSerializer.Serialize(location));
        }
        catch (Exception ex)
        {
            Log.Write($"Save location failed: {ex.Message}");
        }
    }
}
