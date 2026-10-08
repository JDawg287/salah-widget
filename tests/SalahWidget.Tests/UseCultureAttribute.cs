using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace SalahWidget.Tests;

/// <summary>Runs a test (or every test in a class) under a fixed CurrentCulture/CurrentUICulture.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class UseCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    private readonly CultureInfo _culture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
    private CultureInfo? _original;
    private CultureInfo? _originalUi;

    public override void Before(MethodInfo methodUnderTest)
    {
        _original = CultureInfo.CurrentCulture;
        _originalUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    public override void After(MethodInfo methodUnderTest)
    {
        CultureInfo.CurrentCulture = _original!;
        CultureInfo.CurrentUICulture = _originalUi!;
    }
}
