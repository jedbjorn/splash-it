using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// The Revit-independent tree walking behind RevitTabSurface: it finds the document pane by
/// type name, reads tab titles the way Revit carries them, and reports Unavailable, never an
/// exception, on an unexpected tree (AC5).
/// </summary>
public class TreeTabSurfaceTests
{
    /// <summary>The model object behind a Revit tab: AvalonDock's LayoutDocument carries only the view name.</summary>
    public sealed class FakeLayoutDocument(string title)
    {
        public string Title { get; } = title;
    }

    /// <summary>Named like AvalonDock's control, so the surface recognizes it by name only.</summary>
    private sealed class LayoutDocumentPaneControl : TabControl;

    /// <summary>Named like AvalonDock's docking host.</summary>
    private sealed class DockingManager : ContentControl;

    private static (Border Root, LayoutDocumentPaneControl Pane) RevitLikeTree()
    {
        var pane = new LayoutDocumentPaneControl();
        foreach (var (doc, view) in new[] { ("Project1", "Floor Plan: L1"), ("Project1", "Sheet: A101"), ("Family1", "Ref. Level") })
        {
            pane.Items.Add(new TabItem
            {
                // As in Revit 2026/2027: the model's Title is the view only; the tool tip is "doc - view".
                Header = new FakeLayoutDocument(" " + view),
                DataContext = new FakeLayoutDocument(" " + view),
                ToolTip = doc + " - " + view,
            });
        }
        var host = new DockingManager { Content = new DockPanel { Children = { new Grid { Children = { pane } } } } };
        var root = new Border { Child = new Grid { Children = { new TabControl { Items = { new TabItem { Header = "a ribbon tab" } } }, host } } };
        HostTabs.Layout(root);
        return (root, pane);
    }

    private static object? ByTitlePrefix(TabItem tab, string title) =>
        title.StartsWith("Project1 - ", StringComparison.Ordinal) ? "P1"
        : title.StartsWith("Family1 - ", StringComparison.Ordinal) ? "F1"
        : null;

    [Fact]
    public void FindsTheDocumentPaneByTypeName_AndReadsTabsWithTitlesAndKeys()
    {
        Screenshots.RunSta(() =>
        {
            var (root, pane) = RevitLikeTree();
            ((TabItem)pane.Items[1]!).IsSelected = true;
            var log = new ListLog();
            var surface = new TreeTabSurface(r => r.Add(root), () => true, ByTitlePrefix, log);
            var tabs = new List<SurfaceTab>();

            var result = surface.Read(tabs);

            Assert.True(result.IsAvailable, result.Reason);
            Assert.Equal(1, surface.PaneCount);
            Assert.Equal(3, tabs.Count);
            Assert.Equal(new[] { "Project1 - Floor Plan: L1", "Project1 - Sheet: A101", "Family1 - Ref. Level" }, tabs.Select(t => t.Title));
            Assert.Equal(new object?[] { "P1", "P1", "F1" }, tabs.Select(t => t.DocumentKey));
            Assert.Equal(new[] { false, true, false }, tabs.Select(t => t.IsSelected));
            Assert.Same(pane.Items[0], tabs[0].Tab);
            // The log names counts and types, never a title.
            Assert.Contains(log.Lines, l => l.Contains("1 document panes (LayoutDocumentPaneControl)") && l.Contains("host DockingManager"));
            Assert.DoesNotContain(log.Lines, l => l.Contains("Project1"));
        });
    }

    [Fact]
    public void Titles_PreferTheToolTip_ThenTheHeader_ThenATitleProperty()
    {
        Screenshots.RunSta(() =>
        {
            var surface = new TreeTabSurface(_ => { }, () => false, (_, _) => null);
            Assert.Equal("Doc - View", surface.TitleOf(new TabItem { ToolTip = "Doc - View", Header = "other" }));
            Assert.Equal("plain", surface.TitleOf(new TabItem { Header = "plain" }));
            Assert.Equal(" View", surface.TitleOf(new TabItem { Header = new FakeLayoutDocument(" View") }));
            Assert.Equal(" Ctx", surface.TitleOf(new TabItem { Header = new object(), DataContext = new FakeLayoutDocument(" Ctx") }));
            Assert.Equal("", surface.TitleOf(new TabItem()));
        });
    }

    [Fact]
    public void KeysAreResolvedOncePerTabAndTitle_UntilInvalidated()
    {
        Screenshots.RunSta(() =>
        {
            var (root, pane) = RevitLikeTree();
            var calls = 0;
            var surface = new TreeTabSurface(r => r.Add(root), () => true, (t, title) => { calls++; return ByTitlePrefix(t, title); });
            var tabs = new List<SurfaceTab>();

            surface.Read(tabs);
            surface.Read(tabs);
            Assert.Equal(3, calls);

            ((TabItem)pane.Items[0]!).ToolTip = "Project1 - Floor Plan: L2"; // renamed view: resolve that tab again
            surface.Read(tabs);
            Assert.Equal(4, calls);

            surface.Invalidate();
            surface.Read(tabs);
            Assert.Equal(7, calls);
        });
    }

