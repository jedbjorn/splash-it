using SplashIt.Core.Tabs;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// Spec #2 AC2: the grouper restores the selection, stops on the first failure without
/// throwing (leaving a valid order and a logged reason), reports Unavailable when the pane is
/// missing, and never moves a tab across panes. Driven against <see cref="FakePane"/>, whose
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
        var log = new List<string>();
        return (new TabGrouper(KeyOf, log.Add), log);
    }

    [Fact]
    public void Group_GroupsByFile_AndRestoresTheSelectedTab()
    {
        var pane = new FakePane(Tabs("B1 A1 C1 A2 B2 C2 A3"), selected: "C1"); // index 2 now, 5 after: an index-based selection would lose it
        var selected = pane.SelectedItem;
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        Assert.Equal("B1 B2 A1 A2 A3 C1 C2", Names(pane));
        Assert.Equal(new TabGroupResult(TabGroupOutcome.Grouped, 1, 3, null), result);
        Assert.Equal(3, pane.MoveCalls.Count); // the planner's minimum, applied as planned
        Assert.Same(selected, pane.SelectedItem);
        Assert.Equal(1, pane.SelectSets);
        Assert.Equal("group tabs: 3 moves over 7 tabs in 1 panes.", Assert.Single(log));
    }

    [Fact]
    public void Group_WhenThePaneKeepsTheSelectionItself_DoesNotSelectAgain()
    {
        var pane = new FakePane(Tabs("B1 A1 C1 A2 B2"), selected: "C1", selectionFollowsItem: true);
        var selected = pane.SelectedItem;

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal("B1 B2 A1 A2 C1", Names(pane));
        Assert.Same(selected, pane.SelectedItem);
        Assert.Equal(0, pane.SelectSets); // no second activation of the same view
    }

    [Fact]
    public void Group_AlreadyGrouped_MovesNothing_AndTouchesNothing()
    {
        var pane = new FakePane(Tabs("C1 C2 A1 B1 B2 _1"), selected: "B1");
        var (grouper, log) = Grouper();

        var result = grouper.Group([pane]);

        Assert.Equal(new TabGroupResult(TabGroupOutcome.AlreadyGrouped, 1, 0, null), result);
        Assert.Empty(pane.MoveCalls);
        Assert.Equal(0, pane.SelectSets);
        Assert.Equal("C1 C2 A1 B1 B2 _1", Names(pane));
        Assert.Contains("already grouped", Assert.Single(log));
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
        var plan = TabGroupOrder.Plan(tabs.Select(t => t.Key).ToList());
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
        Assert.Equal($"group tabs: stopped after 1 moves. {result.Reason}", Assert.Single(log));

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
        var left = new FakePane(Tabs("A1 B1 A2 C1 B2"), selected: "C1");
        var right = new FakePane(Tabs("C2 A3 C3 B3 A4"), selected: "B3");
        var leftBefore = left.Items.ToHashSet();
        var rightBefore = right.Items.ToHashSet();
        var (grouper, log) = Grouper();

        var result = grouper.Group([left, right]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal(2, result.Panes);
        Assert.Equal("A1 A2 B1 B2 C1", Names(left));
        Assert.Equal("C2 C3 A3 A4 B3", Names(right));
        Assert.True(leftBefore.SetEquals(left.Items));
        Assert.True(rightBefore.SetEquals(right.Items));
        Assert.Equal("C1", left.SelectedItem!.ToString());
        Assert.Equal("B3", right.SelectedItem!.ToString());
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
        Assert.Equal("group tabs: unavailable. " + TabGrouper.NoPaneReason, Assert.Single(log));
    }

    [Fact]
    public void Group_ANullPane_FailsWithoutThrowing()
    {
        var result = Grouper().Grouper.Group([null!]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal("A document pane was missing.", result.Reason);
    }

    public static TheoryData<string> ReadFailures => ["count", "itemAt", "selected", "key"];

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public void Group_WhenReadingThePaneFails_NothingMoves_AndItDoesNotThrow(string failure)
    {
        var tabs = Tabs("A1 B1 A2");
        var pane = failure switch
        {
            "count" => new FakePane(tabs, "B1") { ThrowOnCount = new InvalidCastException() },
            "itemAt" => new FakePane(tabs, "B1") { ThrowOnItemAt = 2 },
            "selected" => new FakePane(tabs, "B1") { ThrowOnGetSelected = true },
            _ => new FakePane(tabs, "B1"),
        };
        var log = new List<string>();
        var grouper = new TabGrouper(item => failure == "key" ? throw new FormatException() : KeyOf(item), log.Add);

        var result = grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.Moves);
        Assert.StartsWith("Reading the tabs failed (", result.Reason);
        Assert.Empty(pane.MoveCalls);
        Assert.Equal("A1 B1 A2", Names(pane));
        Assert.Single(log);
    }

    [Fact]
    public void Group_WhenSelectingAgainThrows_ReportsIt_AndKeepsTheGroupedOrder()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"), selected: "B1") { ThrowOnSetSelected = true };

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.Moves);
        Assert.Equal("Selecting the selected tab again failed (ArgumentException).", result.Reason);
        Assert.Equal("A1 A2 B1", Names(pane));
    }

    [Fact]
    public void Group_WhenTheSelectionDoesNotTake_ReportsIt()
    {
        var pane = new FakePane(Tabs("A1 B1 A2"), selected: "B1") { IgnoreSetSelected = true };

        var result = Grouper().Grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Failed, result.Outcome);
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
        var grouper = new TabGrouper(KeyOf, _ => throw new System.IO.IOException());

        var result = grouper.Group([pane]);

        Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
        Assert.Equal("A1 A2 B1", Names(pane));
    }
}
