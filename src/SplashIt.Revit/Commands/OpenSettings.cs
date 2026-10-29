using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SplashIt.Core.Logging;
using SplashIt.Revit.Resources;
using SplashIt.Revit.Theming;
using SplashIt.UI.Settings;
using SplashIt.UI.Tabs;
using SplashIt.UI.Theming;

namespace SplashIt.Revit.Commands;

/// <summary>
/// The Splash-It ribbon button: opens the settings window (Lane C's <see cref="SettingsLauncher"/>)
/// modally, owned by Revit's main window, in Revit's current theme. The window talks to the
/// colorizer through <see cref="ColorizerSettingsHost"/>. Available with no document open.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenSettings : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var theme = RevitThemeDetector.Detect();
            SplashItTheme.Apply(theme);
            var host = new ColorizerSettingsHost(() => App.Colorizer, App.Log);
            SettingsLauncher.Show(commandData.Application.MainWindowHandle, host, theme, log: App.Log.AsAction());
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            // Nothing in Revit changed; Revit shows this message.
            App.Log.Error("settings: the window failed (" + ex.GetType().Name + " in " + (ex.TargetSite?.Name ?? "?") + ").");
            message = RevitStrings.Command_CouldNotOpen(ex.GetType().Name);
            return Result.Failed;
        }
    }
}

/// <summary>Keeps the Splash-It button enabled everywhere, including with no document open.</summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
