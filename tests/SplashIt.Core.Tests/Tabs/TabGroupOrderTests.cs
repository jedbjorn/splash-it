using System.Collections.ObjectModel;
using SplashIt.Core.Tabs;

namespace SplashIt.Core.Tests.Tabs;

/// <summary>
/// Spec #2 AC1: the order rule (files by their leftmost tab, stable within a file, unmatched
/// tabs last), minimal moves, and zero moves when already grouped. Moves are applied with
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
    private static List<string> Apply(IEnumerable<string> tabs, IEnumerable<TabMove> moves)
    {
        var collection = new ObservableCollection<string>(tabs);
        foreach (var move in moves) collection.Move(move.From, move.To);
        return collection.ToList();
    }

    [Theory]
    // Interleaved, 3 files: files by their leftmost tab (B first), stable within each file.
    [InlineData("B1 A1 C1 A2 B2 C2 A3", "B1 B2 A1 A2 A3 C1 C2", 3)]
    [InlineData("A1 B1 C1 A2 B2 C2 A3 B3 C3", "A1 A2 A3 B1 B2 B3 C1 C2 C3", 4)]
    // A file with one tab far left pulls its whole group there.
    [InlineData("C1 A1 A2 B1 B2 C2", "C1 C2 A1 A2 B1 B2", 1)]
    // Already grouped: zero moves.
    [InlineData("A1 A2 B1 C1 C2 C3", "A1 A2 B1 C1 C2 C3", 0)]
    [InlineData("C1 A1 A2 B1", "C1 A1 A2 B1", 0)]
    // A single file: zero moves whatever its order.
    [InlineData("A3 A1 A2", "A3 A1 A2", 0)]
    // Unmatched tabs go last, in their current relative order.
    [InlineData("_1 A1 _2 B1 A2", "A1 A2 B1 _1 _2", 3)]
    [InlineData("A1 B1 A2 B2 _1 _2", "A1 A2 B1 B2 _1 _2", 1)]
    [InlineData("_2 _1 A1", "A1 _2 _1", 1)]
    [InlineData("_1 _2", "_1 _2", 0)]
    // One tab, no tabs.
    [InlineData("A1", "A1", 0)]
    [InlineData("_1", "_1", 0)]
    [InlineData("", "", 0)]
    // The last tab belongs at the front of the strip: one move, not three.
    [InlineData("B1 A1 A2 A3 B2", "B1 B2 A1 A2 A3", 1)]
    [InlineData("A1 B1 B2 B3 A2", "A1 A2 B1 B2 B3", 1)]
    public void Plan_GivesTheTargetOrder_InTheFewestMoves(string current, string expected, int expectedMoves)
    {
        var tabs = Tabs(current);
        var target = TabGroupOrder.Target(Keys(tabs));
        Assert.Equal(Tabs(expected), target.Select(i => tabs[i]));

        var moves = TabGroupOrder.Moves(target);
        Assert.Equal(Tabs(expected), Apply(tabs, moves));
        Assert.Equal(expectedMoves, moves.Count);
        Assert.Equal(MinimumMoves(tabs.Select((_, i) => Array.IndexOf(target, i)).ToArray()), moves.Count);
        Assert.All(moves, m => Assert.NotEqual(m.From, m.To));
    }

    [Fact]
    public void Keys_AreComparedByEquality_NotReference()
    {
        // Two separately built but equal keys are one file.
        object?[] keys = [new string('a', 1), "b", new string('a', 1)];
        Assert.Equal(new[] { 0, 2, 1 }, TabGroupOrder.Target(keys));
    }

    [Fact]
    public void Keys_UseTheGivenComparer()
    {
        object?[] keys = ["a", "B", "A"];
        Assert.Equal(new[] { 0, 1, 2 }, TabGroupOrder.Target(keys));
        Assert.Equal(new[] { 0, 2, 1 }, TabGroupOrder.Target(keys, new CaseInsensitive()));
    }

    [Fact]
    public void Moves_RejectsATargetThatIsNotAPermutation()
    {
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([0, 0]));
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([1, 2]));
        Assert.Throws<ArgumentException>(() => TabGroupOrder.Moves([-1]));
    }

    /// <summary>
    /// Every strip of up to 6 tabs over 3 files plus unmatched tabs (5,461 strips): the moves
    /// reach the rule's order, and their count equals the true minimum found by a breadth-first
    /// search over single-tab moves, which knows nothing of how the planner works.
    /// </summary>
    [Fact]
    public void Exhaustive_UpToSixTabs_MovesAreCorrectAndMinimal()
    {
        object?[] alphabet = ["A", "B", "C", null];
        var checkedStrips = 0;
        for (var n = 0; n <= 6; n++)
        {
            var distance = DistancesToSorted(n);
            var digits = new int[n];
            while (true)
            {
                var keys = digits.Select(d => alphabet[d]).ToList();
                var tabs = Enumerable.Range(0, n).ToList();
                var target = TabGroupOrder.Target(keys);
                AssertFollowsTheRule(keys, target);

                var moves = TabGroupOrder.Moves(target);
                Assert.Equal(target, Apply(tabs.Select(t => t.ToString()), moves).Select(int.Parse));
                var rank = tabs.Select(i => Array.IndexOf(target, i)).ToArray();
                Assert.Equal(distance[string.Join(",", rank)], moves.Count);
                checkedStrips++;

                var k = 0;
                while (k < n && ++digits[k] == alphabet.Length) digits[k++] = 0;
                if (k == n) break;
            }
        }
        Assert.Equal(5461, checkedStrips);
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
            var target = TabGroupOrder.Target(keys);
            AssertFollowsTheRule(keys, target);

            var moves = TabGroupOrder.Moves(target);
            var tabs = Enumerable.Range(0, n).Select(i => i.ToString()).ToList();
            Assert.Equal(target, Apply(tabs, moves).Select(int.Parse));
            var rank = Enumerable.Range(0, n).Select(i => Array.IndexOf(target, i)).ToArray();
            Assert.Equal(MinimumMoves(rank), moves.Count);
        }
    }

    [Fact]
    public void Plan_IsTargetThenMoves()
    {
        var keys = Keys(Tabs("B1 A1 B2 _1 A2"));
        Assert.Equal(TabGroupOrder.Moves(TabGroupOrder.Target(keys)), TabGroupOrder.Plan(keys));
    }

    [Fact]
    public void Plan_OnItsOwnResult_IsZeroMoves()
    {
        // Rapid repeated clicks: the second click finds the strip grouped.
        var tabs = Tabs("C1 A1 _1 B1 A2 C2 _2 B2 A3");
        var grouped = Apply(tabs, TabGroupOrder.Plan(Keys(tabs)));
        Assert.Empty(TabGroupOrder.Plan(Keys(grouped)));
    }

    /// <summary>
    /// Checks the rule independently: matched tabs first, files in order of their leftmost tab,
    /// contiguous, current relative order within each file and among the unmatched.
    /// </summary>
    private static void AssertFollowsTheRule(IReadOnlyList<object?> keys, int[] target)
    {
        Assert.Equal(Enumerable.Range(0, keys.Count), target.OrderBy(i => i));
        var fileOrder = keys.Where(k => k is not null).Distinct().ToList();
        var expected = fileOrder
            .SelectMany(file => Enumerable.Range(0, keys.Count).Where(i => Equals(keys[i], file)))
            .Concat(Enumerable.Range(0, keys.Count).Where(i => keys[i] is null));
        Assert.Equal(expected, target);
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
