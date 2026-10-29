using SplashIt.Core.Files;

namespace SplashIt.Core.Tests.Files;

public class TabTitleMatcherTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Docs(params string[] titles) =>
        titles.Select(t => TabTitleMatcher.TitleForms(t)).ToArray();

    [Theory]
    [InlineData("Tower - Floor Plan: L1", "Tower", true)]
    [InlineData("Tower B - Floor Plan: L1", "Tower", false)]   // " B " is not the separator
    [InlineData("Tower - Floor Plan: L1", "Tower B", false)]
    [InlineData("Tower", "Tower", true)]                      // equal
    [InlineData("Tower -", "Tower", false)]
    [InlineData("Tower - ", "Tower", false)]                  // nothing after the separator
    [InlineData("tower - L1", "Tower", false)]                // ordinal
    [InlineData("Tower - L1", "", false)]
    [InlineData("", "Tower", false)]
    public void Matches_IsExactOrTitlePlusSeparator_Ordinal(string tab, string doc, bool expected) =>
        Assert.Equal(expected, TabTitleMatcher.Matches(tab, doc));

    [Fact]
    public void Tower_And_TowerB_EachGetTheirOwnTabs()
    {
        var docs = Docs("Tower", "Tower B");
        Assert.Equal(0, TabTitleMatcher.Find("Tower - Floor Plan: L1", docs).Index);
        Assert.Equal(1, TabTitleMatcher.Find("Tower B - Floor Plan: L1", docs).Index);
        Assert.Equal(-1, TabTitleMatcher.Find("Tower C - Floor Plan: L1", docs).Index);
    }

    [Fact]
    public void TheLongestTitleWins()
    {
        var docs = Docs("A", "A - B");
        Assert.Equal(1, TabTitleMatcher.Find("A - B - Floor Plan: L1", docs).Index);
        Assert.Equal(0, TabTitleMatcher.Find("A - Floor Plan: L1", docs).Index);
    }

    [Fact]
    public void TwoDocumentsWithTheSameTitle_AreATie_AndLeaveTheTabUnresolved()
    {
        var docs = Docs("Door", "Door", "Window");
        var match = TabTitleMatcher.Find("Door - Floor Plan: Ref. Level", docs);
        Assert.False(match.IsMatch);
        Assert.True(match.Tied);
        Assert.Equal(-1, match.Index);
        Assert.Equal(2, TabTitleMatcher.Find("Window - Elevation: Front", docs).Index);
    }

    [Fact]
    public void ALongerMatchBeatsATieOnAShorterOne()
    {
        var docs = Docs("A", "A", "A - B");
        Assert.Equal(2, TabTitleMatcher.Find("A - B - L1", docs).Index);
    }

    [Theory]
    [InlineData("Tower.rvt", "Tower - Floor Plan: L1")]
    [InlineData("Tower.rvt", "Tower.rvt - Floor Plan: L1")]
    [InlineData("Tower", "Tower - Floor Plan: L1")]
    [InlineData("Door.RFA", "Door - Ref. Level")]
    public void WithAndWithoutTheExtension_BothMatch(string documentTitle, string tab) =>
        Assert.Equal(0, TabTitleMatcher.Find(tab, Docs(documentTitle)).Index);

    [Fact]
    public void TitleForms_StripOnlyRevitExtensions()
    {
        Assert.Equal(new[] { "Tower.rvt", "Tower" }, TabTitleMatcher.TitleForms("Tower.rvt"));
        Assert.Equal(new[] { "Plan.v2" }, TabTitleMatcher.TitleForms("Plan.v2"));
        Assert.Equal(new[] { ".rvt" }, TabTitleMatcher.TitleForms(".rvt"));
        Assert.Empty(TabTitleMatcher.TitleForms(""));
        Assert.Empty(TabTitleMatcher.TitleForms(null));
    }

    [Fact]
    public void TheSavedFileName_Matches_WhenTheTitleHasNoExtension()
    {
        // Revit 2026 on the VM: Title "Tower26", tabs "Tower26.rvt - Floor Plan: L1".
        var forms = TabTitleMatcher.TitleForms("Tower26", @"C:\SplashIt\laneb\Tower26.rvt");
        Assert.Equal(new[] { "Tower26", "Tower26.rvt" }, forms);
        Assert.Equal(0, TabTitleMatcher.Find("Tower26.rvt - Floor Plan: L1", [forms]).Index);
        Assert.Equal(0, TabTitleMatcher.Find("Tower26 - Floor Plan: L1", [forms]).Index);
        Assert.Equal(new[] { "Lib.rte", "Lib" }, TabTitleMatcher.TitleForms(null, "C:/x/Lib.rte"));
        Assert.Equal(new[] { "Project1" }, TabTitleMatcher.TitleForms("Project1", ""));
    }

    [Fact]
    public void ADocumentsOldTitle_StillMatches_AfterSaveAs()
    {
        IReadOnlyList<IReadOnlyList<string>> docs = [["Tower", "Project1"], ["Project2"]];
        Assert.Equal(0, TabTitleMatcher.Find("Project1 - Floor Plan: L1", docs).Index); // the tool tip not yet updated
        Assert.Equal(0, TabTitleMatcher.Find("Tower - Floor Plan: L1", docs).Index);
        Assert.Equal(1, TabTitleMatcher.Find("Project2 - Floor Plan: L1", docs).Index);
    }

    /// <summary>Re-review r1: Save As "Tower" as "Tower v2", then reopen the original Tower.rvt.</summary>
    [Fact]
    public void AReopenedOriginal_BeatsAnotherFilesKeptTitle()
    {
        IReadOnlyList<IReadOnlyList<string>> current = [TabTitleMatcher.TitleForms("Tower v2"), TabTitleMatcher.TitleForms("Tower")];
        IReadOnlyList<IReadOnlyList<string>> kept = [TabTitleMatcher.TitleForms("Tower"), []];

        Assert.Equal(1, TabTitleMatcher.Find("Tower - Floor Plan: L1", current, kept).Index);    // the reopened file
        Assert.Equal(0, TabTitleMatcher.Find("Tower v2 - Floor Plan: L1", current, kept).Index); // the saved-as file
    }

    [Fact]
    public void AKeptTitle_StillMatches_WhenNoCurrentTitleDoes()
    {
        IReadOnlyList<IReadOnlyList<string>> current = [["Tower v2"], ["Annex"]];
        IReadOnlyList<IReadOnlyList<string>> kept = [["Tower"], []];
        Assert.Equal(0, TabTitleMatcher.Find("Tower - Floor Plan: L1", current, kept).Index); // the tool tip not yet refreshed
    }

    [Fact]
    public void KeptVsKept_AndCurrentVsCurrent_StillTie()
    {
        IReadOnlyList<IReadOnlyList<string>> current = [["A v2"], ["B v2"], ["Door"], ["Door"]];
        IReadOnlyList<IReadOnlyList<string>> kept = [["Tower"], ["Tower"], [], []];

        var keptTie = TabTitleMatcher.Find("Tower - L1", current, kept);
        Assert.True(keptTie.Tied);
        Assert.False(keptTie.IsMatch);

        var currentTie = TabTitleMatcher.Find("Door - Ref. Level", current, kept);
        Assert.True(currentTie.Tied);
        Assert.False(currentTie.IsMatch);
    }

    [Fact]
    public void ALongerKeptTitle_StillBeatsAShorterCurrentOne()
    {
        IReadOnlyList<IReadOnlyList<string>> current = [["A"], ["X"]];
        IReadOnlyList<IReadOnlyList<string>> kept = [[], ["A - B"]];
        Assert.Equal(1, TabTitleMatcher.Find("A - B - L1", current, kept).Index);
    }

    // ---- SC-004, the rc.1 root cause: an Edit Family document has no path, and its tabs carry ".rfa" ----

    /// <summary>The exact strings from the rc.1 run in Revit 2026 (shared/evidence/ac11/2026, x1-f).</summary>
    [Fact]
    public void AnEditFamilyDocument_WithNoPath_MatchesItsTabWithTheRfaExtension()
    {
        var family = TabTitleMatcher.TitleForms("Support - Steel Bar", "", isFamilyDocument: true);
        Assert.Equal(new[] { "Support - Steel Bar", "Support - Steel Bar.rfa" }, family);
        var match = TabTitleMatcher.Find("Support - Steel Bar.rfa - 3D View: View 1", [family]);
        Assert.Equal(0, match.Index);
        Assert.Equal("Support - Steel Bar.rfa".Length, match.Length);
        // With the extension hidden, the title alone still matches.
        Assert.Equal(0, TabTitleMatcher.Find("Support - Steel Bar - 3D View: View 1", [family]).Index);
        // Without the kind, the form is not added (and rc.1's tab matched nothing).
        Assert.Equal(-1, TabTitleMatcher.Find("Support - Steel Bar.rfa - 3D View: View 1", [TabTitleMatcher.TitleForms("Support - Steel Bar", "")]).Index);
    }

    /// <summary>A project titled "Support" does not take the family's tab: the longest match wins.</summary>
    [Fact]
    public void AProjectNamedLikeTheFamilysPrefix_DoesNotStealItsTab()
    {
        IReadOnlyList<IReadOnlyList<string>> docs =
        [
            TabTitleMatcher.TitleForms("Support", @"C:\Models\Support.rvt", isFamilyDocument: false),
            TabTitleMatcher.TitleForms("Support - Steel Bar", "", isFamilyDocument: true),
        ];
        Assert.Equal(1, TabTitleMatcher.Find("Support - Steel Bar.rfa - 3D View: View 1", docs).Index);
        Assert.Equal(1, TabTitleMatcher.Find("Support - Steel Bar - 3D View: View 1", docs).Index);
        Assert.Equal(0, TabTitleMatcher.Find("Support - Floor Plan: L1", docs).Index);
        Assert.Equal(0, TabTitleMatcher.Find("Support.rvt - Floor Plan: L1", docs).Index);
    }

    /// <summary>A project with no path whose tab shows ".rvt" (a machine that shows extensions) matches too.</summary>
    [Fact]
    public void AProjectWithNoPath_MatchesItsTabWithTheRvtExtension()
    {
        var forms = TabTitleMatcher.TitleForms("Tower", "", isFamilyDocument: false);
        Assert.Equal(new[] { "Tower", "Tower.rvt" }, forms);
        Assert.Equal(0, TabTitleMatcher.Find("Tower.rvt - Floor Plan: L1", [forms]).Index);
        Assert.Equal(0, TabTitleMatcher.Find("Tower - Floor Plan: L1", [forms]).Index);
    }

    [Theory]
    [InlineData("Door.rfa", true, new[] { "Door.rfa", "Door" })]          // already has it: nothing added
    [InlineData("Door.RFA", true, new[] { "Door.RFA", "Door" })]          // any case
    [InlineData("Tower.rvt", false, new[] { "Tower.rvt", "Tower" })]
    [InlineData("Door", true, new[] { "Door", "Door.rfa" })]
    [InlineData("Tower", false, new[] { "Tower", "Tower.rvt" })]
    [InlineData("", true, new string[0])]
    public void TheKindsExtension_IsAddedOnlyWhenTheTitleLacksIt(string title, bool isFamily, string[] expected) =>
        Assert.Equal(expected, TabTitleMatcher.TitleForms(title, null, isFamily));

    /// <summary>A family opened from disk keeps matching through its path, as before.</summary>
    [Fact]
    public void AFamilyFromDisk_StillMatchesThroughItsPath()
    {
        var forms = TabTitleMatcher.TitleForms("Door", @"C:\Families\Door.rfa", isFamilyDocument: true);
        Assert.Equal(new[] { "Door", "Door.rfa" }, forms);
        Assert.Equal(0, TabTitleMatcher.Find("Door.rfa - Ref. Level", [forms]).Index);
    }
}
