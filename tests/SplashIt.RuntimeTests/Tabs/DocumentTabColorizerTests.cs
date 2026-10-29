using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Files;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;
using SplashIt.Core.Theming;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// AC5: the colorizer styles, restyles and restores against a synthetic tab tree (real WPF
/// TabItems in a TabControl behind a fake surface); Other-kind tabs are untouched; an
/// unchanged signature is skipped; Unavailable never throws; 3 faults → Faulted with tabs restored.
/// </summary>
public class DocumentTabColorizerTests
{
    /// <summary>A strip of tabs over two projects, a family and an .rte, wired to fakes.</summary>
    private sealed class Rig
    {
        public readonly object P1 = new DocKey("P1"), P2 = new DocKey("P2"), F1 = new DocKey("F1"), T1 = new DocKey("T1");
        public readonly Style Original = HostTabs.RevitLikeStyle(UiTheme.Light);
        public readonly TabControl Strip;
        public readonly FakeSurface Surface;
        public readonly FakeDocuments Documents = new();
        public readonly FakeEvents Events = new();
        public readonly FakeGuard Guard = new(false);
        public readonly ListLog Log = new();
        public readonly TabStyleFactory Styles = new();
        public readonly DocumentTabColorizer Colorizer;

        public Rig(bool openAll = true, Func<long>? clock = null)
        {
            Strip = HostTabs.Strip(Original, UiTheme.Light,
                "P1 - L1", "P1 - L2", "P2 - L1", "F1 - Ref. Level", "T1 - L1", "Unresolved - L1");
            Surface = new FakeSurface(Strip);
            Surface.Bind(Tab(0), P1);
            Surface.Bind(Tab(1), P1);
            Surface.Bind(Tab(2), P2);
            Surface.Bind(Tab(3), F1);
            Surface.Bind(Tab(4), T1);
            // Tab 5 has no document key: the surface could not resolve it. It is the selected
            // tab, so every colored tab starts unselected.
            Tab(5).IsSelected = true;
            if (openAll)
            {
                Documents.Add(P1, FileKind.Project);
                Documents.Add(P2, FileKind.Project);
                Documents.Add(F1, FileKind.Family);
                Documents.Add(T1, FileKind.Other);
            }
            Colorizer = new DocumentTabColorizer(Surface, Documents, Events, Styles, Guard, Log, clock);
        }

        public TabItem Tab(int i) => HostTabs.Tab(Strip, i);

        public IEnumerable<TabItem> Tabs => Strip.Items.Cast<TabItem>();

        public Color Fill(int i) => ((SolidColorBrush)Tab(i).Background).Color;
    }

    private sealed record DocKey(string Name);

