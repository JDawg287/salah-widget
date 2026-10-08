using System.Reflection;
using Microsoft.Windows.Widgets;

namespace SalahWidget;

/// <summary>Adaptive Card templates embedded from Templates/*.json.</summary>
internal static class CardTemplates
{
    public static readonly string Small = Load("Small");
    public static readonly string Medium = Load("Medium");
    public static readonly string Large = Load("Large");
    public static readonly string Customize = Load("Customize");
    public static readonly string Message = Load("Message");

    public static string ForSize(WidgetSize size) => size switch
    {
        WidgetSize.Small => Small,
        WidgetSize.Large => Large,
        _ => Medium,
    };

    private static string Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"SalahWidget.Templates.{name}.json")
            ?? throw new InvalidOperationException($"Missing template {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Action.Execute verbs used by the card templates and handled by the controller.</summary>
internal static class WidgetVerbs
{
    public const string Refresh = "refresh";
    public const string Save = "save";
    public const string Cancel = "cancel";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Refresh, Save, Cancel };
}
