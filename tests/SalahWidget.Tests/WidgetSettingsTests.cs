using System.Text.Json;
using Xunit;

namespace SalahWidget.Tests;

public class WidgetSettingsTests
{
    [Fact]
    public void Defaults_AreAutoMethodAndStandardAsr()
    {
        var s = new WidgetSettings();

        Assert.Null(s.Method);
        Assert.Equal(0, s.School);
        Assert.Equal("auto-0", s.CacheKey);
    }

    [Fact]
    public void CacheKey_DistinguishesMethodAndSchool()
    {
        Assert.Equal("15-1", new WidgetSettings { Method = 15, School = 1 }.CacheKey);
        Assert.NotEqual(new WidgetSettings { Method = 0 }.CacheKey, new WidgetSettings().CacheKey);
    }

    // ---- FromCustomState (persisted per widget by the Widgets host)

    [Fact]
    public void FromCustomState_RoundTripsSerializedSettings()
    {
        var json = JsonSerializer.Serialize(new WidgetSettings { Method = 2, School = 1 });

        var s = WidgetSettings.FromCustomState(json);

        Assert.Equal(2, s.Method);
        Assert.Equal(1, s.School);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("{\"Method\":\"two\"}")]
    public void FromCustomState_MissingOrCorrupt_ReturnsDefaults(string? state)
    {
        var s = WidgetSettings.FromCustomState(state);

        Assert.Null(s.Method);
        Assert.Equal(0, s.School);
    }

    [Fact]
    public void FromCustomState_UnknownPropertiesAreIgnored()
    {
        var s = WidgetSettings.FromCustomState("""{"Method":3,"School":0,"Future":true}""");

        Assert.Equal(3, s.Method);
    }

    // ---- FromCustomizeInputs (Action.Execute payload from the Customize card)

    [Theory]
    [InlineData("""{"method":"auto","school":"0"}""", null, 0)]
    [InlineData("""{"method":"3","school":"1"}""", 3, 1)]
    [InlineData("""{"method":"0","school":"0"}""", 0, 0)]
    [InlineData("""{"method":15,"school":1}""", 15, 1)]
    [InlineData("""{}""", null, 0)]
    [InlineData("""{"school":"1"}""", null, 1)]
    [InlineData("""{"method":"23"}""", 23, 0)]
    public void FromCustomizeInputs_ValidPayloads(string data, int? method, int school)
    {
        var s = WidgetSettings.FromCustomizeInputs(data);

        Assert.NotNull(s);
        Assert.Equal(method, s!.Method);
        Assert.Equal(school, s.School);
    }

    [Theory]
    [InlineData("""{"method":"-1"}""")]
    [InlineData("""{"method":"abc"}""")]
    [InlineData("""{"method":"3.5"}""")]
    [InlineData("""{"method":" 3"}""")]
    [InlineData("""{"method":null}""")]
    public void FromCustomizeInputs_UnrecognisedMethod_FallsBackToAuto(string data)
    {
        Assert.Null(WidgetSettings.FromCustomizeInputs(data)!.Method);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("hanafi")]
    [InlineData("")]
    public void FromCustomizeInputs_UnrecognisedSchool_FallsBackToStandard(string school)
    {
        var s = WidgetSettings.FromCustomizeInputs($$"""{"method":"auto","school":"{{school}}"}""");

        Assert.Equal(0, s!.School);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{broken")]
    [InlineData("[\"3\",\"1\"]")]
    [InlineData("42")]
    [InlineData("null")]
    public void FromCustomizeInputs_NonObjectPayload_ReturnsNull(string? data)
    {
        Assert.Null(WidgetSettings.FromCustomizeInputs(data));
    }
}
