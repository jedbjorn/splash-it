using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SplashIt.Core.Logging;
using SplashIt.Revit.Resources;
using SplashIt.Revit.Tabs;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Commands;

/// <summary>
/// The Group Tabs ribbon button (spec #2): one click puts each file's tabs next to each other in
/// every pane of the main window, files in the order of their leftmost tab. A one-shot command;
/// nothing keeps sorting afterwards.
/// <list type="bullet">
/// <item><b>Reuse</b>: the tabs and their documents come from the colorizer's own
/// <see cref="RevitTabSurface"/> and <see cref="RevitDocuments"/> (the longest-match title rule
/// over current and kept titles), refreshed here first, so it works the same with coloring on
/// or off. <see cref="AvalonDockPane"/> is the only reach into AvalonDock.</item>
/// <item><b>UI thread, one operation</b>: Revit runs external commands on its UI thread, and
/// everything here runs synchronously inside <see cref="Execute"/>.</item>
/// <item><b>Never throws into Revit</b>: when the tab strip has an unexpected shape a short
/// dialog says so and nothing moves; when a move fails partway it stops and says so. The
/// reason is logged either way. No model data, transaction or undo is involved.</item>
/// </list>
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class GroupTabs : IExternalCommand
{
    private const string NotStartedReason = "Splash-It's tab surface did not start (see the startup lines).";

    /// <summary>One finder for the session, so the AvalonDock version is logged once.</summary>
    private static AvalonDockPane.Finder? _finder;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TabGroupResult result;
        try
        {
            result = Run();
        }
        catch (Exception ex)
        {
            App.Log.Error("group tabs: failed (" + ex.GetType().Name + " in " + (ex.TargetSite?.Name ?? "?") + ").");
            result = TabGroupResult.Unavailable(ex.GetType().Name);
        }

        if (result.Outcome is TabGroupOutcome.Unavailable or TabGroupOutcome.Failed) Tell(result.Outcome);
        return Result.Succeeded;
    }

    private static TabGroupResult Run()
    {
        if (App.Documents is not { } documents || App.TabSurface is not { } surface)
        {
            App.Log.Warn("group tabs: unavailable. " + NotStartedReason);
            return TabGroupResult.Unavailable(NotStartedReason);
        }

        // API context: re-read the open documents' current titles (keeping earlier ones), as the
        // colorizer's events would, in case coloring is off and no event has run.
        documents.GetOpenDocuments(new List<object>());
        if (documents.Count == 0)
        {
            App.Log.Info("group tabs: no document open; nothing to group.");
            return new TabGroupResult(TabGroupOutcome.AlreadyGrouped, 0, 0, null);
        }

        var finder = _finder ??= new AvalonDockPane.Finder(App.Log);
        return GroupTabsRunner.Run(surface, finder.Find, App.Log);
    }

    /// <summary>The short dialog (spec #2, Behavior 7). Never throws.</summary>
    private static void Tell(TabGroupOutcome outcome)
    {
        try
        {
            var stopped = outcome == TabGroupOutcome.Failed;
            var dialog = new TaskDialog(App.PanelName)
            {
                MainInstruction = stopped ? RevitStrings.GroupTabs_Stopped : RevitStrings.GroupTabs_Unavailable,
                MainContent = stopped ? RevitStrings.GroupTabs_StoppedDetail : RevitStrings.GroupTabs_UnavailableDetail,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
        }
        catch (Exception ex)
        {
            App.Log.Error("group tabs: the dialog failed (" + ex.GetType().Name + ").");
        }
    }
}
