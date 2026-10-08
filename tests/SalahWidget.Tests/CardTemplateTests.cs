using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Windows.Widgets;
using SalahWidget.Services;
using Xunit;

namespace SalahWidget.Tests;

/// <summary>
/// Checks the embedded Adaptive Card templates against the data the code actually produces, so a
/// renamed data field or a misspelled ${binding} fails here instead of rendering blank on the board.
/// </summary>
[UseCulture("")]
public partial class CardTemplateTests
{
    private static readonly string[] AllowedActions = ["Action.Execute", "Action.OpenUrl"];

    public static TheoryData<string> AllTemplates => new() { "Small", "Medium", "Large", "Customize", "Message" };

    private static string Template(string name) => name switch
    {
        "Small" => CardTemplates.Small,
        "Medium" => CardTemplates.Medium,
        "Large" => CardTemplates.Large,
        "Customize" => CardTemplates.Customize,
        "Message" => CardTemplates.Message,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // ---------------------------------------------------------------- structure

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Template_IsAnAdaptiveCard15(string name)
    {
        var card = JsonNode.Parse(Template(name))!.AsObject();

        Assert.Equal("AdaptiveCard", (string)card["type"]!);
        Assert.Equal("1.5", (string)card["version"]!);
        Assert.NotEmpty(card["body"]!.AsArray());
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Template_UsesOnlyActionsTheWidgetsBoardSupports(string name)
    {
        foreach (var action in Descendants(JsonNode.Parse(Template(name))!).Where(IsAction))
            Assert.Contains((string)action["type"]!, AllowedActions);
    }

    [Fact]
    public void EveryExecuteVerb_IsHandled_AndEveryHandledVerbIsUsed()
    {
        string[] names = ["Small", "Medium", "Large", "Customize", "Message"];
        var used = names.SelectMany(t => Descendants(JsonNode.Parse(Template(t))!))
            .Where(n => (string?)n["type"] == "Action.Execute")
            .Select(n => (string)n["verb"]!)
            .ToHashSet();

        Assert.Subset(WidgetVerbs.All.ToHashSet(), used);
        Assert.Superset(WidgetVerbs.All.ToHashSet(), used);
    }

    [Fact]
    public void ForSize_MapsEverySize()
    {
        Assert.Same(CardTemplates.Small, CardTemplates.ForSize(WidgetSize.Small));
        Assert.Same(CardTemplates.Medium, CardTemplates.ForSize(WidgetSize.Medium));
        Assert.Same(CardTemplates.Large, CardTemplates.ForSize(WidgetSize.Large));
    }

    // ---------------------------------------------------------------- bindings resolve against real data

    public static TheoryData<string, string> PrayerCardScenarios => new()
    {
        { "Small", "afternoon" }, { "Small", "afterIsha" }, { "Small", "approximate" },
        { "Medium", "afternoon" }, { "Medium", "afterIsha" }, { "Medium", "approximate" },
        { "Large", "afternoon" }, { "Large", "afterIsha" }, { "Large", "approximate" },
    };

    [Theory]
    [MemberData(nameof(PrayerCardScenarios))]
    public void PrayerCard_AllBindingsResolve(string template, string scenario)
    {
        AssertBindingsResolve(Template(template), BuildPrayerData(scenario));
    }

    [Fact]
    public void CustomizeCard_AllBindingsResolve()
    {
        AssertBindingsResolve(CardTemplates.Customize, WidgetDataBuilder.BuildCustomize(new WidgetSettings { Method = 3, School = 1 }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MessageCard_AllBindingsResolve(bool showRetry)
    {
        AssertBindingsResolve(CardTemplates.Message, WidgetDataBuilder.BuildMessage("msg", showRetry));
    }

    // ---------------------------------------------------------------- customize card ↔ settings parser

    [Fact]
    public void CustomizeCard_InputIds_AreWhatTheSettingsParserReads()
    {
        var ids = Descendants(JsonNode.Parse(CardTemplates.Customize)!)
            .Where(n => ((string?)n["type"])?.StartsWith("Input.") == true)
            .Select(n => (string)n["id"]!)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "method", "school" }, ids);
    }

    [Fact]
    public void EveryMethodChoice_RoundTripsThroughTheSettingsParser()
    {
        var choices = WidgetDataBuilder.BuildCustomize(new WidgetSettings())["methods"]!.AsArray();

        foreach (var value in choices.Select(c => (string)c!["value"]!))
        {
            var parsed = WidgetSettings.FromCustomizeInputs($$"""{"method":"{{value}}","school":"0"}""");
            Assert.Equal(value == "auto" ? null : int.Parse(value), parsed!.Method);
        }
    }

    [Fact]
    public void MethodChoices_HaveUniqueValuesAndTitles()
    {
        var choices = WidgetDataBuilder.BuildCustomize(new WidgetSettings())["methods"]!.AsArray();

        Assert.Equal(choices.Count, choices.Select(c => (string)c!["value"]!).Distinct().Count());
        Assert.Equal(choices.Count, choices.Select(c => (string)c!["title"]!).Distinct().Count());
        Assert.Equal("auto", (string)choices[0]!["value"]!);
    }

    [Fact]
    public void SchoolChoices_MatchTheSettingsParser()
    {
        var school = Descendants(JsonNode.Parse(CardTemplates.Customize)!).Single(n => (string?)n["id"] == "school");
        var values = school["choices"]!.AsArray().Select(c => (string)c!["value"]!).ToArray();

        Assert.Equal(["0", "1"], values);
        Assert.Equal(1, WidgetSettings.FromCustomizeInputs("""{"school":"1"}""")!.School);
    }

    [Fact]
    public void CustomizeCard_PreselectsCurrentSettings()
    {
        var data = WidgetDataBuilder.BuildCustomize(new WidgetSettings { Method = 15, School = 1 });

        Assert.Equal("15", (string)data["method"]!);
        Assert.Equal("1", (string)data["school"]!);
        Assert.Equal("auto", (string)WidgetDataBuilder.BuildCustomize(new WidgetSettings())["method"]!);
    }

    // ---------------------------------------------------------------- helpers

    private static JsonObject BuildPrayerData(string scenario)
    {
        var day = new PrayerDay
        {
            Date = new DateOnly(2026, 10, 7),
            TimeZone = "Asia/Karachi",
            HijriDay = "26", HijriMonth = "Rabīʿ al-thānī", HijriYear = "1448",
            MethodName = "Karachi",
            Timings = new()
            {
                ["Imsak"] = new(5, 1, 0), ["Fajr"] = new(5, 11, 0), ["Sunrise"] = new(6, 26, 0), ["Dhuhr"] = new(12, 20, 0),
                ["Asr"] = new(15, 43, 0), ["Maghrib"] = new(18, 13, 0), ["Isha"] = new(19, 29, 0), ["Midnight"] = new(0, 20, 0),
            },
        };
        var source = scenario == "approximate" ? LocationSource.Approximate : LocationSource.Precise;
        var location = new GeoLocation(24.86, 67.01, "Karachi", "Pakistan", source, DateTimeOffset.UnixEpoch);
        var hour = scenario == "afterIsha" ? 21 : 15;
        var now = new DateTimeOffset(2026, 10, 7, hour, 0, 0, TimeSpan.FromHours(5));
        return WidgetDataBuilder.Build(day, day, location, new WidgetSettings(), now);
    }

    private static bool IsAction(JsonNode n) => ((string?)n["type"])?.StartsWith("Action.") == true;

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                yield return obj;
                foreach (var (_, child) in obj)
                    if (child is not null)
                        foreach (var d in Descendants(child))
                            yield return d;
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null)
                        foreach (var d in Descendants(child))
                            yield return d;
                break;
        }
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex BindingRegex();

    /// <summary>
    /// Walks the template like the Adaptive Card templating engine: "$data" switches the scope
    /// (repeating the element for arrays), and every ${path} must resolve in the current scope.
    /// </summary>
    private static void AssertBindingsResolve(string template, JsonObject data)
    {
        var errors = new List<string>();
        Walk(JsonNode.Parse(template)!, data, "$", errors);
        Assert.True(errors.Count == 0, "Unresolved bindings:\n" + string.Join("\n", errors));
    }

    private static void Walk(JsonNode node, JsonNode scope, string path, List<string> errors)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$data"] is JsonValue dataExpr)
                {
                    var target = ResolveExpression((string)dataExpr!, scope, $"{path}.$data", errors);
                    if (target is JsonArray items)
                    {
                        foreach (var item in items)
                            WalkProperties(obj, item!, path, errors);
                        return;
                    }

                    if (target is not null)
                        scope = target;
                }

                WalkProperties(obj, scope, path, errors);
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    if (arr[i] is { } child)
                        Walk(child, scope, $"{path}[{i}]", errors);
                break;

            case JsonValue value when value.TryGetValue<string>(out var s):
                foreach (Match m in BindingRegex().Matches(s))
                    ResolvePath(m.Groups[1].Value, scope, path, errors);
                break;
        }
    }

    private static void WalkProperties(JsonObject obj, JsonNode scope, string path, List<string> errors)
    {
        foreach (var (key, child) in obj)
            if (key != "$data" && child is not null)
                Walk(child, scope, $"{path}.{key}", errors);
    }

    private static JsonNode? ResolveExpression(string expr, JsonNode scope, string path, List<string> errors)
    {
        var m = BindingRegex().Match(expr);
        if (!m.Success)
        {
            errors.Add($"{path}: '$data' is not a binding: {expr}");
            return null;
        }

        return ResolvePath(m.Groups[1].Value, scope, path, errors);
    }

    private static JsonNode? ResolvePath(string expr, JsonNode scope, string path, List<string> errors)
    {
        JsonNode? current = scope;
        foreach (var segment in expr.Trim().Split('.'))
        {
            if (current is JsonObject o && o.ContainsKey(segment))
            {
                current = o[segment];
                continue;
            }

            errors.Add($"{path}: ${{{expr}}} does not resolve");
            return null;
        }

        return current;
    }
}
