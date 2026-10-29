using System.Globalization;
using SplashIt.Core.Logging;
using SplashIt.Core.Tabs;

namespace SplashIt.UI.Tabs;

/// <summary>What a Group Tabs run did.</summary>
public enum TabGroupOutcome
{
    /// <summary>At least one tab moved; every pane is grouped.</summary>
    Grouped,

    /// <summary>Every pane was already grouped: nothing moved and nothing was touched.</summary>
    AlreadyGrouped,

    /// <summary>The panes could not be found, read, or have an unexpected shape: nothing moved.</summary>
    Unavailable,

    /// <summary>A move failed: the run stopped there, leaving a valid partial order.</summary>
    Failed,
}

/// <summary>The result of <see cref="TabGrouper.Group"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Panes">The panes looked at.</param>
/// <param name="Moves">The moves that were applied (before a failure, if any).</param>
/// <param name="Reason">Why it was unavailable or failed: type names and counts only, never a title.</param>
/// <param name="SelectionKept">False when a pane's selected tab could not be selected again, so
/// the selected view may have changed.</param>
public readonly record struct TabGroupResult(TabGroupOutcome Outcome, int Panes, int Moves, string? Reason, bool SelectionKept = true)
{
    public bool Succeeded => Outcome is TabGroupOutcome.Grouped or TabGroupOutcome.AlreadyGrouped;

    public static TabGroupResult Unavailable(string reason) => new(TabGroupOutcome.Unavailable, 0, 0, reason);

    /// <summary>What the user should be told, chosen from what actually happened (review m1).</summary>
    public TabGroupNotice Notice => Outcome switch
    {
        TabGroupOutcome.Unavailable => TabGroupNotice.CantGroup,
        TabGroupOutcome.Failed when !SelectionKept => TabGroupNotice.StoppedSelectionMayHaveChanged,
        TabGroupOutcome.Failed => TabGroupNotice.Stopped,
        _ => TabGroupNotice.None,
    };
}

/// <summary>The dialog a Group Tabs click ends with, if any.</summary>
public enum TabGroupNotice
{
    /// <summary>It worked (or there was nothing to do): no dialog.</summary>
    None,

    /// <summary>"Can't group tabs on this Revit build": nothing moved.</summary>
    CantGroup,

    /// <summary>Stopped partway: the order is valid, and the selected view is unchanged.</summary>
    Stopped,

    /// <summary>Stopped partway, and the selected tab could not be selected again.</summary>
    StoppedSelectionMayHaveChanged,

    /// <summary>An unexpected error (a bug of ours): a generic message.</summary>
    UnexpectedError,
}

/// <summary>
/// Runs "Group tabs by File" against <see cref="IDocumentPane"/>s (spec #2, Behavior 1–7;
/// decision #28).
/// <list type="bullet">
/// <item><b>Each pane separately</b>: a pane's order comes from its own tabs
/// (<see cref="TabGroupOrder"/>), with the file of the pane's selected tab first (the active
/// file, #28), and its moves go to that pane only. No tab ever moves between panes.</item>
/// <item><b>Selection</b>: each pane's selected tab is remembered before its first move and
/// selected again afterwards if it is no longer the selected one, also after a failure.
/// A pane with no moves is not touched at all.</item>
/// <item><b>Stop on the first failure</b>: a move that throws, or that leaves the pane in a
/// different shape than planned, stops the whole run (<see cref="TabGroupOutcome.Failed"/>, an
/// error). Every move is a whole move, so the tabs are always in a valid order. A pane that
/// can't be read before any move was tried means nothing moved:
/// <see cref="TabGroupOutcome.Unavailable"/>, a warning.</item>
/// <item><b>Never throws</b>: every failure becomes a <see cref="TabGroupResult"/> and one
/// log line with a reason (type names and counts, never a title).</item>
/// </list>
/// UI thread only.
/// </summary>
public sealed class TabGrouper
{
    public const string NoPaneReason = "No document tab pane was found.";

    private readonly Func<object, object?> _keyOf;
    private readonly ILog _log;

