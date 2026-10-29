using SplashIt.Core.Tabs;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// Spec #2 AC2: the grouper puts the selected tab's file first (#28), restores the selection,
/// stops on the first failure without throwing (leaving a valid order and a logged reason),
/// reports Unavailable when the pane is missing or can't be read, and never moves a tab across
/// panes. Driven against <see cref="FakePane"/>, whose
/// selection is by index by default, so a move really does change the selected tab unless
/// the grouper puts it back.
/// </summary>
public class TabGrouperTests
{
    private sealed record Tab(string Name)
    {
        public object? Key => Name[0] == '_' ? null : Name[..1];

        public override string ToString() => Name;
    }

    private static object? KeyOf(object item) => ((Tab)item).Key;

    private static List<Tab> Tabs(string tabs) => tabs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => new Tab(t)).ToList();

    private static string Names(FakePane pane) => string.Join(" ", pane.Items);

    /// <summary>An <see cref="IDocumentPane"/> over a list, recording every call.</summary>
    private sealed class FakePane : IDocumentPane
    {
        private int _selectedIndex = -1;
        private object? _selectedItem;

        public FakePane(IEnumerable<Tab> tabs, string? selected = null, bool selectionFollowsItem = false)
        {
            Items = tabs.Cast<object>().ToList();
            SelectionFollowsItem = selectionFollowsItem;
            if (selected is not null) SelectedItem = Items.Single(t => ((Tab)t).Name == selected);
            SelectSets = 0;
        }

        public List<object> Items { get; }

        /// <summary>False: selection is an index that a move does not update (the selected tab changes). True: it follows the tab.</summary>
        public bool SelectionFollowsItem { get; }

        public List<(int From, int To)> MoveCalls { get; } = [];

        public int SelectSets { get; private set; }

        /// <summary>The 1-based Move call that throws (and does not move).</summary>
        public int? ThrowOnMove { get; init; }

        /// <summary>Move inserts one place further than asked (another index convention).</summary>
        public bool Misplace { get; init; }

        public Exception? ThrowOnCount { get; init; }

        public int? ThrowOnItemAt { get; init; }

        public bool ThrowOnGetSelected { get; init; }

        public bool ThrowOnSetSelected { get; init; }

        /// <summary>Setting the selection is silently ignored.</summary>
        public bool IgnoreSetSelected { get; init; }

        /// <summary>
        /// What AvalonDock does when a strip overflows: a selected tab that is past the visible
        /// tabs moves to index 0 and stays selected. Not a Move call (AvalonDock does it itself).
        /// </summary>
        public void PullSelectedToFrontIfHidden(int visible)
        {
            var selected = SelectedItem;
            var index = selected is null ? -1 : Items.IndexOf(selected);
            if (index < visible) return;
            Items.RemoveAt(index);
            Items.Insert(0, selected!);
            _selectedItem = selected;
            _selectedIndex = 0;
        }

        public int Count => ThrowOnCount is null ? Items.Count : throw ThrowOnCount;

        public object ItemAt(int index) => index == ThrowOnItemAt ? throw new IndexOutOfRangeException() : Items[index];

        public void Move(int from, int to)
        {
            MoveCalls.Add((from, to));
            if (MoveCalls.Count == ThrowOnMove) throw new InvalidOperationException("boom");
            var item = Items[from];
            Items.RemoveAt(from);
            Items.Insert(Misplace ? Math.Min(to + 1, Items.Count) : to, item);
        }

        public object? SelectedItem
        {
            get
            {
                if (ThrowOnGetSelected) throw new NotSupportedException();
                if (SelectionFollowsItem) return _selectedItem;
                return _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
            }
            set
            {
                if (ThrowOnSetSelected) throw new ArgumentException("no");
                SelectSets++;
                if (IgnoreSetSelected) return;
                _selectedItem = value;
                _selectedIndex = value is null ? -1 : Items.IndexOf(value);
            }
        }
    }

    private static (TabGrouper Grouper, List<string> Log) Grouper()
    {
        var log = new ListLog();
        return (new TabGrouper(KeyOf, log), log.Lines);
    }

    [Fact]
    public void Group_GroupsByFile_AndRestoresTheSelectedTab()
    {
        var pane = new FakePane(Tabs("B1 A1 C1 A2 B2 C2 A3"), selected: "C1"); // index 2 now, 0 after: an index-based selection would lose it
        var selected = pane.SelectedItem;
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        // The selected tab's file first (#28), then the others by their leftmost tab.
        Assert.Equal("C1 C2 B1 B2 A1 A2 A3", Names(pane));
        Assert.Equal(new TabGroupResult(TabGroupOutcome.Grouped, 1, 3, null), result);
        Assert.Equal(3, pane.MoveCalls.Count); // the planner's minimum, applied as planned
        Assert.Same(selected, pane.SelectedItem);
        Assert.Equal(1, pane.SelectSets);
        Assert.Equal("Info group tabs: 3 moves over 7 tabs in 1 panes.", Assert.Single(log));
    }

    [Fact]
    public void Group_WhenThePaneKeepsTheSelectionItself_DoesNotSelectAgain()
    {
        var pane = new FakePane(Tabs("B1 A1 C1 A2 B2"), selected: "A2", selectionFollowsItem: true);
        var selected = pane.SelectedItem;

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal("A1 A2 B1 B2 C1", Names(pane));
        Assert.Same(selected, pane.SelectedItem);
        Assert.Equal(0, pane.SelectSets); // no second activation of the same view
    }

    [Fact]
    public void Group_AlreadyGrouped_MovesNothing_AndTouchesNothing()
    {
        var pane = new FakePane(Tabs("C1 C2 A1 B1 B2 _1"), selected: "C2");
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        Assert.Equal(new TabGroupResult(TabGroupOutcome.AlreadyGrouped, 1, 0, null), result);
        Assert.Empty(pane.MoveCalls);
        Assert.Equal(0, pane.SelectSets);
        Assert.Equal("C1 C2 A1 B1 B2 _1", Names(pane));
        Assert.StartsWith("Info group tabs: already grouped", Assert.Single(log));
    }

    [Fact]
    public void Group_Twice_TheSecondRunMovesNothing()
    {
        var pane = new FakePane(Tabs("A1 B1 A2 _1 B2 C1 A3"), selected: "B2");
        var (grouper, _) = Grouper();

        Assert.Equal(TabGroupOutcome.Grouped, grouper.Group([pane]).Outcome);
        var moves = pane.MoveCalls.Count;
        Assert.Equal(TabGroupOutcome.AlreadyGrouped, grouper.Group([pane]).Outcome);
        Assert.Equal(moves, pane.MoveCalls.Count);
    }

    [Fact]
    public void Group_AFailingMove_StopsThere_LeavesAValidOrder_RestoresTheSelection_AndLogsWhy()
    {
        var tabs = Tabs("A1 B1 C1 A2 B2 C2 A3 B3 C3");
        var plan = TabGroupOrder.Plan(tabs.Select(t => t.Key).ToList(), "C");
        Assert.True(plan.Count >= 3);
        var pane = new FakePane(tabs, selected: "C2") { ThrowOnMove = 2 };
        var selected = pane.SelectedItem;
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        // Stopped at the second move: exactly one move applied, nothing tried after it.
        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.Moves);
        Assert.Equal(2, pane.MoveCalls.Count);
        Assert.Equal($"Move 2 of {plan.Count} failed (InvalidOperationException).", result.Reason);
        Assert.Equal($"Error group tabs: stopped after 1 moves. {result.Reason}", Assert.Single(log));
        Assert.Equal(TabGroupNotice.Stopped, result.Notice);

        // A valid order: the same tabs, each once, as after the first planned move.
        var expected = tabs.Cast<object>().ToList();
        var first = expected[plan[0].From];
        expected.RemoveAt(plan[0].From);
        expected.Insert(plan[0].To, first);
        Assert.Equal(expected, pane.Items);
        Assert.Same(selected, pane.SelectedItem);
    }

    [Fact]
    public void Group_AMoveThatLandsElsewhere_Stops()
    {
        var pane = new FakePane(Tabs("A1 B1 C1 A2 B2 C2"), selected: "A1") { Misplace = true };

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Single(pane.MoveCalls);
        Assert.Equal(0, result.Moves);
        Assert.StartsWith("Move 1 of ", result.Reason);
        Assert.EndsWith("did not put the tab where planned.", result.Reason);
        Assert.Equal(6, pane.Items.Distinct().Count());
    }

    [Fact]
    public void Group_AFailureInOnePane_StopsBeforeTheNextPane()
    {
        var first = new FakePane(Tabs("A1 B1 A2 B2 A3"), selected: "B1") { ThrowOnMove = 1 };
        var second = new FakePane(Tabs("B3 A4 B4"), selected: "A4");

        var result = Grouper().Grouper.Group([first, second]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal("Move 1 of 2 in pane 1 of 2 failed (InvalidOperationException).", result.Reason);
        Assert.Empty(second.MoveCalls);
        Assert.Equal("B3 A4 B4", Names(second));
        Assert.Equal("A1 B1 A2 B2 A3", Names(first));
    }

    [Fact]
    public void Group_EachPaneSeparately_NoTabCrossesPanes()
    {
        // Tile Views: the same files in two panes; each is grouped within itself.
        var left = new FakePane(Tabs("A1 B1 A2 C1 B2"), selected: "B2");
        var right = new FakePane(Tabs("C2 A3 C3 B3 A4"), selected: "A4");
        var leftBefore = left.Items.ToHashSet();
        var rightBefore = right.Items.ToHashSet();
        var (grouper, log) = Grouper();

        var result = grouper.Group([left, right]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal(2, result.Panes);
        Assert.Equal("B1 B2 A1 A2 C1", Names(left));
        Assert.Equal("A3 A4 C2 C3 B3", Names(right));
        Assert.True(leftBefore.SetEquals(left.Items));
        Assert.True(rightBefore.SetEquals(right.Items));
        Assert.Equal("B2", left.SelectedItem!.ToString());
        Assert.Equal("A4", right.SelectedItem!.ToString());
        Assert.Equal(result.Moves, left.MoveCalls.Count + right.MoveCalls.Count);
        Assert.Contains("over 10 tabs in 2 panes", Assert.Single(log));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Group_NoPane_IsUnavailable(bool empty)
    {
        var (grouper, log) = Grouper();

        var result = grouper.Group(empty ? Array.Empty<IDocumentPane>() : null);

        Assert.Equal(TabGroupResult.Unavailable(TabGrouper.NoPaneReason), result);
        Assert.Equal(TabGroupNotice.CantGroup, result.Notice);
        Assert.Equal("Warn group tabs: unavailable; nothing moved. " + TabGrouper.NoPaneReason, Assert.Single(log));
    }

    [Fact]
    public void Group_ANullPane_IsUnavailable_WithoutThrowing()
    {
        var result = Grouper().Grouper.Group([null!]);

        Assert.Equal(TabGroupOutcome.Unavailable, result.Outcome);
        Assert.Equal("A document pane was missing.", result.Reason);
    }

    public static TheoryData<string> ReadFailures => ["count", "itemAt", "selected", "key"];

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public void Group_WhenReadingThePaneFails_NothingMoves_ItIsUnavailable_AndItDoesNotThrow(string failure)
    {
        var tabs = Tabs("A1 B1 A2");
        var pane = failure switch
        {
            "count" => new FakePane(tabs, "B1") { ThrowOnCount = new InvalidCastException() },
            "itemAt" => new FakePane(tabs, "B1") { ThrowOnItemAt = 2 },
            "selected" => new FakePane(tabs, "B1") { ThrowOnGetSelected = true },
            _ => new FakePane(tabs, "B1"),
        };
        var log = new ListLog();
        var grouper = new TabGrouper(item => failure == "key" ? throw new FormatException() : KeyOf(item), log);

        var result = grouper.Group([pane]);

        // Nothing was tried: "can't group" and a warning, not "stopped partway" and an error (review m1).
        Assert.Equal(TabGroupOutcome.Unavailable, result.Outcome);
        Assert.Equal(TabGroupNotice.CantGroup, result.Notice);
        Assert.Equal(0, result.Moves);
        Assert.StartsWith("Reading the tabs failed (", result.Reason);
        Assert.Empty(pane.MoveCalls);
        Assert.Equal("A1 B1 A2", Names(pane));
        Assert.StartsWith("Warn group tabs: unavailable; nothing moved. Reading the tabs failed (", Assert.Single(log.Lines));
    }

    [Fact]
    public void Group_WhenSelectingAgainThrows_ReportsIt_AndKeepsTheGroupedOrder()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"), selected: "B1") { ThrowOnSetSelected = true };
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.False(result.SelectionKept);
        Assert.Equal(TabGroupNotice.StoppedSelectionMayHaveChanged, result.Notice);
        Assert.Equal(1, result.Moves);
        Assert.Equal("Selecting the selected tab again failed (ArgumentException).", result.Reason);
        Assert.Equal("B1 A1 A2", Names(pane));
        Assert.Equal("Error group tabs: stopped after 1 moves; the selected view may have changed. " + result.Reason, Assert.Single(log));
    }

    [Fact]
    public void Group_WhenTheSelectionDoesNotTake_ReportsIt()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"), selected: "B1") { IgnoreSetSelected = true };

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal(TabGroupNotice.StoppedSelectionMayHaveChanged, result.Notice);
        Assert.Equal("The selected tab could not be selected again.", result.Reason);
    }

    [Fact]
    public void Group_WithNoSelectedTab_SelectsNothing()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"));

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal(0, pane.SelectSets);
        Assert.Null(pane.SelectedItem);
    }

    [Fact]
    public void Group_UnmatchedTabs_GoLast_InTheirOrder()
    {
        var pane = new FakePane(Tabs("_2 A1 _1 B1 A2"), selected: "_1");

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal("A1 A2 B1 _2 _1", Names(pane));
        Assert.Equal("_1", pane.SelectedItem!.ToString());
    }

    [Fact]
    public void Group_ALogThatThrows_DoesNotThrow()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"), selected: "A1");
        var grouper = new TabGrouper(KeyOf, new ThrowingLog());

        var result = grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal("A1 A2 B1", Names(pane));
    }

    [Fact]
    public void Notice_IsNone_WhenItWorked_OrThereWasNothingToDo()
    {
        Assert.Equal(TabGroupNotice.None, Grouper().Grouper.Group([new FakePane(Tabs("A1 B1 A2"), "A1")]).Notice);
        Assert.Equal(TabGroupNotice.None, Grouper().Grouper.Group([new FakePane(Tabs("A1 A2 B1"), "A1")]).Notice);
    }

    [Fact]
    public void Group_AReadFailureAfterAnotherPaneMoved_IsStoppedPartway_NotUnavailable()
    {
        var first = new FakePane(Tabs("A1 B1 A2"), selected: "A1");
        var second = new FakePane(Tabs("B2 A3"), selected: "B2") { ThrowOnItemAt = 1 };

        var result = Grouper().Grouper.Group([first, second]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal(TabGroupNotice.Stopped, result.Notice);
        Assert.Equal(1, result.Moves);
        Assert.Equal("Reading the tabs in pane 2 of 2 failed (IndexOutOfRangeException).", result.Reason);
        Assert.Equal("A1 A2 B1", Names(first));
    }

    /// <summary>
    /// Decision #28 (review M1): on an overflowing strip AvalonDock moves a selected tab that is
    /// hidden to the front. With the selected tab's file first, the strip stays grouped after
    /// that, and grouping again makes no moves. Visible: how many tabs fit.
    /// </summary>
    [Theory]
    // The review's strip: C1 selected. With files by leftmost tab only, C1 landed at index 7.
    [InlineData("A1 B1 C1 B2 C2 B3 C3 A2 B4 A3", "C1", 7)]
    // A long active file: its selected tab A9 lands hidden at index 8 and is pulled to the front.
    [InlineData("A1 B1 A2 B2 A3 B3 A4 B4 A5 B5 A6 A7 A8 A9", "A9", 4)]
    [InlineData("C1 B1 A1 B2 A2 B3 A3 _1 A4", "A4", 3)]
    public void Group_ThenAvalonDockPullsTheHiddenSelectedTabToTheFront_StillGrouped_AndTheNextClickMakesNoMoves(
        string tabs, string selectedTab, int visible)
    {
        var pane = new FakePane(Tabs(tabs), selected: selectedTab);
        var selected = pane.SelectedItem!;
        var (grouper, _) = Grouper();

        Assert.Equal(TabGroupOutcome.Grouped, grouper.Group([pane]).Outcome);
        Assert.Same(selected, pane.SelectedItem);
        pane.PullSelectedToFrontIfHidden(visible);

        AssertGrouped(pane);
        Assert.Same(selected, pane.SelectedItem);
        var moves = pane.MoveCalls.Count;
        var again = grouper.Group([pane]);
        Assert.Equal(TabGroupOutcome.AlreadyGrouped, again.Outcome);
        Assert.Equal(moves, pane.MoveCalls.Count);
    }

    /// <summary>Each file's tabs are next to each other, and unmatched tabs come last.</summary>
    private static void AssertGrouped(FakePane pane)
    {
        var keys = pane.Items.Select(KeyOf).ToList();
        var seen = new List<object>();
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] is not { } key)
            {
                Assert.All(keys.Skip(i), k => Assert.Null(k));
                return;
            }
            if (i > 0 && Equals(keys[i - 1], key)) continue;
            Assert.DoesNotContain(key, seen);
            seen.Add(key);
        }
    }

    private sealed class ThrowingLog : SplashIt.Core.Logging.ILog
    {
        public void Write(SplashIt.Core.Logging.LogLevel level, string message) => throw new System.IO.IOException();
    }
}