    private static Color Opaque(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>An unselected project tab at the default fill (100 %) and Unselected tab opacity (50 %, #23).</summary>
    private static Color Muted(Rgb c) => At(c, TabTreatment.DefaultUnselectedPercent);

    private static Color At(Rgb c, int percent) =>
        Color.FromArgb((byte)Math.Round(percent / 100.0 * 255, MidpointRounding.AwayFromZero), c.R, c.G, c.B);

    [Fact]
    public void Start_StylesProjectsAndFamilies_AndLeavesOtherAndUnresolvedTabsUntouched()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            using var changes = new StyleChangeCounter(new[] { rig.Tab(4), rig.Tab(5) });

            rig.Colorizer.Start();

            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
            Assert.True(rig.Events.IsSubscribed);
            // Projects: fills from Project colors, BasedOn the original, in tab order (P1 → 1, P2 → 2).
            for (var i = 0; i < 3; i++)
            {
                Assert.True(TabStyleFactory.IsDerived(rig.Tab(i).Style), $"tab {i}");
                Assert.Same(rig.Original, rig.Tab(i).Style.BasedOn);
            }
            Assert.Same(rig.Tab(0).Style, rig.Tab(1).Style); // tabs of one file share it
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), rig.Fill(0));
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), rig.Fill(2));
            // Family: a bar from Family colors.
            var bar = TabDecoration.GetDecoration(rig.Tab(3));
            Assert.NotNull(bar);
            Assert.Equal(SplashItDefaults.FamilyBarWidth, bar!.LeftBarWidth);
            Assert.Equal(Opaque(SplashItDefaults.FamilyColors[0]), ((SolidColorBrush)bar.LeftBar!).Color);
            // Other and unresolved: never assigned.
            Assert.Same(rig.Original, rig.Tab(4).Style);
            Assert.Same(rig.Original, rig.Tab(5).Style);
            Assert.Equal(0, changes.Count);
            Assert.Equal(4, rig.Colorizer.StyledTabCount);
        });
    }

    [Fact]
    public void UnchangedSignature_MakesNoStyleAssignment_AndAllocatesNothing()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            for (var i = 0; i < 20; i++) rig.Events.Layout(); // warm up (JIT)

            using var changes = new StyleChangeCounter(rig.Tabs);
            var readsBefore = rig.Surface.Reads;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 500; i++) rig.Events.Layout();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(500, rig.Surface.Reads - readsBefore); // every pass read the tabs...
            Assert.Equal(0, changes.Count);                     // ...and assigned nothing
            Assert.Equal(0, allocated);
        });
    }

    [Fact]
    public void SelectionChange_RunsAPass_ButAssignsNothing_TheStylesTriggerHandlesIt()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            using var changes = new StyleChangeCounter(rig.Tabs);

            rig.Tab(2).IsSelected = true;
            rig.Events.Layout();

            Assert.Equal(0, changes.Count);
            Assert.Equal(Opaque(SplashItDefaults.ProjectColors[1]), rig.Fill(2));
            Assert.NotNull(TabDecoration.GetDecoration(rig.Tab(2))?.TopAccent);
        });
    }

    [Fact]
    public void Apply_RestylesOpenTabsAtOnce()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Apply(SplashItSettings.Default);
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);

            var red = Rgb.Parse("#E15759");
            var green = Rgb.Parse("#59A14F");
            rig.Colorizer.Apply(SplashItSettings.Default with
            {
                ProjectColors = [red, green],
                ProjectFillOpacity = 40,
                FamilyBarWidth = 6,
                FamilyBarOpacity = 30,
            });

            Assert.Equal(At(red, 20), rig.Fill(0));   // unselected: 50 % of the 40 % fill (#23)
            Assert.Equal(At(green, 20), rig.Fill(2));
            var bar = TabDecoration.GetDecoration(rig.Tab(3))!;
            Assert.Equal(6, bar.LeftBarWidth);
            Assert.Equal(At(SplashItDefaults.FamilyColors[0], 30), ((SolidColorBrush)bar.LeftBar!).Color);
            Assert.Same(rig.Original, rig.Tab(0).Style.BasedOn); // still based on the true original
        });
    }

    [Fact]
    public void Stop_RestoresEveryOriginalByReference_AndLeavesNoSubscription()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            // One tab with no local style at all: restore must clear, not set.
            rig.Tab(1).ClearValue(FrameworkElement.StyleProperty);
            rig.Colorizer.Start();
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(1).Style));

            rig.Colorizer.Stop();

            Assert.Equal(ColorizerStatus.Off, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.Equal(1, rig.Events.Unsubscribes);
            foreach (var i in new[] { 0, 2, 3, 4, 5 }) Assert.Same(rig.Original, rig.Tab(i).Style);
            Assert.Equal(DependencyProperty.UnsetValue, rig.Tab(1).ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Null(TabDecoration.GetDecoration(rig.Tab(3)));
            Assert.Equal(0, rig.Colorizer.StyledTabCount);

            // Further layout passes do nothing once stopped.
            var reads = rig.Surface.Reads;
            rig.Events.Layout();
            Assert.Equal(reads, rig.Surface.Reads);
        });
    }

    [Fact]
    public void ApplyDisabled_TurnsItOff_AndApplyEnabled_TurnsItBackOn()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Apply(SplashItSettings.Default);
            rig.Colorizer.Apply(SplashItSettings.Default with { Enabled = false });

            Assert.Equal(ColorizerStatus.Off, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));

            rig.Colorizer.Apply(SplashItSettings.Default);
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
        });
    }

    [Fact]
    public void InvalidSettings_AreRefused_AndTheColoringStays()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Apply(SplashItSettings.Default);
            var before = rig.Tab(0).Style;

            rig.Colorizer.Apply(SplashItSettings.Default with { ProjectColors = [] });

            Assert.Same(before, rig.Tab(0).Style);
            Assert.Equal(SplashItDefaults.ProjectColors.Count, rig.Colorizer.Settings.ProjectColors.Count);
            Assert.Contains(rig.Log.Lines, l => l.Contains("settings refused"));
        });
    }

    [Fact]
    public void ClosingAFile_FreesItsColor_AndTheNextFileTakesIt()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();

            // P1 closes: its tabs go.
            rig.Strip.Items.RemoveAt(1);
            var closedTab = rig.Tab(0);
            rig.Strip.Items.RemoveAt(0);
            rig.Documents.Open.Remove(rig.P1);
            rig.Events.Sink!.OnDocumentClosed(rig.P1);
            Assert.Same(rig.Original, closedTab.Style); // a tab that disappears gets its original back

            // P3 opens and takes colour 1 again; P2 keeps colour 2.
            var p3 = new DocKey("P3");
            rig.Documents.Add(p3, FileKind.Project);
            var tab = new TabItem { Header = "P3 - L1", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Surface.Bind(tab, p3);
            rig.Events.Sink!.OnDocumentOpened(p3);

            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), ((SolidColorBrush)tab.Background).Color);
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), rig.Fill(0)); // P2, now first
            Assert.True(rig.Surface.Invalidations >= 2);
        });
    }

    [Fact]
    public void SaveAsAnotherKind_Reclassifies_AndAnOtherKindGoesNative()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(2).Style));

            rig.Documents.Kinds[rig.P2] = FileKind.Other; // saved as .rte
            rig.Events.Sink!.OnDocumentSavedAs(rig.P2);

            Assert.Same(rig.Original, rig.Tab(2).Style);
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
        });
    }

    [Fact]
    public void ResetAssignments_ReassignsFromTheStart_InTabOrder()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            // Both close (their tabs gone from the surface), then P2 reopens before P1:
            // P2 now holds colour 1 and P1 colour 2.
            rig.Surface.Unbind(rig.Tab(0));
            rig.Surface.Unbind(rig.Tab(1));
            rig.Surface.Unbind(rig.Tab(2));
            rig.Events.Sink!.OnDocumentClosed(rig.P1);
            rig.Events.Sink!.OnDocumentClosed(rig.P2);
            rig.Surface.Bind(rig.Tab(2), rig.P2);
            rig.Events.Sink!.OnDocumentOpened(rig.P2);
            rig.Surface.Bind(rig.Tab(0), rig.P1);
            rig.Surface.Bind(rig.Tab(1), rig.P1);
            rig.Events.Sink!.OnDocumentOpened(rig.P1);
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), rig.Fill(0));
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), rig.Fill(2));

            rig.Colorizer.ResetAssignments();

            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), rig.Fill(0)); // P1 is first in the strip
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), rig.Fill(2));
        });
    }

    [Fact]
    public void Unavailable_NeverThrows_RestoresTheTabs_AndRecovers()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();

            rig.Surface.UnavailableReason = "No document tab pane was found.";
            rig.Events.Layout();

            Assert.Equal(ColorizerStatus.Unavailable, rig.Colorizer.Status);
            Assert.Equal("No document tab pane was found.", rig.Colorizer.StatusDetail);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
            Assert.True(rig.Colorizer.IsRunning);

            rig.Surface.UnavailableReason = null;
            rig.Events.Layout();
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
        });
    }

    [Fact]
    public void UnavailableAtStart_ReportsUnavailable_WithoutThrowing()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Surface.UnavailableReason = "unexpected tree";

            rig.Colorizer.Start();

            Assert.Equal(ColorizerStatus.Unavailable, rig.Colorizer.Status);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
        });
    }

    [Fact]
    public void ThreeFaultsInARow_StopForTheSession_WithTabsRestored()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));

            rig.Surface.Throw = new InvalidOperationException("broken tree");
            rig.Events.Layout();
            rig.Events.Layout();
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status); // two faults: still running
            rig.Events.Layout();

            Assert.Equal(ColorizerStatus.Faulted, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
            Assert.Null(TabDecoration.GetDecoration(rig.Tab(3)));
            Assert.Equal(3, rig.Log.Lines.Count(l => l.StartsWith("Error") && l.Contains("layout pass failed")));

            // It stays stopped for the session, whatever is saved.
            rig.Surface.Throw = null;
            rig.Colorizer.Apply(SplashItSettings.Default);
            rig.Colorizer.Start();
            Assert.Equal(ColorizerStatus.Faulted, rig.Colorizer.Status);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
        });
    }

    [Fact]
    public void FaultsThatAreNotInARow_DoNotStopIt()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            for (var round = 0; round < 3; round++)
            {
                rig.Surface.Throw = new InvalidOperationException("flaky");
                rig.Events.Layout();
                rig.Events.Layout();
                rig.Surface.Throw = null;
                rig.Events.Layout(); // a good pass resets the count
            }
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
        });
    }

    [Fact]
    public void AStartThatFails_LeavesNoSubscriptionOrStyle()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Events.ThrowOnSubscribe = new InvalidOperationException("no events");

            rig.Colorizer.Start();

            Assert.Equal(ColorizerStatus.Faulted, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
        });
    }

    [Fact]
    public void PyRevitRunning_BlocksTheStart_AndASaveWhileItRuns_StopsColoring()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Guard.Running = true;
            rig.Colorizer.Apply(SplashItSettings.Default);
            Assert.Equal(ColorizerStatus.BlockedByPyRevit, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));

            rig.Guard.Running = false;
            rig.Colorizer.Apply(SplashItSettings.Default);
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);

            rig.Guard.Running = true; // pyRevit turned on, then the user saves
            rig.Colorizer.Apply(SplashItSettings.Default);
            Assert.Equal(ColorizerStatus.BlockedByPyRevit, rig.Colorizer.Status);
            Assert.False(rig.Events.IsSubscribed);
            Assert.All(rig.Tabs, t => Assert.Same(rig.Original, t.Style));
        });
    }

    [Fact]
    public void ThemeChange_RestoresRecapturesAndReapplies_WithContrastForTheNewTheme()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            // A navy at 30 %: black text over the light surface, white over the dark one.
            rig.Colorizer.Apply(SplashItSettings.Default with { ProjectColors = [Rgb.Parse("#1A3D7C")], ProjectFillOpacity = 30 });
            Assert.Equal(Colors.Black, ((SolidColorBrush)rig.Tab(0).Foreground).Color);

            // The host restyles its tabs for dark (a new original style), then raises the event.
            var darkOriginal = HostTabs.RevitLikeStyle(UiTheme.Dark);
            foreach (var tab in rig.Tabs)
                if (!TabStyleFactory.IsDerived(tab.Style)) tab.Style = darkOriginal;
            rig.Events.CurrentTheme = UiTheme.Dark;
            rig.Events.Sink!.OnThemeChanged(UiTheme.Dark);

            Assert.Equal(UiTheme.Dark, rig.Colorizer.Theme);
            Assert.Equal(Colors.White, ((SolidColorBrush)rig.Tab(0).Foreground).Color);
            // Tabs the colorizer styled were restored, then restyled on what they carry now.
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
            Assert.Same(darkOriginal, rig.Tab(4).Style);
        });
    }

    [Fact]
    public void AStyleReplacedByTheHost_IsTakenAsTheNewOriginal()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();

            var recycled = HostTabs.RevitLikeStyle(UiTheme.Light);
            rig.Tab(0).Style = recycled; // the host re-applied its container style
            rig.Events.Layout();

            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
            Assert.Same(recycled, rig.Tab(0).Style.BasedOn);
            rig.Colorizer.Stop();
            Assert.Same(recycled, rig.Tab(0).Style);
        });
    }

    [Fact]
    public void DocumentsAlreadyOpenAtStart_TakeColorsInTabOrder()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Documents.Open.Reverse(); // Revit lists them in another order
            rig.Colorizer.Start();

            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), rig.Fill(0));
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), rig.Fill(2));
        });
    }

    [Fact]
    public void OffThread_PublicCallsAreRefused()
    {
        DocumentTabColorizer? colorizer = null;
        Screenshots.RunSta(() => colorizer = new Rig().Colorizer);
        Assert.Throws<InvalidOperationException>(() => colorizer!.Start());
    }

    [Fact]
    public void ATabSomeoneKeepsRestyling_IsLeftAloneAfterTheLimit()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();

            Style? foreign = null;
            for (var i = 0; i < DocumentTabColorizer.MaxRecaptures + 5; i++)
            {
                foreign = HostTabs.RevitLikeStyle(UiTheme.Light); // another colorizer, every pass
                rig.Tab(0).Style = foreign;
                rig.Events.Layout();
            }

            Assert.Same(foreign, rig.Tab(0).Style);                       // theirs now: no fight loop
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(1).Style));     // the others stay colored
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(2).Style));
            Assert.Single(rig.Log.Lines, l => l.Contains("left alone"));
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
        });
    }

    [Fact]
    public void Stop_PutsTheCloseButtonsOwnColorBack()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            var root = HostTabs.Host(rig.Strip, SplashItDefaults.LightSurface);
            HostTabs.Layout(root);
            var close = TabCloseGlyph.FindButton(rig.Tab(2))!;
            Assert.Equal(HostTabs.CloseGrey, ((SolidColorBrush)close.Foreground).Color);

            rig.Colorizer.Start();
            HostTabs.Layout(root);
            Assert.Equal(((SolidColorBrush)rig.Tab(2).Foreground).Color, ((SolidColorBrush)close.Foreground).Color);
            Assert.NotEqual(HostTabs.CloseGrey, ((SolidColorBrush)close.Foreground).Color);
            Assert.Equal(HostTabs.CloseGrey, ((SolidColorBrush)TabCloseGlyph.FindButton(rig.Tab(3))!.Foreground).Color); // family: native

            rig.Colorizer.Stop();
            HostTabs.Layout(root);
            Assert.Equal(DependencyProperty.UnsetValue, close.ReadLocalValue(Control.ForegroundProperty));
            Assert.Equal(HostTabs.CloseGrey, ((SolidColorBrush)close.Foreground).Color);
        });
    }

    [Fact]
    public void AStatusListenerThatThrows_DoesNotEscape()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.StatusChanged += (_, _) => throw new InvalidOperationException("listener");
            rig.Colorizer.Start();
            rig.Surface.UnavailableReason = "gone";
            rig.Events.Layout();
            Assert.Equal(ColorizerStatus.Unavailable, rig.Colorizer.Status);
            Assert.Contains(rig.Log.Lines, l => l.Contains("status listener failed"));
        });
    }

    /// <summary>Re-review r2: a close button whose local color cannot be put back exactly is left alone.</summary>
    [Fact]
    public void ACloseButtonWithAnUnknownLocalValue_IsNotRecolored()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            var root = HostTabs.Host(rig.Strip, SplashItDefaults.LightSurface);
            HostTabs.Layout(root);
            var tab = rig.Tab(2);
            var close = TabCloseGlyph.FindButton(tab)!;
            var grey = HostTabs.Brush(Rgb.Parse("#808080"));
            tab.Resources["GlyphBrush"] = grey;
            close.SetResourceReference(Control.ForegroundProperty, "GlyphBrush"); // reads back as an expression
            var local = close.ReadLocalValue(Control.ForegroundProperty);

            rig.Colorizer.Start();
            HostTabs.Layout(root);
            Assert.True(TabStyleFactory.IsDerived(tab.Style));
            Assert.Same(grey, close.Foreground);                                 // not recolored
            Assert.Null(TabCloseGlyph.FindRecolored(tab));

            rig.Colorizer.Stop();
            HostTabs.Layout(root);
            Assert.Same(local.GetType(), close.ReadLocalValue(Control.ForegroundProperty).GetType()); // still the resource reference
            Assert.Same(grey, close.Foreground);
            Assert.NotEqual(DependencyProperty.UnsetValue, close.ReadLocalValue(Control.ForegroundProperty));
        });
    }

    // ---- SC-004: the registration fallback (FnB decision #26) ----

    /// <summary>
    /// A family opened with no "opened" event (Edit Family in the rc.1 report): its tab first
    /// resolves to nothing; the pass asks for a scan; the scan (the next Idling in Revit)
    /// registers it, and its tab gets the family bar.
    /// </summary>
    [Fact]
    public void ADocumentNoEventAnnounced_IsRegisteredByTheScan_AndItsTabsGetColored()
    {
        Screenshots.RunSta(() =>
        {
            long now = 0;
            var rig = new Rig(clock: () => now);
            rig.Surface.IsKnown = rig.Documents.IsRegistered;
            rig.Colorizer.Start();
            now += DocumentTabColorizer.DocumentScanIntervalMs;
            var requestsAtStart = rig.Events.ScanRequests;

            // Revit opens a second family and raises no event: only its tab appears.
            var f2 = new DocKey("F2");
            rig.Documents.AddUnannounced(f2, FileKind.Family);
            var tab = new TabItem { Header = "F2 - Ref. Level", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Surface.Bind(tab, f2);
            rig.Events.Layout();

            Assert.Same(rig.Original, tab.Style);                         // unresolved: left alone...
            Assert.Equal(requestsAtStart + 1, rig.Events.ScanRequests);   // ...and a scan is asked for
            var invalidations = rig.Surface.Invalidations;

            rig.Events.Scan();

            Assert.True(TabStyleFactory.IsDerived(tab.Style));
            var bar = TabDecoration.GetDecoration(tab);
            Assert.NotNull(bar);
            Assert.Equal(Opaque(SplashItDefaults.FamilyColors[1]), ((SolidColorBrush)bar!.LeftBar!).Color); // F1 holds colour 1
            Assert.Equal(SplashItDefaults.FamilyBarWidth, bar.LeftBarWidth);
            Assert.True(rig.Surface.Invalidations > invalidations);
            Assert.Contains(rig.Log.Lines, l => l.Contains("document scan registered 1 documents no event announced (0 project, 1 family, 0 other)"));
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style)); // the others keep theirs
            Assert.Equal(ColorizerStatus.Active, rig.Colorizer.Status);
        });
    }

    /// <summary>A project no event announced is registered by the scan too, and takes the next free project colour.</summary>
    [Fact]
    public void AProjectNoEventAnnounced_TakesTheNextFreeColor()
    {
        Screenshots.RunSta(() =>
        {
            long now = 0;
            var rig = new Rig(clock: () => now);
            rig.Surface.IsKnown = rig.Documents.IsRegistered;
            rig.Colorizer.Start();
            now += DocumentTabColorizer.DocumentScanIntervalMs;

            var p3 = new DocKey("P3");
            rig.Documents.AddUnannounced(p3, FileKind.Project);
            var tab = new TabItem { Header = "P3 - L1", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Surface.Bind(tab, p3);
            rig.Events.Layout();
            rig.Events.Scan();

            Assert.Equal(Muted(SplashItDefaults.ProjectColors[2]), ((SolidColorBrush)tab.Background).Color);
        });
    }

    /// <summary>
    /// Scans are asked for only while tabs are unresolved, at most once per second, and at most
    /// <see cref="DocumentTabColorizer.MaxScansPerChange"/> times until the tabs change.
    /// </summary>
    [Fact]
    public void ScanRequests_AreThrottled_AndOnlyWhileTabsAreUnresolved()
    {
        Screenshots.RunSta(() =>
        {
            long now = 100_000;
            var rig = new Rig(clock: () => now);
            rig.Colorizer.Start();          // tab 5 never resolves (no document has its title)
            Assert.Equal(1, rig.Events.ScanRequests);

            for (var i = 0; i < 50; i++) rig.Events.Layout(); // same second: no more
            Assert.Equal(1, rig.Events.ScanRequests);

            now += DocumentTabColorizer.DocumentScanIntervalMs;
            rig.Events.Layout();
            Assert.Equal(2, rig.Events.ScanRequests);
            rig.Events.Scan();              // finds nothing new: no pass, nothing restyled
            now += DocumentTabColorizer.DocumentScanIntervalMs;
            rig.Events.Layout();
            Assert.Equal(3, rig.Events.ScanRequests);
            for (var s = 0; s < 5; s++)
            {
                now += DocumentTabColorizer.DocumentScanIntervalMs;
                rig.Events.Layout();
            }
            Assert.Equal(DocumentTabColorizer.MaxScansPerChange, rig.Events.ScanRequests); // the budget is spent

            // The tabs change (another tab selected): the unresolved tab may ask again.
            rig.Tab(0).IsSelected = true;
            now += DocumentTabColorizer.DocumentScanIntervalMs;
            rig.Events.Layout();
            Assert.Equal(DocumentTabColorizer.MaxScansPerChange + 1, rig.Events.ScanRequests);

            // Every tab resolved: no request, however long it runs.
            rig.Surface.Bind(rig.Tab(5), rig.T1);
            rig.Events.Layout();
            var settled = rig.Events.ScanRequests;
            for (var s = 0; s < 10; s++)
            {
                now += DocumentTabColorizer.DocumentScanIntervalMs;
                rig.Events.Layout();
            }
            Assert.Equal(settled, rig.Events.ScanRequests);
        });
    }

    /// <summary>The unchanged hot path with an unresolved tab still allocates nothing, scan requests included.</summary>
    [Fact]
    public void UnchangedSignature_WithAnUnresolvedTab_AllocatesNothing_EvenWhenAskingForScans()
    {
        Screenshots.RunSta(() =>
        {
            long now = 0;
            var rig = new Rig(clock: () => now);
            rig.Colorizer.Start();
            for (var i = 0; i < 20; i++) rig.Events.Layout();
            now += 10 * DocumentTabColorizer.DocumentScanIntervalMs;
            rig.Tab(0).IsSelected = true;   // a change: the budget is refilled
            rig.Events.Layout();
            var requests = rig.Events.ScanRequests;

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 500; i++)
            {
                now += 10;                  // 5 s in all: the throttle lets requests through
                rig.Events.Layout();
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(rig.Events.ScanRequests > requests);
            Assert.Equal(0, allocated);
        });
    }

    /// <summary>A scan that finds nothing new restyles nothing; one that finds a document gone forgets it and frees its colour.</summary>
    [Fact]
    public void AScan_WithNothingNew_ChangesNothing_AndForgetsDocumentsNoLongerOpen()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            using var changes = new StyleChangeCounter(rig.Tabs);
            var invalidations = rig.Surface.Invalidations;

            rig.Events.Scan();
            Assert.Equal(0, changes.Count);
            Assert.Equal(invalidations, rig.Surface.Invalidations);
            Assert.Contains(rig.Log.Lines, l => l.Contains("document scan registered 0 documents") && l.Contains("0 changed title or kind, 0 reclassified; 0 no longer open; 1 unresolved."));

            // P1 closed with no event: the scan forgets it, so P3 takes colour 1.
            rig.Strip.Items.RemoveAt(1);
            rig.Strip.Items.RemoveAt(0);
            rig.Documents.Open.Remove(rig.P1);
            rig.Events.Scan();
            Assert.Contains(rig.Log.Lines, l => l.Contains("1 no longer open"));
            var p3 = new DocKey("P3");
            rig.Documents.Add(p3, FileKind.Project);
            var tab = new TabItem { Header = "P3 - L1", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Surface.Bind(tab, p3);
            rig.Events.Sink!.OnDocumentOpened(p3);
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[0]), ((SolidColorBrush)tab.Background).Color);
        });
    }

    /// <summary>A scan that throws counts as a fault (never escapes), and a scan after Stop does nothing.</summary>
    [Fact]
    public void AScanThatFails_IsAFault_AndAScanAfterStop_DoesNothing()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            var sink = rig.Events.Sink!;
            rig.Surface.Throw = new InvalidOperationException("tree");
            var p3 = new DocKey("P3");
            rig.Documents.AddUnannounced(p3, FileKind.Project);
            sink.OnDocumentScan();          // the pass after it throws
            Assert.Contains(rig.Log.Lines, l => l.Contains("document scan failed") && l.Contains("fault 1 of 3"));
            rig.Surface.Throw = null;

            rig.Colorizer.Stop();
            var listings = rig.Documents.Listings;
            sink.OnDocumentScan();
            Assert.Equal(listings, rig.Documents.Listings);
            Assert.Equal(0, rig.Colorizer.StyledTabCount);
        });
    }

    /// <summary>
    /// Review m1: a scan that finds a known document's kind changed (a Save As no event reported)
    /// handles it like a Save As: an .rte goes native and frees its colour.
    /// </summary>
    [Fact]
    public void AScan_ThatFindsAKindChanged_ReclassifiesLikeASaveAs()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            rig.Colorizer.Start();
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(2).Style));

            rig.Documents.OnNextListing = () =>
            {
                rig.Documents.Kinds[rig.P2] = FileKind.Other; // saved as .rte, no event
                return 1;
            };
            rig.Events.Scan();

            Assert.Same(rig.Original, rig.Tab(2).Style);
            Assert.True(TabStyleFactory.IsDerived(rig.Tab(0).Style));
            Assert.Contains(rig.Log.Lines, l => l.Contains("1 changed title or kind, 1 reclassified"));

            // P2's colour is free again: a new project takes colour 2.
            var p3 = new DocKey("P3");
            rig.Documents.Add(p3, FileKind.Project);
            var tab = new TabItem { Header = "P3 - L1", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Surface.Bind(tab, p3);
            rig.Events.Sink!.OnDocumentOpened(p3);
            Assert.Equal(Muted(SplashItDefaults.ProjectColors[1]), ((SolidColorBrush)tab.Background).Color);
        });
    }

    /// <summary>
    /// SC-004, the rc.1 root cause: an Edit Family document (announced, classified Family, no
    /// path, title "Support - Steel Bar") whose tab reads "Support - Steel Bar.rfa - 3D View: View 1"
    /// resolves through its title forms and gets the bar, beside a project titled "Support".
    /// </summary>
    [Fact]
    public void AnEditFamilyTab_WithTheRfaExtension_ResolvesByTitle_AndGetsTheBar()
    {
        Screenshots.RunSta(() =>
        {
            var rig = new Rig();
            var support = new DocKey("Support");
            var steelBar = new DocKey("Support - Steel Bar");
            var forms = new List<(object Key, IReadOnlyList<string> Forms)>
            {
                (support, SplashIt.Core.Files.TabTitleMatcher.TitleForms("Support", @"C:\Models\Support.rvt", isFamilyDocument: false)),
                (steelBar, SplashIt.Core.Files.TabTitleMatcher.TitleForms("Support - Steel Bar", "", isFamilyDocument: true)),
            };
            rig.Surface.ResolveTitle = title =>
            {
                var match = SplashIt.Core.Files.TabTitleMatcher.Find(title, forms.Select(f => f.Forms).ToArray());
                return match.IsMatch ? forms[match.Index].Key : null;
            };
            rig.Documents.Add(support, FileKind.Project);
            var supportTab = new TabItem { Header = "Support - Floor Plan: L1", Style = rig.Original };
            rig.Strip.Items.Add(supportTab);
            rig.Colorizer.Start();

            rig.Documents.Add(steelBar, FileKind.Family); // DocumentCreated, as in the rc.1 log
            var tab = new TabItem { Header = "Support - Steel Bar.rfa - 3D View: View 1", Style = rig.Original };
            rig.Strip.Items.Add(tab);
            rig.Events.Sink!.OnDocumentOpened(steelBar);

            var bar = TabDecoration.GetDecoration(tab);
            Assert.NotNull(bar?.LeftBar);
            Assert.Equal(Opaque(SplashItDefaults.FamilyColors[1]), ((SolidColorBrush)bar!.LeftBar!).Color); // F1 holds colour 1
            Assert.True(TabStyleFactory.IsDerived(supportTab.Style));
            Assert.Null(TabDecoration.GetDecoration(supportTab)?.LeftBar);                                // the project keeps its fill
            Assert.Contains(rig.Log.Lines, l => l.Contains("1 unresolved")); // only the rig's own unresolvable tab
            Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("2 unresolved"));
        });
    }
}
