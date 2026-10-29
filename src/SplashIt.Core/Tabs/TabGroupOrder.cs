namespace SplashIt.Core.Tabs;

/// <summary>
/// One move of a tab within its pane, with the semantics of AvalonDock's
/// <c>MoveChild(oldIndex, newIndex)</c> and <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/>:
/// the tab at <paramref name="From"/> is removed, then inserted so that it ends up at
/// <paramref name="To"/>.
/// </summary>
public readonly record struct TabMove(int From, int To);

/// <summary>
/// The "Group tabs by File" order rule (spec #2, Behavior 1–3, 6). Pure.
/// <list type="number">
/// <item><b>Files keep their place</b>: files are ordered by their leftmost tab, so each file's
/// group starts where that file's leftmost tab is now (relative to the other files).</item>
/// <item><b>Stable within a file</b>: a file's tabs keep their current relative order.</item>
/// <item><b>Unmatched tabs last</b>: tabs with no document key keep their current relative
/// order after every grouped tab.</item>
/// </list>
/// <see cref="Moves"/> turns a target order into the fewest single-tab moves: every tab outside
/// one longest run of tabs that are already in target order moves exactly once, and the tabs in
/// that run never move. An order that is already grouped gives zero moves.
/// </summary>
public static class TabGroupOrder
{
    /// <summary>
    /// The target order: <c>result[j]</c> is the current index of the tab that belongs at
    /// position <c>j</c>. <paramref name="keys"/>[i] is the document key of the tab now at
    /// index i, or null when the tab matches no document. Keys are compared with
    /// <paramref name="comparer"/> (default equality when null).
    /// </summary>
    public static int[] Target(IReadOnlyList<object?> keys, IEqualityComparer<object>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var groupOf = new Dictionary<object, int>(comparer ?? EqualityComparer<object>.Default);
        var groups = new List<List<int>>();
        var unmatched = new List<int>();
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key is null)
            {
                unmatched.Add(i);
                continue;
            }
            if (!groupOf.TryGetValue(key, out var group))
            {
                group = groups.Count;
                groupOf[key] = group;
                groups.Add(new List<int>());
            }
            groups[group].Add(i);
        }

        var target = new int[keys.Count];
        var j = 0;
        foreach (var group in groups)
            foreach (var index in group)
                target[j++] = index;
        foreach (var index in unmatched)
            target[j++] = index;
        return target;
    }

    /// <summary>
    /// The fewest moves that turn the current order into <paramref name="target"/> (as returned
    /// by <see cref="Target"/>: a permutation of <c>0..n-1</c>). Applied in sequence with
    /// <see cref="TabMove"/> semantics, they leave the tab that was at <c>target[j]</c> at
    /// position <c>j</c>. The count is <c>n</c> minus the longest increasing run of target
    /// positions in current order, which is the minimum for single-tab moves.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is not a permutation.</exception>
    public static IReadOnlyList<TabMove> Moves(IReadOnlyList<int> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var n = target.Count;

        // rank[i]: where the tab now at index i belongs.
        var rank = new int[n];
        var seen = new bool[n];
        for (var j = 0; j < n; j++)
        {
            var i = target[j];
            if (i < 0 || i >= n || seen[i])
                throw new ArgumentException("The target order is not a permutation of the current tabs.", nameof(target));
            seen[i] = true;
            rank[i] = j;
        }

        // The tabs that stay: one longest increasing subsequence of rank, in current order.
        var settled = new bool[n];
        foreach (var i in LongestIncreasingRun(rank))
            settled[rank[i]] = true;

        // Each other tab moves once, in target order, to just after the last settled tab that
        // belongs before it. Settled tabs are always in increasing order, so when every tab is
        // settled the order is the target.
        var current = new List<int>(rank);
        var moves = new List<TabMove>();
        for (var r = 0; r < n; r++)
        {
            if (settled[r]) continue;
            var from = current.IndexOf(r);
            current.RemoveAt(from);
            var to = 0;
            for (var k = 0; k < current.Count; k++)
                if (settled[current[k]] && current[k] < r)
                    to = k + 1;
            current.Insert(to, r);
            settled[r] = true;
            if (from != to) moves.Add(new TabMove(from, to));
        }
        return moves;
    }

    /// <summary><see cref="Target"/> then <see cref="Moves"/>.</summary>
    public static IReadOnlyList<TabMove> Plan(IReadOnlyList<object?> keys, IEqualityComparer<object>? comparer = null) =>
        Moves(Target(keys, comparer));

    /// <summary>The indices of one longest strictly increasing subsequence of <paramref name="values"/> (patience sorting, O(n log n)).</summary>
    private static List<int> LongestIncreasingRun(int[] values)
    {
        var n = values.Length;
        var tails = new List<int>(); // tails[l]: index of the smallest tail of a run of length l + 1
        var previous = new int[n];
        for (var i = 0; i < n; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (values[tails[mid]] < values[i]) lo = mid + 1;
                else hi = mid;
            }
            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i);
            else tails[lo] = i;
        }

        var run = new List<int>(tails.Count);
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
            run.Add(i);
        run.Reverse();
        return run;
    }
}
