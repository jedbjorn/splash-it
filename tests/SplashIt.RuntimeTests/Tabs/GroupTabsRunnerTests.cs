using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Files;
using SplashIt.Revit.Tabs;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;
using Fake = SplashIt.RuntimeTests.Tabs.FakeAvalonDock;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// Spec #2: one Group Tabs click wired as in Revit, minus Revit. The colorizer's real
/// <see cref="TreeTabSurface"/> finds the tabs by type name and resolves each to its document
/// with the real <see cref="TabTitleMatcher"/> (longest match, kept titles), the shipped
/// <see cref="AvalonDockPane"/> finds the panes, and <see cref="TabGrouper"/> groups them. The
/// layout model is a fake shaped like AvalonDock's.
/// </summary>
public class GroupTabsRunnerTests
{
    /// <summary>Named like AvalonDock's control, so the surface recognizes it by name only.</summary>
    private sealed class LayoutDocumentPaneControl : TabControl;

    /// <summary>Named like AvalonDock's docking host.</summary>
    private sealed class DockingManager : ContentControl;

    /// <summary>Open documents: current titles, and titles kept from before a Save As.</summary>
    private static readonly string[][] Current = [["Tower"], ["Tower B"], ["Family1"]];
    private static readonly string[][] Kept = [["OldTower"], [], []];
    private static readonly object[] DocumentKeys = ["doc#1", "doc#2", "doc#3"];

    private static object? ResolveKey(TabItem tab, string title)
    {
        var match = TabTitleMatcher.Find(title, Current, Kept);
        return match.IsMatch ? DocumentKeys[match.Index] : null;
    }

    /// <summary>
    /// A Revit-like tree: one <c>LayoutDocumentPaneControl</c> per pane, each tab's DataContext
    /// the pane model's <c>LayoutDocument</c> and its tool tip the full <c>"doc - view"</c> title.
    /// </summary>
    private static (Border Root, List<Fake.LayoutDocumentPane> Models) Tree(params string[][] panes)
    {
        var grid = new Grid();
        var models = new List<Fake.LayoutDocumentPane>();
        foreach (var titles in panes)
        {
            var model = new Fake.LayoutDocumentPane();
            var control = new LayoutDocumentPaneControl();
            foreach (var title in titles)
            {
                var document = new Fake.LayoutDocument(title);
                model.Add(document);
                control.Items.Add(new TabItem { Header = title, DataContext = document, ToolTip = title });
            }
            models.Add(model);
            grid.Children.Add(control);
        }
        var root = new Border { Child = new DockingManager { Content = grid } };
        HostTabs.Layout(root);
        return (root, models);
    }

    private static TreeTabSurface Surface(Visual root, ListLog log) =>
        new(roots => roots.Add(root), () => true, ResolveKey, log);

    private static string[] Titles(Fake.LayoutDocumentPane model) => model.Children.Select(c => c.Title).ToArray();

    private static string Selected(Fake.LayoutDocumentPane model) => model.Children[model.SelectedContentIndex].Title;

