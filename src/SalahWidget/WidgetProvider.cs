using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace SalahWidget;

/// <summary>
/// COM-activated entry point used by the Widgets board. The host may create several
/// instances, so all state lives in the shared <see cref="WidgetController"/>.
/// Every callback must return quickly; real work is queued on the thread pool.
/// </summary>
[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("FB2735C8-EBF7-4172-9A06-DF567D945927")]
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private static WidgetController Controller => WidgetController.Instance;

    public WidgetProvider()
    {
        Controller.RecoverExistingWidgets();
    }

    public void CreateWidget(WidgetContext widgetContext) =>
        Controller.OnCreate(widgetContext, customState: null);

    public void DeleteWidget(string widgetId, string customState) =>
        Controller.OnDelete(widgetId);

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) =>
        Controller.OnAction(actionInvokedArgs.WidgetContext, actionInvokedArgs.Verb, actionInvokedArgs.Data);

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) =>
        Controller.OnContextChanged(contextChangedArgs.WidgetContext);

    public void Activate(WidgetContext widgetContext) =>
        Controller.OnActivate(widgetContext);

    public void Deactivate(string widgetId) =>
        Controller.OnDeactivate(widgetId);

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs) =>
        Controller.OnCustomize(customizationRequestedArgs.WidgetContext);

    /// <summary>Signalled when the last widget is removed so the COM server can exit.</summary>
    public static ManualResetEvent EmptyWidgetListEvent => WidgetController.Instance.EmptyEvent;
}
