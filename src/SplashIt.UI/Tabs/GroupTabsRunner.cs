using SplashIt.Core.Logging;

namespace SplashIt.UI.Tabs;

/// <summary>The panes found for a set of tabs, or why there are none.</summary>
/// <param name="Panes">The distinct panes, in the order their first tab was given.</param>
/// <param name="Reason">Null when <paramref name="Panes"/> is usable; otherwise why nothing may move.</param>
public readonly record struct DocumentPaneLookup(IReadOnlyList<IDocumentPane> Panes, string? Reason)
{
    public bool IsAvailable => Reason is null;

    public static DocumentPaneLookup Unavailable(string reason) => new([], reason);
}

/// <summary>
/// One Group Tabs click (spec #2), wired from the colorizer's own parts so nothing is found or
/// matched twice:
/// <list type="number">
/// <item>the colorizer's <see cref="ITabSurface"/> finds the main window's document tabs and
/// resolves each to its document (in Revit, <c>RevitTabSurface</c> with the longest-match title
/// rule over current and kept titles);</item>
/// <item>each tab's layout model (its <c>DataContext</c>, else its <c>Content</c>) carries that
/// tab's document key;</item>
/// <item><paramref name="findPanes"/> (in Revit, <c>AvalonDockPane.Finder</c>) turns the models
/// into panes;</item>
/// <item><see cref="TabGrouper"/> groups each pane, keyed by the map from step 2. A pane item
/// with no tab, or a tab the surface left unresolved, has no key and goes last.</item>
/// </list>
/// Call it only while documents are open (the command checks): then finding no tab at all means
/// the tab strip isn't where it is expected, which is <see cref="TabGroupOutcome.Unavailable"/>
/// on the first click, not a silent no-op (review m2; the surface's own grace period for tabs
/// that lag a document's open event is for the colorizer's passes, not for a click).
/// Never throws. UI thread only.
/// </summary>
public static class GroupTabsRunner
{
    public const string NoTabsReason = "Documents are open, but no document tab was found.";

    public static TabGroupResult Run(
        ITabSurface surface,
        Func<IReadOnlyList<object>, DocumentPaneLookup> findPanes,
        ILog? log = null)
    {
        log ??= NullLog.Instance;
        try
        {
            // Documents may have opened, closed or been renamed since the colorizer last looked.
            surface.Invalidate();
            var tabs = new List<SurfaceTab>();
            var read = surface.Read(tabs);
            if (!read.IsAvailable) return Unavailable(read.Reason ?? "The tabs could not be read.", log);
            if (tabs.Count == 0) return Unavailable(NoTabsReason, log);

            var keys = new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
            var models = new List<object>(tabs.Count);
            foreach (var tab in tabs)
            {
                if ((tab.Tab.DataContext ?? tab.Tab.Content) is not { } model || keys.ContainsKey(model)) continue;
                keys[model] = tab.DocumentKey;
                models.Add(model);
            }

            var lookup = findPanes(models);
            if (!lookup.IsAvailable) return Unavailable(lookup.Reason!, log);

            var grouper = new TabGrouper(item => keys.TryGetValue(item, out var key) ? key : null, log);
            return grouper.Group(lookup.Panes);
        }
        catch (Exception ex)
        {
            return Unavailable("Finding the tabs failed (" + ex.GetType().Name + ").", log);
        }
    }

    private static TabGroupResult Unavailable(string reason, ILog log)
    {
        log.Warn("group tabs: unavailable; nothing moved. " + reason);
        return TabGroupResult.Unavailable(reason);
    }
}