    [Fact]
    public void Run_GroupsByDocument_ActiveFileFirst_LongestTitleAndKeptTitles_UnmatchedLast_SelectionKept()
    {
        Screenshots.RunSta(() =>
        {
            var (root, models) = Tree(new[] { "Tower B - L1", "Tower - L1", "Family1 - Ref. Level", "Tower - L2", "OldTower - L3", "Tower B - L2", "Elsewhere - X" });
            var model = models[0];
            model.SelectedContentIndex = 3; // Tower - L2: Tower is the active file, so it goes first (#28)
            var log = new ListLog();

            var result = GroupTabsRunner.Run(Surface(root, log), new AvalonDockPane.Finder(log).Find, log);

            Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
            // "Tower B - L1" is Tower B's, not Tower's (longest match); "OldTower - L3" is Tower's (kept title).
            Assert.Equal(
                new[] { "Tower - L1", "Tower - L2", "OldTower - L3", "Tower B - L1", "Tower B - L2", "Family1 - Ref. Level", "Elsewhere - X" },
                Titles(model));
            Assert.Equal("Tower - L2", Selected(model));
            Assert.Equal(result.Moves, model.MoveChildCalls.Count);
            Assert.DoesNotContain(log.Lines, l => l.StartsWith("Error", StringComparison.Ordinal) || l.StartsWith("Warn", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Run_TwoPanes_EachIsGroupedWithinItself()
    {
        Screenshots.RunSta(() =>
        {
            var (root, models) = Tree(
                new[] { "Tower - L1", "Family1 - Ref. Level", "Tower - L2" },
                new[] { "Family1 - Front", "Tower B - L1", "Family1 - Back" });
            models[0].SelectedContentIndex = 1;
            models[1].SelectedContentIndex = 1;
            var left = models[0].Children.ToHashSet();
            var right = models[1].Children.ToHashSet();
            var log = new ListLog();

            var result = GroupTabsRunner.Run(Surface(root, log), new AvalonDockPane.Finder(log).Find, log);

            Assert.Equal(TabGroupOutcome.Grouped, result.Outcome);
            Assert.Equal(2, result.Panes);
            // Each pane leads with the file of its own selected tab.
            Assert.Equal(
                new[] { "Family1 - Ref. Level", "Tower - L1", "Tower - L2" }, Titles(models[0]));
            Assert.Equal(
                new[] { "Tower B - L1", "Family1 - Front", "Family1 - Back" }, Titles(models[1]));
            Assert.True(left.SetEquals(models[0].Children));
            Assert.True(right.SetEquals(models[1].Children));
            Assert.Equal("Family1 - Ref. Level", Selected(models[0]));
            Assert.Equal("Tower B - L1", Selected(models[1]));
        });
    }

    [Fact]
    public void Run_AlreadyGrouped_MovesNothing()
    {
        Screenshots.RunSta(() =>
        {
            var (root, models) = Tree(new[] { "Tower - L1", "Tower - L2", "Family1 - Ref. Level", "Elsewhere - X" });
            models[0].SelectedContentIndex = 3;
            var log = new ListLog();

            var result = GroupTabsRunner.Run(Surface(root, log), new AvalonDockPane.Finder(log).Find, log);

            Assert.Equal(TabGroupOutcome.AlreadyGrouped, result.Outcome);
            Assert.Empty(models[0].MoveChildCalls);
            Assert.Equal(3, models[0].SelectedContentIndex);
        });
    }

    [Fact]
    public void Run_InvalidatesTheSurfaceFirst_AndReportsItUnavailable_WithoutTouchingAnything()
    {
        Screenshots.RunSta(() =>
        {
            var surface = new FakeSurface(new TabControl()) { UnavailableReason = "No pane (test)." };
            var log = new ListLog();
            var finds = 0;

            var result = GroupTabsRunner.Run(surface, _ => { finds++; return default; }, log);

            Assert.Equal(TabGroupResult.Unavailable("No pane (test)."), result);
            Assert.Equal(1, surface.Invalidations);
            Assert.Equal(0, finds);
            Assert.Equal("Warn group tabs: unavailable; nothing moved. No pane (test).", Assert.Single(log.Lines));
        });
    }

    [Fact]
    public void Run_WhenAnyTabSitsOutsideADocumentPane_NothingMovesAnywhere()
    {
        Screenshots.RunSta(() =>
        {
            var (root, models) = Tree(new[] { "Tower - L1", "Family1 - Ref. Level", "Tower - L2" }, new[] { "Tower B - L1" });
            models[1].Children[0].Parent = new Fake.LayoutAnchorablePane();
            var log = new ListLog();

            var result = GroupTabsRunner.Run(Surface(root, log), new AvalonDockPane.Finder(log).Find, log);

            Assert.Equal(TabGroupOutcome.Unavailable, result.Outcome);
            Assert.Equal("A document tab's parent is LayoutAnchorablePane, not LayoutDocumentPane.", result.Reason);
            Assert.Empty(models[0].MoveChildCalls);
            Assert.Contains("Warn group tabs: unavailable; nothing moved. A document tab's parent is LayoutAnchorablePane, not LayoutDocumentPane.", log.Lines);
        });
    }

    [Fact]
    public void Run_DocumentsOpenButNoTabs_IsUnavailable()
    {
        Screenshots.RunSta(() =>
        {
            var log = new ListLog();

            var result = GroupTabsRunner.Run(new FakeSurface(new TabControl()), _ => throw new InvalidOperationException(), log);

            Assert.Equal(TabGroupResult.Unavailable(GroupTabsRunner.NoTabsReason), result);
            Assert.Equal(TabGroupNotice.CantGroup, result.Notice);
            Assert.Equal("Warn group tabs: unavailable; nothing moved. " + GroupTabsRunner.NoTabsReason, Assert.Single(log.Lines));
        });
    }

    /// <summary>
    /// Review m2: with coloring off nothing has started the surface's 5 s grace period, so on
    /// the first click a tree without the pane control reads as "no tabs yet". The click must
    /// still say it can't group, not do nothing.
    /// </summary>
    [Fact]
    public void Run_ATreeWithoutThePaneControl_IsUnavailable_OnTheFirstClick()
    {
        Screenshots.RunSta(() =>
        {
            var strip = new TabControl { Items = { new TabItem { Header = "x", ToolTip = "Tower - L1" } } };
            var root = new Border { Child = new DockingManager { Content = strip } };
            HostTabs.Layout(root);
            var log = new ListLog();

            var result = GroupTabsRunner.Run(Surface(root, log), new AvalonDockPane.Finder(log).Find, log);

            Assert.Equal(TabGroupOutcome.Unavailable, result.Outcome);
            Assert.Equal(GroupTabsRunner.NoTabsReason, result.Reason);
        });
    }

    [Fact]
    public void Run_ASurfaceThatThrows_IsUnavailable_NotAnException()
    {
        Screenshots.RunSta(() =>
        {
            var surface = new FakeSurface(new TabControl()) { Throw = new InvalidCastException() };

            var result = GroupTabsRunner.Run(surface, _ => default, new ListLog());

            Assert.Equal(TabGroupResult.Unavailable("Finding the tabs failed (InvalidCastException)."), result);
        });
    }
}
