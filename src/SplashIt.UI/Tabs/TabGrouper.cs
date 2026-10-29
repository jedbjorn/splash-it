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

    /// <summary>The panes could not be found or have an unexpected shape: nothing moved.</summary>
    Unavailable,

    /// <summary>A step failed: the run stopped there, leaving a valid partial order.</summary>
    Failed,
}

/// <summary>The result of <see cref="TabGrouper.Group"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Panes">The panes looked at.</param>
/// <param name="Moves">The moves that were applied (before a failure, if any).</param>
/// <param name="Reason">Why it was unavailable or failed: type names and counts only, never a title.</param>
public readonly record struct TabGroupResult(TabGroupOutcome Outcome, int Panes, int Moves, string? Reason)
{
    public bool Succeeded => Outcome is TabGroupOutcome.Grouped or TabGroupOutcome.AlreadyGrouped;

    public static TabGroupResult Unavailable(string reason) => new(TabGroupOutcome.Unavailable, 0, 0, reason);
}

/// <summary>
/// Runs "Group tabs by File" against <see cref="IDocumentPane"/>s (spec #2, Behavior 1–7).
/// <list type="bullet">
/// <item><b>Each pane separately</b>: a pane's order comes from its own tabs
/// (<see cref="TabGroupOrder"/>), and its moves go to that pane only. No tab ever moves
/// between panes.</item>
/// <item><b>Selection</b>: each pane's selected tab is remembered before its first move and
/// selected again afterwards if it is no longer the selected one, also after a failure.
/// A pane with no moves is not touched at all.</item>
/// <item><b>Stop on the first failure</b>: a move that throws, or that leaves the pane in a
/// different shape than planned, stops the whole run. Every move is a whole move, so the
/// tabs are always in a valid order; only the order changes.</item>
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
        var moves = 0;
        var tabs = 0;
        try
        {
            if (panes is null || panes.Count == 0) return Report(TabGroupResult.Unavailable(NoPaneReason));

            for (var p = 0; p < panes.Count; p++)
            {
                var failure = GroupPane(panes[p], p, panes.Count, ref moves, ref tabs);
                if (failure is not null)
                    return Report(new TabGroupResult(TabGroupOutcome.Failed, panes.Count, moves, failure));
            }

            var outcome = moves > 0 ? TabGroupOutcome.Grouped : TabGroupOutcome.AlreadyGrouped;
            return Report(new TabGroupResult(outcome, panes.Count, moves, null), tabs);
        }
        catch (Exception ex)
        {
            return Report(new TabGroupResult(TabGroupOutcome.Failed, 0, moves, "Grouping failed (" + ex.GetType().Name + ")."));
        }
    }

    /// <summary>Groups one pane; null when it went well, otherwise why it stopped.</summary>
    private string? GroupPane(IDocumentPane? pane, int index, int paneCount, ref int moves, ref int tabs)
    {
        var where = paneCount > 1 ? Inv($" in pane {index + 1} of {paneCount}") : "";
        if (pane is null) return "A document pane was missing" + where + ".";

        // Read the pane: its tabs, their keys and the selected tab.
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
            plan = TabGroupOrder.Plan(keys);
        }
        catch (Exception ex)
        {
            return "Reading the tabs" + where + " failed (" + ex.GetType().Name + ").";
        }

        tabs += items.Count;
        if (plan.Count == 0) return null; // already grouped: nothing is touched

        string? failure = null;
        for (var m = 0; m < plan.Count && failure is null; m++)
        {
            var move = plan[m];
            var item = items[move.From];
            try
            {
                pane.Move(move.From, move.To);
                items.RemoveAt(move.From);
                items.Insert(move.To, item);
                if (pane.Count != items.Count || !ReferenceEquals(pane.ItemAt(move.To), item))
                    failure = Inv($"Move {m + 1} of {plan.Count}{where} did not put the tab where planned.");
                else
                    moves++;
            }
            catch (Exception ex)
            {
                failure = Inv($"Move {m + 1} of {plan.Count}{where} failed ({ex.GetType().Name}).");
            }
        }

        var restoreFailure = Reselect(pane, selected, where);
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
                    _log.Warn("group tabs: unavailable. " + result.Reason);
                    break;
                default:
                    _log.Error(Inv($"group tabs: stopped after {result.Moves} moves. {result.Reason}"));
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
}
