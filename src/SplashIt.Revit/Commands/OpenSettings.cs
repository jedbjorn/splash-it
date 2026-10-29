using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SplashIt.Revit.Resources;

namespace SplashIt.Revit.Commands;

/// <summary>
/// The Splash-It ribbon button. For now it shows the colorizer's status in a TaskDialog;
/// the settings window (Lane C) replaces the body. Available with no document open.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenSettings : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var colorizer = App.Colorizer;
        var heading = colorizer is null
            ? RevitStrings.Status_NotStarted
            : RevitStrings.Status(colorizer.Status);

        var dialog = new TaskDialog(RevitStrings.Status_Title)
        {
            MainInstruction = RevitStrings.Status_Heading(heading),
            MainContent = RevitStrings.Status_Details(colorizer?.StyledTabCount ?? 0)
                          + (colorizer?.StatusDetail is { } detail ? "\n\n" + detail : ""),
        };
        dialog.Show();
        return Result.Succeeded;
    }
}

/// <summary>Keeps the Splash-It button enabled everywhere, including with no document open.</summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
