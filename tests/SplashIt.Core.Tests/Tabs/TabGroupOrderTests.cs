using System.Collections.ObjectModel;
using SplashIt.Core.Tabs;

namespace SplashIt.Core.Tests.Tabs;

/// <summary>
/// Spec #2 AC1 (decision #28): the order rule (the active file first, then files by their
/// leftmost tab, stable within a file, unmatched tabs last), minimal moves, and zero moves when
/// already grouped. Also AvalonDock's overflow behaviour: a hidden selected tab pulled to the
/// front must leave the strip grouped, with zero moves on the next run. Moves are applied with
/// <see cref="ObservableCollection{T}.Move"/>, the collection AvalonDock's <c>MoveChild</c>
/// moves, so the tests also pin the index semantics.
/// </summary>
public class TabGroupOrderTests
{
    /// <summary>
    /// Tabs written as <c>A1 B1 A2 _1</c>: the letter is the file (document key), <c>_</c> is a
    /// tab that matches no document, and the digit tells a file's tabs apart.
    /// </summary>
    private static string[] Tabs(string tabs) => tabs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static object? KeyOf(string tab) => tab[0] == '_' ? null : tab[..1];

    private static List<object?> Keys(IEnumerable<string> tabs) => tabs.Select(KeyOf).ToList();

    /// <summary>Applies the moves to the tabs the way MoveChild does.</summary>
    private static List<T> Apply<T>(IEnumerable<T> tabs, IEnumerable<TabMove> moves)
    {
        var collection = new ObservableCollection<T>(tabs);
        foreach (var move in moves) collection.Move(move.From, move.To);
        return collection.ToList();
    }

