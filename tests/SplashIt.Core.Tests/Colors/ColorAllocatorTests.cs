using SplashIt.Core.Colors;

namespace SplashIt.Core.Tests.Colors;

/// <summary>
/// AC2: allocation per list: first free from the start, stable while open, freed on
/// close, repeat with fewest holders, reset, survives a shortened list.
/// </summary>
public class ColorAllocatorTests
{
    /// <summary>
    /// Runs a script of steps against one allocator and returns each open's index.
    /// <c>+A</c> opens A, <c>-A</c> closes A, <c>reset:C,A</c> resets in that order,
    /// <c>count:3</c> changes the list length. The result lists every held index
    /// afterwards as <c>A0 B1</c>, in the order the keys first took a color.
    /// </summary>
    private static (string Opens, string Held) Run(int colors, string script)
    {
        var allocator = new ColorAllocator<string>(colors);
        var opens = new List<string>();
        foreach (var step in script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step.StartsWith('+')) opens.Add($"{step[1..]}{allocator.Assign(step[1..])}");
            else if (step.StartsWith('-')) Assert.True(allocator.Release(step[1..]), $"{step}: not open");
            else if (step.StartsWith("reset:")) allocator.Reset(step["reset:".Length..].Split(','));
            else if (step.StartsWith("count:")) allocator.SetColorCount(int.Parse(step["count:".Length..]));
            else throw new ArgumentException($"bad step {step}");
        }
        var held = allocator.Keys.Select(k => allocator.TryGetIndex(k, out var i) ? $"{k}{i}" : "?");
        return (string.Join(" ", opens), string.Join(" ", held));
    }

    [Theory]
    // First free from the start of the list.
    [InlineData(12, "+A +B +C", "A0 B1 C2", "A0 B1 C2")]
    // Closing frees the color; the next open takes the first free one again.
    [InlineData(12, "+A +B +C -B +D", "A0 B1 C2 D1", "A0 C2 D1")]
    [InlineData(12, "+A +B +C -A -B +D +E +F", "A0 B1 C2 D0 E1 F3", "C2 D0 E1 F3")]
    // Close the first file and open a new one: it takes color 1 again (AC11 step 4).
    [InlineData(12, "+P1 +P2 -P1 +P3", "P10 P21 P30", "P21 P30")]
    // Stable while open: assigning an open key again returns its index, others don't move it.
    [InlineData(12, "+A +B +A -B +C +A", "A0 B1 A0 C1 A0", "A0 C1")]
    // Every color taken: repeat with the fewest holders, lowest index first.
    [InlineData(3, "+A +B +C +D +E +F +G", "A0 B1 C2 D0 E1 F2 G0", "A0 B1 C2 D0 E1 F2 G0")]
    // After closes, the fewest-holders rule picks the emptied index first.
    [InlineData(3, "+A +B +C +D +E -B +F +G", "A0 B1 C2 D0 E1 F1 G2", "A0 C2 D0 E1 F1 G2")]
    // A single color is shared by everyone.
    [InlineData(1, "+A +B +C", "A0 B0 C0", "A0 B0 C0")]
    public void ScriptedSequences_GiveTheExpectedIndexes(int colors, string script, string opens, string held)
    {
        var result = Run(colors, script);
        Assert.Equal(opens, result.Opens);
        Assert.Equal(held, result.Held);
    }

    [Fact]
    public void FourteenFiles_OnTwelveColors_RepeatFromTheStart()
    {
        var allocator = new ColorAllocator<string>(12);
        var indexes = Enumerable.Range(1, 14).Select(i => allocator.Assign($"P{i}")).ToArray();

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0, 1], indexes);
        Assert.Equal(2, allocator.HolderCount(0));
        Assert.Equal(2, allocator.HolderCount(1));
        Assert.All(Enumerable.Range(2, 10), i => Assert.Equal(1, allocator.HolderCount(i)));
    }

    [Fact]
    public void FourteenFiles_OnTwelveColors_ThenCloses_ReuseTheEmptiestColor()
    {
        var allocator = new ColorAllocator<string>(12);
        for (var i = 1; i <= 14; i++) allocator.Assign($"P{i}");

        // P5 (index 4) closes: index 4 is now free, so the 15th file takes it.
        Assert.True(allocator.Release("P5"));
        Assert.Equal(4, allocator.Assign("P15"));

        // P1 (index 0, shared with P13) closes: indexes 0, 2..11 have one holder,
        // index 1 has two, so the 16th file takes the lowest of the fewest: 0.
        Assert.True(allocator.Release("P1"));
        Assert.Equal(0, allocator.Assign("P16"));
        // Now 0 and 1 have two holders each; 2 is the lowest with one.
        Assert.Equal(2, allocator.Assign("P17"));
    }

    [Fact]
    public void Release_UnknownKey_ReturnsFalse_AndChangesNothing()
    {
        var allocator = new ColorAllocator<string>(4);
        allocator.Assign("A");
        Assert.False(allocator.Release("Z"));
        Assert.True(allocator.Release("A"));
        Assert.False(allocator.Release("A"));
        Assert.Equal(0, allocator.Count);
        Assert.Equal(0, allocator.HolderCount(0));
    }

    [Fact]
    public void TryGetIndex_NeverAssigns()
    {
        var allocator = new ColorAllocator<string>(4);
        Assert.False(allocator.TryGetIndex("A", out _));
        Assert.Equal(0, allocator.Count);
    }

    [Theory]
    // Reset reassigns from the start of the list, in the order given (tab order).
    [InlineData(12, "+A +B +C -A +D reset:C,D,B", "C0 D1 B2")]
    // Keys not listed are dropped; duplicates are ignored.
    [InlineData(12, "+A +B +C reset:B,B,A", "B0 A1")]
    // Reset with more files than colors repeats from the start.
    [InlineData(2, "+A +B +C reset:C,B,A", "C0 B1 A0")]
    public void Reset_ReassignsFromTheStartInTheGivenOrder(int colors, string script, string held) =>
        Assert.Equal(held, Run(colors, script).Held);

    [Fact]
    public void Reset_WithItsOwnKeys_Works()
    {
        var allocator = new ColorAllocator<string>(3);
        foreach (var k in new[] { "A", "B", "C", "D" }) allocator.Assign(k);
        allocator.Release("B");
        allocator.Reset(allocator.Keys);
        Assert.Equal(["A", "C", "D"], allocator.Keys);
        Assert.True(allocator.TryGetIndex("D", out var d));
        Assert.Equal(2, d);
    }

    [Theory]
    // A shortened list reassigns only the keys beyond it, by the usual rule.
    [InlineData(5, "+A +B +C +D +E count:3", "A0 B1 C2 D0 E1")]
    // Displaced keys go to the first free index when there is one.
    [InlineData(5, "+A +B +C +D +E -B count:3", "A0 C2 D1 E0")]
    // Shortened to one color: everyone shares it.
    [InlineData(4, "+A +B +C +D count:1", "A0 B0 C0 D0")]
    // A longer list keeps every assignment; new files take the new free colors.
    [InlineData(2, "+A +B +C count:4 +D", "A0 B1 C0 D2")]
    // Unchanged length is a no-op.
    [InlineData(3, "+A +B count:3", "A0 B1")]
    public void SetColorCount_SurvivesListEdits(int colors, string script, string held) =>
        Assert.Equal(held, Run(colors, script).Held);

    [Fact]
    public void SetColorCount_ThenRelease_KeepsHolderCountsRight()
    {
        var allocator = new ColorAllocator<string>(5);
        foreach (var k in new[] { "A", "B", "C", "D", "E" }) allocator.Assign(k);
        allocator.SetColorCount(2); // C→0, D→1, E→0
        Assert.Equal(3, allocator.HolderCount(0));
        Assert.Equal(2, allocator.HolderCount(1));
        allocator.Release("E");
        allocator.Release("C");
        Assert.Equal(1, allocator.HolderCount(0));
        Assert.Equal(0, allocator.Assign("F"));
    }

    [Fact]
    public void TwoLists_AllocateIndependently()
    {
        var projects = new ColorAllocator<string>(12);
        var families = new ColorAllocator<string>(12);

        Assert.Equal(0, projects.Assign("Tower.rvt"));
        Assert.Equal(1, projects.Assign("Podium.rvt"));
        Assert.Equal(0, families.Assign("Door.rfa"));
        Assert.Equal(2, projects.Assign("Site.rvt"));
        Assert.Equal(1, families.Assign("Window.rfa"));
    }

    [Fact]
    public void Keys_UseTheGivenComparer()
    {
        var allocator = new ColorAllocator<string>(3, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, allocator.Assign("doc"));
        Assert.Equal(0, allocator.Assign("DOC"));
        Assert.Equal(1, allocator.Count);
        Assert.Equal(1, allocator.Assign("Other"));

        // Release through a key that is equal only under the comparer: no ghost is left.
        Assert.True(allocator.Release("Doc"));
        Assert.Equal(1, allocator.Count);
        Assert.Equal(["Other"], allocator.Keys);
        Assert.False(allocator.TryGetIndex("doc", out _));
        Assert.Equal(0, allocator.HolderCount(0));

        // SetColorCount walks the keys; a ghost would throw KeyNotFoundException here.
        allocator.SetColorCount(1);
        Assert.True(allocator.TryGetIndex("other", out var other));
        Assert.Equal(0, other);

        // Reset over Keys gives no color back to the released document.
        allocator.Reset(allocator.Keys);
        Assert.Equal(["Other"], allocator.Keys);
        Assert.False(allocator.TryGetIndex("DOC", out _));

        Assert.True(allocator.Release("OTHER"));
        Assert.Equal(0, allocator.Count);
        Assert.Empty(allocator.Keys);
    }

    [Fact]
    public void Keys_IsASnapshot_ReleasingEveryKeyWhileEnumeratingWorks()
    {
        var allocator = new ColorAllocator<string>(12);
        foreach (var k in new[] { "A", "B", "C", "D" }) allocator.Assign(k);

        var keys = allocator.Keys;
        foreach (var key in allocator.Keys) Assert.True(allocator.Release(key));

        Assert.Equal(0, allocator.Count);
        Assert.Empty(allocator.Keys);
        Assert.Equal(["A", "B", "C", "D"], keys); // the earlier snapshot did not change
        Assert.False(keys is List<string>);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)keys).Add("E"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ColorCount_BelowOne_IsRefused(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorAllocator<string>(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorAllocator<string>(3).SetColorCount(count));
    }
}