    /// <param name="keyOf">The document key of a pane item (a tab), or null when it matches no
    /// document. Keys are compared with their own equality.</param>
    /// <param name="log">Receives one line per run: the counts (info), why it was unavailable
    /// (warning), or why it stopped (error).</param>
    public TabGrouper(Func<object, object?> keyOf, ILog? log = null)
    {
        _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Groups every pane in turn. <see cref="TabGroupOutcome.Unavailable"/> when there is no pane.</summary>
    public TabGroupResult Group(IReadOnlyList<IDocumentPane>? panes)
    {
        var run = new Run();
        try
        {
            if (panes is null || panes.Count == 0) return Report(TabGroupResult.Unavailable(NoPaneReason));

            for (var p = 0; p < panes.Count; p++)
            {
                var failure = GroupPane(panes[p], p, panes.Count, run);
                if (failure is not null) return Report(Stopped(run, panes.Count, failure));
            }

            var outcome = run.Moves > 0 ? TabGroupOutcome.Grouped : TabGroupOutcome.AlreadyGrouped;
            return Report(new TabGroupResult(outcome, panes.Count, run.Moves, null), run.Tabs);
        }
        catch (Exception ex)
        {
            return Report(Stopped(run, 0, "Grouping failed (" + ex.GetType().Name + ")."));
        }
    }

    /// <summary>Nothing tried yet: unavailable (nothing moved). Otherwise failed partway.</summary>
    private static TabGroupResult Stopped(Run run, int panes, string reason) =>
        run.Touched
            ? new TabGroupResult(TabGroupOutcome.Failed, panes, run.Moves, reason, run.SelectionKept)
            : new TabGroupResult(TabGroupOutcome.Unavailable, panes, 0, reason);

    /// <summary>Groups one pane; null when it went well, otherwise why it stopped.</summary>
    private string? GroupPane(IDocumentPane? pane, int index, int paneCount, Run run)
    {
        var where = paneCount > 1 ? Inv($" in pane {index + 1} of {paneCount}") : "";
        if (pane is null) return "A document pane was missing" + where + ".";

        // Read the pane: its tabs, their keys and the selected tab, whose file goes first (#28).
        List<object> items;
        object? selected;
        IReadOnlyList<TabMove> plan;
        try
        {
            var count = pane.Count;
            items = new List<object>(count);
            var keys = new List<object?>(count);
            for (var i = 0; i < count; i++)
            {
                var item = pane.ItemAt(i) ?? throw new InvalidOperationException("A pane returned no tab.");
                items.Add(item);
                keys.Add(_keyOf(item));
            }
            selected = pane.SelectedItem;
            var selectedIndex = selected is null ? -1 : items.FindIndex(item => ReferenceEquals(item, selected));
            plan = TabGroupOrder.Plan(keys, selectedIndex >= 0 ? keys[selectedIndex] : null);
        }
        catch (Exception ex)
        {
            return "Reading the tabs" + where + " failed (" + ex.GetType().Name + ").";
        }

        run.Tabs += items.Count;
        if (plan.Count == 0) return null; // already grouped: nothing is touched

        string? failure = null;
        for (var m = 0; m < plan.Count && failure is null; m++)
        {
            var move = plan[m];
            var item = items[move.From];
            try
            {
                run.Touched = true;
                pane.Move(move.From, move.To);
                items.RemoveAt(move.From);
                items.Insert(move.To, item);
                if (pane.Count != items.Count || !ReferenceEquals(pane.ItemAt(move.To), item))
                    failure = Inv($"Move {m + 1} of {plan.Count}{where} did not put the tab where planned.");
                else
                    run.Moves++;
            }
            catch (Exception ex)
            {
                failure = Inv($"Move {m + 1} of {plan.Count}{where} failed ({ex.GetType().Name}).");
            }
        }

        var restoreFailure = Reselect(pane, selected, where);
        if (restoreFailure is not null) run.SelectionKept = false;
        return failure ?? restoreFailure;
    }

    /// <summary>Selects <paramref name="selected"/> again when a move changed the selection.</summary>
    private static string? Reselect(IDocumentPane pane, object? selected, string where)
    {
        if (selected is null) return null;
        try
        {
            if (ReferenceEquals(pane.SelectedItem, selected)) return null;
            pane.SelectedItem = selected;
            return ReferenceEquals(pane.SelectedItem, selected)
                ? null
                : "The selected tab" + where + " could not be selected again.";
        }
        catch (Exception ex)
        {
            return "Selecting the selected tab again" + where + " failed (" + ex.GetType().Name + ").";
        }
    }

    private TabGroupResult Report(TabGroupResult result, int tabs = 0)
    {
        try
        {
            switch (result.Outcome)
            {
                case TabGroupOutcome.Grouped:
                    _log.Info(Inv($"group tabs: {result.Moves} moves over {tabs} tabs in {result.Panes} panes."));
                    break;
                case TabGroupOutcome.AlreadyGrouped:
                    _log.Info(Inv($"group tabs: already grouped ({tabs} tabs in {result.Panes} panes); nothing moved."));
                    break;
                case TabGroupOutcome.Unavailable:
                    _log.Warn("group tabs: unavailable; nothing moved. " + result.Reason);
                    break;
                default:
                    var selection = result.SelectionKept ? "" : "; the selected view may have changed";
                    _log.Error(Inv($"group tabs: stopped after {result.Moves} moves{selection}. {result.Reason}"));
                    break;
            }
        }
        catch (Exception)
        {
            // A log that throws never fails the command.
        }
        return result;
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>What a run has done so far.</summary>
    private sealed class Run
    {
        public int Moves;
        public int Tabs;

        /// <summary>A move was tried, so the order may have changed.</summary>
        public bool Touched;

        public bool SelectionKept = true;
    }
}