    [Theory]
    // Interleaved, 3 files: with no active file, files by their leftmost tab (B first).
    [InlineData("B1 A1 C1 A2 B2 C2 A3", "", "B1 B2 A1 A2 A3 C1 C2", 3)]
    // The active file first, then the others by their leftmost tab (#28).
    [InlineData("B1 A1 C1 A2 B2 C2 A3", "C", "C1 C2 B1 B2 A1 A2 A3", 3)]
    [InlineData("B1 A1 C1 A2 B2 C2 A3", "A", "A1 A2 A3 B1 B2 C1 C2", 3)]
    // The active file is already the leftmost: the same as no active file.
    [InlineData("B1 A1 C1 A2 B2 C2 A3", "B", "B1 B2 A1 A2 A3 C1 C2", 3)]
    [InlineData("A1 B1 C1 A2 B2 C2 A3 B3 C3", "", "A1 A2 A3 B1 B2 B3 C1 C2 C3", 4)]
    [InlineData("A1 B1 C1 A2 B2 C2 A3 B3 C3", "B", "B1 B2 B3 A1 A2 A3 C1 C2 C3", 5)]
    // The review's overflow strip (M1), C1 selected.
    [InlineData("A1 B1 C1 B2 C2 B3 C3 A2 B4 A3", "C", "C1 C2 C3 A1 A2 A3 B1 B2 B3 B4", 5)]
    // A file with one tab far left pulls its whole group there.
    [InlineData("C1 A1 A2 B1 B2 C2", "", "C1 C2 A1 A2 B1 B2", 1)]
    [InlineData("C1 A1 A2 B1 B2 C2", "B", "B1 B2 C1 C2 A1 A2", 3)]
    // Already grouped, with the active file first (or none): zero moves.
    [InlineData("A1 A2 B1 C1 C2 C3", "", "A1 A2 B1 C1 C2 C3", 0)]
    [InlineData("A1 A2 B1 C1 C2 C3", "A", "A1 A2 B1 C1 C2 C3", 0)]
    [InlineData("C1 A1 A2 B1", "C", "C1 A1 A2 B1", 0)]
    // Grouped, but another file is active: that file's group moves to the front.
    [InlineData("A1 A2 B1 C1 C2 C3", "C", "C1 C2 C3 A1 A2 B1", 3)]
    // A single file: zero moves whatever its order.
    [InlineData("A3 A1 A2", "", "A3 A1 A2", 0)]
    [InlineData("A3 A1 A2", "A", "A3 A1 A2", 0)]
    // Unmatched tabs go last, in their current relative order.
    [InlineData("_1 A1 _2 B1 A2", "", "A1 A2 B1 _1 _2", 3)]
    [InlineData("_1 A1 _2 B1 A2", "B", "B1 A1 A2 _1 _2", 3)]
    [InlineData("A1 B1 A2 B2 _1 _2", "", "A1 A2 B1 B2 _1 _2", 1)]
    [InlineData("_2 _1 A1", "", "A1 _2 _1", 1)]
    // An unmatched tab first: one move to the end, not one move per file tab.
    [InlineData("_1 A1 B1 C1 C2", "", "A1 B1 C1 C2 _1", 1)]
    [InlineData("_1 A1 B1 C1 C2", "C", "C1 C2 A1 B1 _1", 3)]
    [InlineData("_1 _2", "", "_1 _2", 0)]
    // One tab, no tabs.
    [InlineData("A1", "", "A1", 0)]
    [InlineData("A1", "A", "A1", 0)]
    [InlineData("_1", "", "_1", 0)]
    [InlineData("", "", "", 0)]
    // The last tab belongs at the front of the strip: one move, not three.
    [InlineData("B1 A1 A2 A3 B2", "", "B1 B2 A1 A2 A3", 1)]
    [InlineData("A1 B1 B2 B3 A2", "", "A1 A2 B1 B2 B3", 1)]
    // An active file with no tab in this pane changes nothing.
    [InlineData("A1 B1 A2", "D", "A1 A2 B1", 1)]
    public void Plan_GivesTheTargetOrder_InTheFewestMoves(string current, string active, string expected, int expectedMoves)
    {
        var tabs = Tabs(current);
        var activeKey = active.Length == 0 ? null : active;
        var target = TabGroupOrder.Target(Keys(tabs), activeKey);
        Assert.Equal(Tabs(expected), target.Select(i => tabs[i]));

        var moves = TabGroupOrder.Moves(target);
        Assert.Equal(Tabs(expected), Apply(tabs, moves));
        Assert.Equal(expectedMoves, moves.Count);
        Assert.Equal(MinimumMoves(tabs.Select((_, i) => Array.IndexOf(target, i)).ToArray()), moves.Count);
        Assert.All(moves, m => Assert.NotEqual(m.From, m.To));
        Assert.Equal(moves, TabGroupOrder.Plan(Keys(tabs), activeKey));
    }

    [Fact]
    public void Keys_AreComparedByEquality_NotReference()
    {
        // Two separately built but equal keys are one file, the active key included.
        object?[] keys = [new string('a', 1), "b", new string('a', 1)];
        Assert.Equal(new[] { 0, 2, 1 }, TabGroupOrder.Target(keys));
        Assert.Equal(new[] { 1, 0, 2 }, TabGroupOrder.Target(keys, new string('b', 1)));
    }

    [Fact]
    public void Keys_UseTheGivenComparer()
    {
        object?[] keys = ["a", "B", "A"];
        Assert.Equal(new[] { 0, 1, 2 }, TabGroupOrder.Target(keys));
        Assert.Equal(new[] { 0, 2, 1 }, TabGroupOrder.Target(keys, comparer: new CaseInsensitive()));
        Assert.Equal(new[] { 1, 0, 2 }, TabGroupOrder.Target(keys, "b", new CaseInsensitive()));
    }