    [Fact]
    public void PanesAreCached_AndLookedForAgainAfterInvalidateOrThePeriod()
    {
        Screenshots.RunSta(() =>
        {
            var (root, _) = RevitLikeTree();
            long now = 1000;
            var surface = new TreeTabSurface(r => r.Add(root), () => true, ByTitlePrefix, clockMs: () => now)
            {
                RediscoverAfter = TimeSpan.FromSeconds(2),
            };
            var tabs = new List<SurfaceTab>();

            surface.Read(tabs);
            surface.Read(tabs);
            Assert.Equal(1, surface.WalkCount);

            now += 2500;
            surface.Read(tabs);
            Assert.Equal(2, surface.WalkCount);

            surface.Invalidate();
            surface.Read(tabs);
            Assert.Equal(3, surface.WalkCount);
        });
    }

    [Fact]
    public void NoDocumentsOpen_IsAvailableWithNoTabs_AndWalksNothing()
    {
        Screenshots.RunSta(() =>
        {
            var walked = 0;
            var surface = new TreeTabSurface(_ => walked++, () => false, ByTitlePrefix);
            var tabs = new List<SurfaceTab> { default };

            var result = surface.Read(tabs);

            Assert.True(result.IsAvailable);
            Assert.Empty(tabs);
            Assert.Equal(0, walked);
        });
    }

    [Fact]
    public void AnUnexpectedTree_IsUnavailable_NotAnException()
    {
        Screenshots.RunSta(() =>
        {
            // Plain TabControls only: no LayoutDocumentPaneControl anywhere.
            var root = new Border
            {
                Child = new StackPanel
                {
                    Children =
                    {
                        new TabControl { Items = { new TabItem { Header = "Project1 - L1" } } },
                        new TabControl { Items = { new TabItem { Header = "Project1 - L2" } } },
                    },
                },
            };
            HostTabs.Layout(root);
            var surface = new TreeTabSurface(r => r.Add(root), () => true, ByTitlePrefix) { UnavailableAfter = TimeSpan.Zero };
            var tabs = new List<SurfaceTab>();

            var result = surface.Read(tabs);

            Assert.False(result.IsAvailable);
            Assert.Equal(TreeTabSurface.NoPaneReason, result.Reason);
            Assert.Empty(tabs);
        });
    }

    [Fact]
    public void NoPaneYet_IsGivenAGracePeriod_BeforeUnavailable()
    {
        Screenshots.RunSta(() =>
        {
            var root = new Border();
            long now = 0;
            var surface = new TreeTabSurface(r => r.Add(root), () => true, ByTitlePrefix, clockMs: () => now)
            {
                UnavailableAfter = TimeSpan.FromSeconds(5),
                RetryAfter = TimeSpan.Zero,
            };
            var tabs = new List<SurfaceTab>();

            Assert.True(surface.Read(tabs).IsAvailable); // tabs may still be on their way
            now = 4000;
            Assert.True(surface.Read(tabs).IsAvailable);
            now = 5000;
            Assert.False(surface.Read(tabs).IsAvailable);
        });
    }

    [Fact]
    public void NoRoots_IsUnavailable()
    {
        Screenshots.RunSta(() =>
        {
            var surface = new TreeTabSurface(_ => { }, () => true, ByTitlePrefix);
            var result = surface.Read(new List<SurfaceTab>());
            Assert.False(result.IsAvailable);
            Assert.Equal(TreeTabSurface.NoRootsReason, result.Reason);
        });
    }

    [Fact]
    public void AnExceptionInside_IsUnavailable_NotThrown()
    {
        Screenshots.RunSta(() =>
        {
            var (root, _) = RevitLikeTree();
            var surface = new TreeTabSurface(r => r.Add(root), () => true, (_, _) => throw new InvalidOperationException("boom"));
            var tabs = new List<SurfaceTab>();

            var result = surface.Read(tabs);

            Assert.False(result.IsAvailable);
            Assert.Contains("InvalidOperationException", result.Reason);
            Assert.Empty(tabs);

            var throwingRoots = new TreeTabSurface(_ => throw new InvalidOperationException(), () => true, ByTitlePrefix);
            Assert.False(throwingRoots.Read(tabs).IsAvailable);
        });
    }

    [Fact]
    public void ItsUnavailable_DrivesTheColorizerToUnavailable_WithoutAThrow()
    {
        Screenshots.RunSta(() =>
        {
            var root = new Border { Child = new TabControl { Items = { new TabItem { Header = "x" } } } };
            HostTabs.Layout(root);
            var surface = new TreeTabSurface(r => r.Add(root), () => true, ByTitlePrefix) { UnavailableAfter = TimeSpan.Zero };
            var events = new FakeEvents();
            var colorizer = new DocumentTabColorizer(surface, new FakeDocuments(), events, new TabStyleFactory());

            colorizer.Start();
            events.Layout();

            Assert.Equal(ColorizerStatus.Unavailable, colorizer.Status);
            Assert.Equal(TreeTabSurface.NoPaneReason, colorizer.StatusDetail);
        });
    }
}
