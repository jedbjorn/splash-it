namespace SplashIt.Core.Colors;

/// <summary>
/// Hands out indexes into one color list (Project colors or Family colors; one
/// allocator per list) to open documents, identified by an opaque key.
/// <list type="bullet">
/// <item>An opening document takes the first free index from the start of the list.</item>
/// <item>It keeps that index until it is released (the document closes); release frees it.</item>
/// <item>When every index is held, indexes repeat: the one with the fewest current
/// holders, lowest index first. With 12 colors the 13th document gets index 0 and the 14th index 1.</item>
/// <item><see cref="Reset"/> reassigns from the start of the list in the order given (tab order).</item>
/// <item><see cref="SetColorCount"/> survives list edits: a held index beyond a shortened
/// list is reassigned by the same rule, in the order the documents first took a color.</item>
/// </list>
/// Not thread-safe; the add-in uses it on Revit's UI thread only.
/// </summary>
public sealed class ColorAllocator<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, int> _indexOf;
    private readonly List<TKey> _order = [];
    private int[] _holders;

    /// <param name="colorCount">The list length, at least 1.</param>
    /// <param name="comparer">Key equality; the default comparer when null.</param>
    public ColorAllocator(int colorCount, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(colorCount, 1);
        _holders = new int[colorCount];
        _indexOf = new Dictionary<TKey, int>(comparer);
    }

    /// <summary>The current list length.</summary>
    public int ColorCount => _holders.Length;

    /// <summary>The number of keys holding an index.</summary>
    public int Count => _order.Count;

    /// <summary>
    /// A snapshot of the keys holding an index, in the order they first took one.
    /// Safe to enumerate while releasing keys; later changes do not show in it.
    /// Each read allocates a new copy, so keep it off hot paths (such as a
    /// <c>LayoutUpdated</c> handler); use <see cref="Count"/> and <see cref="TryGetIndex"/> there.
    /// </summary>
    public IReadOnlyList<TKey> Keys => Array.AsReadOnly(_order.ToArray());

    /// <summary>
    /// The index held by <paramref name="key"/>, assigning one first if it has none.
    /// Calling it again for a key that holds an index returns the same index.
    /// </summary>
    public int Assign(TKey key)
    {
        if (_indexOf.TryGetValue(key, out var held)) return held;
        var index = NextIndex();
        _indexOf.Add(key, index);
        _holders[index]++;
        _order.Add(key);
        return index;
    }

    /// <summary>Frees the index held by <paramref name="key"/>. False when it held none.</summary>
    public bool Release(TKey key)
    {
        if (!_indexOf.Remove(key, out var index)) return false;
        _holders[index]--;
        // Match with the dictionary's comparer, not TKey's own Equals: the key passed
        // in may differ from the stored one (e.g. "DOC" vs "Doc" under OrdinalIgnoreCase).
        var comparer = _indexOf.Comparer;
        _order.RemoveAt(_order.FindIndex(k => comparer.Equals(k, key)));
        return true;
    }

    /// <summary>The index held by <paramref name="key"/>, if any. Never assigns.</summary>
    public bool TryGetIndex(TKey key, out int index) => _indexOf.TryGetValue(key, out index);

    /// <summary>How many keys currently hold <paramref name="index"/>.</summary>
    public int HolderCount(int index) => _holders[index];

    /// <summary>Releases every key.</summary>
    public void Clear()
    {
        _indexOf.Clear();
        _order.Clear();
        Array.Clear(_holders);
    }

    /// <summary>
    /// Releases every key, then assigns <paramref name="keysInOrder"/> from the start of
    /// the list in that order (duplicates ignored). Keys not listed end up holding nothing.
    /// </summary>
    public void Reset(IEnumerable<TKey> keysInOrder)
    {
        ArgumentNullException.ThrowIfNull(keysInOrder);
        var keys = keysInOrder.ToList(); // materialize first: the sequence may be lazy over this allocator
        Clear();
        foreach (var key in keys) Assign(key);
    }

    /// <summary>
    /// Changes the list length (at least 1). Keys whose index still exists keep it;
    /// keys holding an index beyond a shortened list are reassigned by the usual rule,
    /// in the order they first took a color.
    /// </summary>
    public void SetColorCount(int colorCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(colorCount, 1);
        if (colorCount == _holders.Length) return;

        var holders = new int[colorCount];
        var displaced = new List<TKey>();
        foreach (var key in _order)
        {
            var index = _indexOf[key];
            if (index < colorCount) holders[index]++;
            else displaced.Add(key);
        }
        _holders = holders;

        foreach (var key in displaced)
        {
            var index = NextIndex();
            _indexOf[key] = index;
            _holders[index]++;
        }
    }

    /// <summary>The first index with the fewest holders: a free one if any, lowest first.</summary>
    private int NextIndex()
    {
        var best = 0;
        for (var i = 1; i < _holders.Length; i++)
            if (_holders[i] < _holders[best]) best = i;
        return best;
    }
}