    [Fact]
    public void Moves_RejectsATargetThatIsNotAPermutation()
    {
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([0, 0]));
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([1, 2]));
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([-1]));
    }

    /// <summary>
    /// Every strip of up to 6 tabs over 3 files plus unmatched tabs (5,461 strips), each with no
    /// active file and with each of the three files active (21,844 cases): the moves reach the
    /// rule's order, and their count equals the true minimum found by a breadth-first search
    /// over single-tab moves, which knows nothing of how the planner works.
    /// </summary>
    [Fact]
    public void Exhaustive_UpToSixTabs_MovesAreCorrectAndMinimal()
    {
        object?[] actives = [null, "A", "B", "C"];
        var checkedCases = 0;
        foreach (var (keys, distance) in AllStrips())
        {
            foreach (var active in actives)
            {
                var n = keys.Count;
                var target = TabGroupOrder.Target(keys, active);
                AssertFollowsTheRule(keys, active, target);

                var moves = TabGroupOrder.Moves(target);
                Assert.Equal(target, Apply(Enumerable.Range(0, n), moves));
                var rank = Enumerable.Range(0, n).Select(i => Array.IndexOf(target, i)).ToArray();
                Assert.Equal(distance[string.Join(",", rank)], moves.Count);
                checkedCases++;
            }
        }
        Assert.Equal(5461 * 4, checkedCases);
    }

    /// <summary>
    /// Decision #28, every strip of up to 6 tabs and every selected tab that belongs to a file:
    /// group with the selected tab's file active, then do what AvalonDock does to a selected tab
    /// in the hidden part of an overflowing strip, and move it to the front. The strip is still
    /// grouped, and grouping again makes no moves.
    /// </summary>
    [Fact]
    public void Exhaustive_ASelectedTabPulledToTheFront_StaysGrouped_AndTheNextRunMakesNoMoves()
    {
        var checkedCases = 0;
        foreach (var (keys, _) in AllStrips())
        {
            for (var selected = 0; selected < keys.Count; selected++)
            {
                var active = keys[selected];
                if (active is null) continue; // a selected tab that matches no file has no group to lead
                var grouped = Apply(Enumerable.Range(0, keys.Count), TabGroupOrder.Plan(keys, active));

                // AvalonDock: the selected tab is hidden, so it moves to index 0.
                grouped.Remove(selected);
                grouped.Insert(0, selected);

                var after = grouped.Select(i => keys[i]).ToList();
                AssertGrouped(after);
                Assert.Empty(TabGroupOrder.Plan(after, active));
                checkedCases++;
            }
        }
        Assert.True(checkedCases > 10_000, checkedCases.ToString());
    }

    /// <summary>Why #28: with files ordered by their leftmost tab only, the review's strip is not grouped after the pull.</summary>
    [Fact]
    public void WithoutTheActiveFileFirst_APulledTab_WouldBreakTheGroups()
    {
        var tabs = Tabs("A1 B1 C1 B2 C2 B3 C3 A2 B4 A3");
        var grouped = Apply(tabs, TabGroupOrder.Plan(Keys(tabs)));
        Assert.Equal(7, grouped.IndexOf("C1")); // past about 7 visible tabs
        grouped.Remove("C1");
        grouped.Insert(0, "C1");
        Assert.NotEmpty(TabGroupOrder.Plan(Keys(grouped)));

        var withActive = Apply(tabs, TabGroupOrder.Plan(Keys(tabs), "C"));
        Assert.Equal(0, withActive.IndexOf("C1"));
    }

    /// <summary>Larger random strips: the moves reach the target; the count is n minus the longest increasing run.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Random_LargerStrips_MovesAreCorrectAndMinimal(int seed)
    {
        var random = new Random(seed);
        for (var round = 0; round < 200; round++)
        {
            var n = random.Next(0, 60);
            var files = random.Next(1, 8);
            var keys = new List<object?>();
            for (var i = 0; i < n; i++)
            {
                var file = random.Next(files + 1);
                keys.Add(file == files ? null : $"F{file}");
            }
            var active = n == 0 ? null : keys[random.Next(n)];
            var target = TabGroupOrder.Target(keys, active);
            AssertFollowsTheRule(keys, active, target);

            var moves = TabGroupOrder.Moves(target);
            Assert.Equal(target, Apply(Enumerable.Range(0, n), moves));
            var rank = Enumerable.Range(0, n).Select(i => Array.IndexOf(target, i)).ToArray();
            Assert.Equal(MinimumMoves(rank), moves.Count);
        }
    }

    [Fact]
    public void Plan_OnItsOwnResult_IsZeroMoves()
    {
        // Rapid repeated clicks: the second click finds the strip grouped.
        var tabs = Tabs("C1 A1 _1 B1 A2 C2 _2 B2 A3");
        foreach (var active in new object?[] { null, "A", "B", "C" })
        {
            var grouped = Apply(tabs, TabGroupOrder.Plan(Keys(tabs), active));
            Assert.Empty(TabGroupOrder.Plan(Keys(grouped), active));
        }
    }

    /// <summary>Every strip of 0 to 6 tabs over A, B, C and unmatched, with BFS distances for its length.</summary>
    private static IEnumerable<(List<object?> Keys, Dictionary<string, int> Distance)> AllStrips()
    {
        object?[] alphabet = ["A", "B", "C", null];
        for (var n = 0; n <= 6; n++)
        {
            var distance = DistancesToSorted(n);
            var digits = new int[n];
            while (true)
            {
                yield return (digits.Select(d => alphabet[d]).ToList(), distance);
                var k = 0;
                while (k < n && ++digits[k] == alphabet.Length) digits[k++] = 0;
                if (k == n) break;
            }
        }
    }

    /// <summary>
    /// Checks the rule independently: the active file's tabs first (if it has any), then the
    /// other files in order of their leftmost tab, each contiguous and in current order, then the
    /// unmatched tabs in current order.
    /// </summary>
    private static void AssertFollowsTheRule(IReadOnlyList<object?> keys, object? active, int[] target)
    {
        Assert.Equal(Enumerable.Range(0, keys.Count), target.OrderBy(i => i));
        var files = keys.Where(k => k is not null).Distinct().ToList();
        if (active is not null && files.Remove(active)) files.Insert(0, active);
        var expected = files
            .SelectMany(file => Enumerable.Range(0, keys.Count).Where(i => Equals(keys[i], file)))
            .Concat(Enumerable.Range(0, keys.Count).Where(i => keys[i] is null));
        Assert.Equal(expected, target);
    }

    /// <summary>Each file's tabs are contiguous, and unmatched tabs come after every file's.</summary>
    private static void AssertGrouped(IReadOnlyList<object?> keys)
    {
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

    /// <summary>n minus the longest strictly increasing subsequence (O(n²), independent of the planner's).</summary>
    private static int MinimumMoves(int[] rank)
    {
        var best = 0;
        var run = new int[rank.Length];
        for (var i = 0; i < rank.Length; i++)
        {
            run[i] = 1;
            for (var j = 0; j < i; j++)
                if (rank[j] < rank[i]) run[i] = Math.Max(run[i], run[j] + 1);
            best = Math.Max(best, run[i]);
        }
        return rank.Length - best;
    }

    /// <summary>Breadth-first search from the sorted order over single-tab moves (a move's inverse is a move).</summary>
    private static Dictionary<string, int> DistancesToSorted(int n)
    {
        var start = Enumerable.Range(0, n).ToList();
        var distance = new Dictionary<string, int> { [string.Join(",", start)] = 0 };
        var queue = new Queue<List<int>>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var d = distance[string.Join(",", current)];
            for (var from = 0; from < n; from++)
                for (var to = 0; to < n; to++)
                {
                    if (from == to) continue;
                    var next = new List<int>(current);
                    var item = next[from];
                    next.RemoveAt(from);
                    next.Insert(to, item);
                    var key = string.Join(",", next);
                    if (distance.ContainsKey(key)) continue;
                    distance[key] = d + 1;
                    queue.Enqueue(next);
                }
        }
        return distance;
    }

    private sealed class CaseInsensitive : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => string.Equals(x as string, y as string, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(object obj) => StringComparer.OrdinalIgnoreCase.GetHashCode((string)obj);
    }
}
