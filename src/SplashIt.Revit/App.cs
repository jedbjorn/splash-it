using Autodesk.Revit.UI;

namespace SplashIt.Revit;

/// <summary>
/// Revit entry point. A skeleton for now: the ribbon panel, theme-aware icons,
/// the settings read and the tab colorizer start are added by the colorizer lane.
/// </summary>
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application) => Result.Succeeded;

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
