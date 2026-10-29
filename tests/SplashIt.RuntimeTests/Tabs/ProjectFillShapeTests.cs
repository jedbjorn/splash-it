using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// #36 (supersedes #34), with #31 and #33: the project fill is solid from the left edge to a fade start
/// 26 px before the close button's measured slot (where #30's fade started), then fades linearly
/// through the × to half its opacity at the tab's right edge; the selected tab's top line takes the
/// same shape. Geometry (including a button that is not where Revit's is), hover and narrow tabs, the
/// rendered backdrop sample (never on our fill), the 3:1 floor for titles and the ×, and restore.
/// </summary>
public class ProjectFillShapeTests
{
    private static readonly Rgb Blue = SplashItDefaults.ProjectColors[0];
    private static readonly Rgb Orange = SplashItDefaults.ProjectColors[1];
    private static readonly ProjectFillShape Shape = ProjectFillShape.Default;

    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    [Fact]
    public void Default_StartsTheFade26PxBeforeTheX_AndEndsAtHalfOpacityAtTheEdge()
    {
        Assert.Equal(new ProjectFillShape(24, 2, 18, 12, 0.5, 16), ProjectFillShape.Default);
        Assert.Equal(ProjectFillShape.Default, new TabStyleFactory().ProjectFill);
        Assert.Equal(26, Shape.StartBeforeClose);
        Assert.Equal(44, Shape.TypicalRun);
        Assert.Equal(1 - 0.5 * 13.5 / 44, Shape.TitleEndShare, 10); // a title's last column: about 85 %
        var close = Shape.CloseShares();
        Assert.Equal(16, close.Length);                              // one per column of the 16 px button
        Assert.Equal(1 - 0.5 * 26.5 / 44, close[0], 10);
        Assert.Equal(1 - 0.5 * 41.5 / 44, close[^1], 10);
        Assert.Equal(1, Shape.ShareAt(-3, 44));                      // solid before the fade
        Assert.Equal(0.75, Shape.ShareAt(22, 44), 10);
        Assert.Equal(0.5, Shape.ShareAt(44, 44), 10);                // never transparent
        Assert.Equal(0.5, Shape.ShareAt(90, 44), 10);
    }

    /// <summary>The fade starts 26 px before the button's left edge (with no button slot, the 18 px slot from the right edge) and ends at the right edge.</summary>
    [Theory]
    [InlineData(155, 137.0, 111)]
    [InlineData(155, null, 111)]     // no slot: 155 - 18 - 26
    [InlineData(350, 332.0, 306)]
    [InlineData(101, 90.0, 64)]      // a button somewhere else: its slot wins
    [InlineData(10, null, 0)]        // narrower than the solid part: fades from the left edge
    [InlineData(40, -5.0, 0)]
    [InlineData(20, 30.0, 4)]
    public void TheFade_StartsBeforeTheX_AndEndsAtTheRightEdge(double width, double? closeLeft, double start)
    {
        var fade = Shape.FadeFor(width, closeLeft);
        Assert.Equal(start, fade.Start);
        Assert.Equal(width, fade.End);
    }

    [Fact]
    public void ANonPositiveWidth_HasNoFade() => Assert.Equal((0.0, 0.0), Shape.FadeFor(0, null));

    // ---- live tabs ---------------------------------------------------------------------------------

    /// <summary>
    /// A strip of project tabs on <paramref name="original"/> (default: the Revit-like style), the first
    /// selected, laid out; <paramref name="room"/> px of strip past the last tab.
    /// </summary>
    private static (FrameworkElement Root, TabItem Selected, TabItem Unselected) Strip(UiTheme theme, double width,
        Rgb? color = null, Style? original = null, string[]? titles = null, int fill = 100, double room = 0)
    {
        original ??= HostTabs.RevitLikeStyle(theme);
        var strip = HostTabs.Strip(original, theme, titles ?? ["Tower - Floor Plan: L1", "Tower - Sheet: A101"]);
        var root = HostTabs.Host(strip, theme.Surface(), padding: 12);
        foreach (TabItem tab in strip.Items)
            if (!double.IsNaN(width)) tab.Width = width;
        HostTabs.Layout(root); // in a tree, so the backdrop search finds what is behind the tabs
        if (room > 0)
        {
            strip.Width = strip.ActualWidth + room;
            HostTabs.Layout(root);
        }
        var factory = new TabStyleFactory();
        foreach (TabItem tab in strip.Items)
        {
            var backdrop = TabBackdrop.Resolve(tab, tab.Background, theme).Color;
            tab.Style = factory.Create(original, TabTreatment.ProjectFill(color ?? Blue, fill), HostTabs.Brush(backdrop), theme);
        }
        HostTabs.Tab(strip, 0).IsSelected = true;
        HostTabs.Layout(root);
        return (root, HostTabs.Tab(strip, 0), HostTabs.Tab(strip, 1));
    }

    /// <summary>The close button's left edge, measured independently of the code under test (TranslatePoint, not TransformToAncestor).</summary>
    private static double MeasuredCloseLeft(TabItem tab, string name = "mDocumentCloseButton")
    {
        var button = WindowCapture.Descendants<Button>(tab).Single(b => b.Name == name);
        return button.TranslatePoint(new Point(0, 0), tab).X;
    }

    private static Color Pixel(BitmapSource bitmap, FrameworkElement root, FrameworkElement tab, double x, double y)
    {
        var p = tab.TransformToAncestor(root).Transform(new Point(x, y));
        return Screenshots.PixelAt(bitmap, (int)p.X, (int)p.Y);
    }

    private static Color[] Row(BitmapSource bitmap, FrameworkElement root, FrameworkElement tab, double y) =>
        Enumerable.Range(0, (int)tab.ActualWidth).Select(x => Pixel(bitmap, root, tab, x + 0.5, y)).ToArray();

    private static void Near(Color expected, Color actual, int tolerance, string what) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
                    && Math.Abs(expected.B - actual.B) <= tolerance, $"{what}: expected {expected}, got {actual}");

    /// <summary>The share of the opacity the fade draws at <paramref name="x"/> on a fade from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static double ShareAt(double x, double start, double end) => Shape.ShareAt(x - start, end - start);

    /// <summary>
    /// At 101, 155, 250 and 350 px the fill and the selected tab's line start fading 26 px before the
    /// measured button and run to half the opacity at the right edge: drawn brushes, the adorner's
    /// extent, and pixels (solid, mid-fade, the last column: never the bare strip).
    /// </summary>
    [Theory]
    [InlineData(101)]
    [InlineData(155)]
    [InlineData(250)]
    [InlineData(350)]
    public void TheFade_RunsFromBeforeTheX_ToHalfAtTheEdge_AtEveryWidth(double width)
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Light;
            var (root, selected, unselected) = Strip(theme, width);
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.Well(theme);
            foreach (var tab in new[] { selected, unselected })
            {
                Assert.Equal(width, tab.ActualWidth);
                var start = MeasuredCloseLeft(tab) - Shape.StartBeforeClose;
                Assert.Equal((start, width), Shape.FadeFor(tab));

                var opacity = tab.IsSelected ? 1.0 : 0.5;
                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(BrushMappingMode.Absolute, brush.MappingMode);
                Assert.Equal(GradientSpreadMethod.Pad, brush.SpreadMethod);
                Assert.Equal(start, brush.StartPoint.X);
                Assert.Equal(width, brush.EndPoint.X);
                Assert.Equal((byte)Math.Round(opacity * 0.5 * 255, MidpointRounding.AwayFromZero), brush.GradientStops[1].Color.A);

                // Pixels (row 18.5: below the title and the ×): solid before the fade, three quarters at its middle, half at the edge.
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity, well)), Pixel(bitmap, root, tab, start - 1.5, 18.5), 3, $"{width} solid");
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity * 0.75, well)), Pixel(bitmap, root, tab, (start + width) / 2, 18.5), 6, $"{width} mid-fade");
                var last = Pixel(bitmap, root, tab, width - 0.5, 18.5);
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity * ShareAt(width - 0.5, start, width), well)), last, 6, $"{width} last column");
                Assert.NotEqual(HostTabs.ColorOf(well), last); // no colorless column
            }

            // The selected tab's line: the same shape, opaque before the fade, about half at the edge.
            var adorner = TabDecoration.FindAdorner(selected)!;
            var (fadeStart, end) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, adorner.Spec);
            Assert.Equal(MeasuredCloseLeft(selected) - Shape.StartBeforeClose, fadeStart);
            Assert.Equal(width, end);
            var fade = Assert.IsType<LinearGradientBrush>(adorner.Spec.TopAccentFadeBrush);
            Assert.Equal(255, fade.GradientStops[0].Color.A);
            Assert.Equal(128, fade.GradientStops[1].Color.A);
            var accent = ColorMath.Darken(Blue, TabStyleFactory.AccentDarken);
            Near(HostTabs.ColorOf(accent), Pixel(bitmap, root, selected, fadeStart - 1.5, 1.5), 3, $"{width} line before its fade");
            var share = ShareAt(width - 0.5, fadeStart, width);
            var underLine = ColorMath.Blend(Blue, share, well);
            Near(HostTabs.ColorOf(ColorMath.Blend(accent, share, underLine)), Pixel(bitmap, root, selected, width - 0.5, 1.5), 8, $"{width} line at the edge");
            Assert.Null(TabDecoration.FindAdorner(unselected));
        });
    }

    /// <summary>
    /// Review M2: a template whose close button is NOT at width - 18 (24 px wide, 7 px from the edge,
    /// so its left edge is at width - 32). The fade starts 26 px before the measured button, not where
    /// the right-edge fallback would put it, and still runs to the edge.
    /// </summary>
    [Theory]
    [InlineData(155)]
    [InlineData(250)]
    public void TheFadeStart_IsMeasuredFromTheButton_NotTheRightEdge(double width)
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Dark;
            var original = HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 8);
            var (root, selected, unselected) = Strip(theme, width, original: original);
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.Well(theme);
            var fallback = width - Shape.CloseSlot - Shape.StartBeforeClose;
            foreach (var tab in new[] { selected, unselected })
            {
                var measured = MeasuredCloseLeft(tab);
                Assert.Equal(width - 32, measured);
                Assert.Equal(measured, ProjectFillShape.CloseButtonLeft(tab));
                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(measured - Shape.StartBeforeClose, brush.StartPoint.X);
                Assert.NotEqual(fallback, brush.StartPoint.X);
                Assert.Equal(width, brush.EndPoint.X);
                // Where a fallback fade would still be solid, the measured one is already fading.
                var opacity = tab.IsSelected ? 1.0 : 0.5;
                var x = fallback - 1.5;
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity * ShareAt(x, brush.StartPoint.X, width), well)),
                    Pixel(bitmap, root, tab, x, 18.5), 6, "inside the measured fade");
                Assert.NotEqual(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity, well)), Pixel(bitmap, root, tab, x, 18.5));
            }
            var spec = TabDecoration.FindAdorner(selected)!.Spec;
            var (fadeStart, _) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, spec);
            Assert.Equal(MeasuredCloseLeft(selected) - Shape.StartBeforeClose, fadeStart);
        });
    }

    /// <summary>The right-edge fallback is used only when the tab has no close button to measure.</summary>
    [Fact]
    public void TheFallback_IsUsedOnlyWhenThereIsNoButton()
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Light;
            var noButton = HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 8, closeName: "SomethingElse");
            var (_, selected, unselected) = Strip(theme, 155, original: noButton);
            foreach (var tab in new[] { selected, unselected })
            {
                Assert.Null(TabCloseGlyph.FindButton(tab));
                Assert.Null(ProjectFillShape.CloseButtonLeft(tab));
                Assert.Equal(155 - Shape.CloseSlot - Shape.StartBeforeClose, ((LinearGradientBrush)tab.Background).StartPoint.X);
            }

            var (_, other, _) = Strip(theme, 155, original: HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 8));
            Assert.NotNull(ProjectFillShape.CloseButtonLeft(other));
            Assert.NotEqual(155 - Shape.CloseSlot, ProjectFillShape.CloseButtonLeft(other));
        });
    }

    /// <summary>
    /// Revit shows the × only on the selected or hovered tab, but keeps its slot (Hidden). On one
    /// unselected tab: show the × (what hover does), re-evaluate the fill and render; the fill's
    /// pixels and the fade are the same as before. A collapsed button falls back to the 18 px slot,
    /// which on Revit's geometry is the same start.
    /// </summary>
    [Fact]
    public void TheFade_DoesNotMove_WhenTheCloseButtonAppears()
    {
        Screenshots.RunSta(() =>
        {
            var (root, _, tab) = Strip(UiTheme.Light, 155);
            var button = TabCloseGlyph.FindButton(tab)!;
            Assert.Equal(Visibility.Hidden, button.Visibility); // unselected: hidden, as in Revit
            var fade = Shape.FadeFor(tab);
            var before = Row(Screenshots.Render(root), root, tab, 18.5);

            foreach (var visibility in new[] { Visibility.Visible, Visibility.Hidden, Visibility.Visible })
            {
                button.Visibility = visibility;
                HostTabs.Layout(root);
                BindingOperations.GetMultiBindingExpression(tab, Control.BackgroundProperty)!.UpdateTarget(); // re-measure now
                HostTabs.Layout(root);
                Assert.Equal(fade, Shape.FadeFor(tab));
                Assert.Equal(fade.Start, ((LinearGradientBrush)tab.Background).StartPoint.X);
                Assert.Equal(before, Row(Screenshots.Render(root), root, tab, 18.5)); // the row under the ×, pixel for pixel
            }

            button.Visibility = Visibility.Collapsed; // a template that collapses it: the fallback slot
            HostTabs.Layout(root);
            Assert.Null(ProjectFillShape.CloseButtonLeft(tab));
            Assert.Equal(fade, Shape.FadeFor(tab));
        });
    }

    /// <summary>A tab narrower than the solid part fades from its left edge to the right edge. No negative widths.</summary>
    [Theory]
    [InlineData(60)]
    [InlineData(40)]
    [InlineData(21)]
    [InlineData(12)]
    public void ANarrowTab_FadesFromItsLeftEdge_WithNoNegativeWidths(double width)
    {
        Screenshots.RunSta(() =>
        {
            var (root, selected, unselected) = Strip(UiTheme.Light, width);
            Screenshots.Render(root); // draws without throwing
            foreach (var tab in new[] { selected, unselected })
            {
                var (start, end) = Shape.FadeFor(tab);
                Assert.True(start >= 0 && end >= start, $"{width}: {start}..{end}");
                Assert.Equal(width, end);
                if (width < Shape.TypicalRun) Assert.Equal(0, start);
                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(start, brush.StartPoint.X);
                Assert.Equal(end, brush.EndPoint.X);
            }
            var spec = TabDecoration.FindAdorner(selected)?.Spec;
            if (spec is not null)
            {
                var (fadeStart, lineEnd) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, spec);
                Assert.True(fadeStart >= 0 && lineEnd >= fadeStart);
            }
        });
    }

    // ---- what is behind the tab ---------------------------------------------------------------------

    /// <summary>With strip past the last tab, the rendered sample reads it; the painted search agrees.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheBackdrop_IsSampledFromTheStripPastTheLastTab(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var (_, selected, _) = Strip(theme, 155, room: 40);
            var behind = TabBackdrop.Behind(selected);
            Assert.NotNull(behind);
            Assert.Equal(HostTabs.Well(theme), behind!.Value.Color);
            Assert.Equal("rendered, the strip past the last tab", behind.Value.Source);
            Assert.Equal(HostTabs.Well(theme), TabBackdrop.FromTree(selected)!.Value.Color);
            Assert.Equal(HostTabs.Well(theme), TabBackdrop.Resolve(selected, selected.Background, theme).Color);
        });
    }

    /// <summary>
    /// #36: the rendered sample never reads our fill. Every tab carries an opaque project fill, which
    /// runs to each tab's right edge; one Hidden filled tab is left at a stale position over the strip
    /// past the last tab (Revit does that, vm/2026/diag-2026.txt). The sample point lies off every drawn
    /// tab, right of the last one; the pixel just left of it is our fill, the pixel it reads is the strip.
    /// With no strip past the last tab there is no sample at all (the painted search answers instead).
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheSample_NeverReadsOurFill(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var (root, first, _) = Strip(theme, 155, Orange, titles: ["Tower - A", "Tower - B", "Tower - C"], room: 40);
            var strip = (TabControl)ItemsControl.ItemsControlFromItemContainer(first);
            var pane = (FrameworkElement)VisualTreeHelper.GetParent(first);
            var last = HostTabs.Tab(strip, 2);
            var hidden = HostTabs.Tab(strip, 1);
            var point = TabBackdrop.SamplePoint(pane)!.Value;
            var lastRight = last.TranslatePoint(new Point(last.ActualWidth, 0), pane).X;
            Assert.InRange(point.X, lastRight + 1, lastRight + 4);
            foreach (TabItem tab in strip.Items)
            {
                var bounds = new Rect(tab.TranslatePoint(new Point(0, 0), pane), tab.RenderSize);
                Assert.False(bounds.Contains(point), $"{tab.Header} covers the sample point");
            }

            // A Hidden filled tab moved over the point is not drawn, so it changes nothing.
            hidden.Visibility = Visibility.Hidden;
            var hiddenLeft = hidden.TranslatePoint(new Point(0, 0), pane).X;
            hidden.RenderTransform = new TranslateTransform(point.X - 20 - hiddenLeft, 0);
            HostTabs.Layout(root);
            point = TabBackdrop.SamplePoint(pane)!.Value; // the last drawn tab is still tab 3
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.ColorOf(HostTabs.Well(theme));
            var inRoot = pane.TransformToAncestor(root).Transform(point);
            Assert.Equal(well, Screenshots.PixelAt(bitmap, (int)inRoot.X, (int)inRoot.Y));
            Assert.NotEqual(well, Pixel(bitmap, root, last, last.ActualWidth - 0.5, point.Y)); // our fill, at the edge

            var rendered = TabBackdrop.Rendered(first, out var failure);
            Assert.True(rendered is not null, failure);
            Assert.Equal(HostTabs.Well(theme), rendered!.Value.Color);

            // No strip past the last tab: no sample, never a point on a tab.
            var (_, full, _) = Strip(theme, 155, Orange);
            Assert.Null(TabBackdrop.SamplePoint((FrameworkElement)VisualTreeHelper.GetParent(full)));
            Assert.Null(TabBackdrop.Rendered(full, out failure));
            Assert.Equal("no strip past the last tab", failure);
            Assert.Equal(HostTabs.Well(theme), TabBackdrop.Behind(full)!.Value.Color); // the painted search
        });
    }

    /// <summary>A translucent strip is blended over what is behind it.</summary>
    [Fact]
    public void ATranslucentStrip_IsBlendedOverWhatIsBehindIt()
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Light;
            var strip = HostTabs.Strip(HostTabs.RevitLikeStyle(theme), theme, "Tower - L1");
            strip.Background = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
            var root = HostTabs.Host(strip, theme.Surface(), padding: 12);
            HostTabs.Layout(root);
            var behind = TabBackdrop.Behind(HostTabs.Tab(strip, 0))!.Value;
            Assert.Equal(ColorMath.Blend(new Rgb(255, 0, 0), 128 / 255.0, theme.Surface()), behind.Color);
            Assert.Contains("translucent", behind.Source);
        });
    }

    /// <summary>Nothing solid behind the tab: its own background, else the theme surface.</summary>
    [Fact]
    public void WithNothingBehind_TheTabsOwnBackground_ElseTheSurface()
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Dark;
            var strip = HostTabs.Strip(HostTabs.RevitLikeStyle(theme), theme, "Tower - L1");
            strip.Background = null;
            var root = new System.Windows.Documents.AdornerDecorator { Child = new Grid { Children = { strip } } };
            HostTabs.Layout(root);
            var tab = HostTabs.Tab(strip, 0);
            Assert.Null(TabBackdrop.Behind(tab));
            var surface = TabBackdrop.Resolve(tab, new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)), theme);
            Assert.Equal(theme.Surface(), surface.Color);
            Assert.Contains("theme surface", surface.Source);
            var own = TabBackdrop.Resolve(tab, HostTabs.Brush(Rgb.Parse("#112233")), theme);
            Assert.Equal(Rgb.Parse("#112233"), own.Color);
            Assert.Contains("own background", own.Source);
            Assert.Null(TabBackdrop.Behind(new TabItem())); // not in a tree
        });
    }

    // ---- the 3:1 floor for the title (#31) and the × (#36) ------------------------------------------

    private static readonly int[] Fills = [10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100];

    /// <summary>Steps across the title span (from the fade under its last column to the solid part) the floor checks.</summary>
    private const int TitleSteps = 24;

    /// <summary>
    /// #31 and #36: for every default project color, fill 10 to 100 % (and 85, 95), Unselected tab
    /// opacity 10 to 100 %, and each state (unselected, hover, selected), the factory's text color
    /// reaches 3:1 over the whole title (the solid part to its last column in the fade), and its ×
    /// color reaches 3:1 over the fade under every column of the ×, each blended over what shows
    /// behind the tab, which is also what they are chosen against: the candidate strips (Revit's,
    /// from the VM, and the stand-in's), the theme surface (the fallback) and the settings preview's
    /// tab color. Any case below 3:1 fails the test. Computed with the factory's own blend and
    /// contrast math; the rendered checks below tie it to pixels. The worst cases go to a .txt.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheFloor_Holds_ForTitlesAndTheX_ForEveryDefaultColorFillShareAndState(UiTheme theme)
    {
        var palette = UiPalette.For(theme);
        var previewTab = palette[UiPalette.Hover];
        var backdrops = new List<(string Name, Rgb Color)>
        {
            ("theme surface (the fallback)", theme.Surface()),
            ("stand-in strip", HostTabs.Well(theme)),
            ("settings preview tab", new Rgb(previewTab.R, previewTab.G, previewTab.B)),
            theme == UiTheme.Dark
                ? ("Revit dark tab strip (VM, #33)", Rgb.Parse("#454F61"))
                : ("Revit light tab strip (VM, #33)", Rgb.Parse("#CCCCCC")),
        };
        var end = Shape.TitleEndShare;
        var close = Shape.CloseShares();
        var log = new StringBuilder($"#31/#36 floor, {theme}: text over the whole title, from the solid part to its end at {end:P1} of the opacity ({TitleSteps + 1} points); the × over the fade under its {close.Length} columns ({close[^1]:P1} to {close[0]:P1})\n");
        var failures = new List<string>();
        foreach (var (name, backdrop) in backdrops)
        {
            var worstTitle = (Value: double.MaxValue, What: "");
            var worstX = (Value: double.MaxValue, What: "");
            var count = 0;
            foreach (var color in SplashItDefaults.ProjectColors)
            foreach (var fill in Fills)
            for (var share = 10; share <= 100; share += 10)
            {
                var colors = TabStyleFactory.FillColors(TabTreatment.ProjectFill(color, fill, share), backdrop, Shape);
                foreach (var (state, c, opacity, text, glyph) in new[]
                         {
                             ("unselected", colors.Fill, colors.MutedOpacity, colors.Text, colors.Glyph),
                             ("hover", colors.Hover, colors.MutedOpacity, colors.HoverText, colors.HoverGlyph),
                             ("selected", colors.Selected, colors.Opacity, colors.SelectedText, colors.SelectedGlyph),
                         })
                {
                    count++;
                    var what = $"{color.ToHex()} fill {fill} % unselected {share} % {state}";
                    var title = double.MaxValue;
                    for (var k = 0; k <= TitleSteps; k++)
                        title = Math.Min(title, ColorMath.ContrastRatio(text, ColorMath.Blend(c, opacity * (end + (1 - end) * k / TitleSteps), backdrop)));
                    var x = close.Min(s => ColorMath.ContrastRatio(glyph, ColorMath.Blend(c, opacity * s, backdrop)));
                    if (title < worstTitle.Value) worstTitle = (title, what);
                    if (x < worstX.Value) worstX = (x, what);
                    if (title < 3) failures.Add($"{name}: {what}: title {title:0.00}:1");
                    if (x < 3) failures.Add($"{name}: {what}: × {x:0.00}:1");
                }
            }
            log.AppendLine($"{name} {backdrop.ToHex()}: title worst {worstTitle.Value:0.00}:1 ({worstTitle.What}); × worst {worstX.Value:0.00}:1 ({worstX.What}); {count} cases");
        }
        log.AppendLine($"below 3:1: {failures.Count}");
        System.IO.Directory.CreateDirectory(Screenshots.Folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Screenshots.Folder, $"project-fill-floor-{theme.ToString().ToLowerInvariant()}.txt"), log.ToString());
        Assert.True(failures.Count == 0, $"{failures.Count} below 3:1, e.g. {string.Join("; ", failures.Take(5))}");
    }

    /// <summary>
    /// The analytic floor against rendered pixels: on a long-title tab (it fills its tab, so its last
    /// column sits in the fade), the pixel under the title's last column (below the letters) is the
    /// fill at <see cref="ProjectFillShape.TitleEndShare"/> blended over what is behind the tab, and
    /// the title really ends <see cref="ProjectFillShape.TitleGap"/> px before the ×.
    /// </summary>
    [Theory]
    [InlineData("#E15759", 90, UiTheme.Dark)]
    [InlineData("#9C755F", 90, UiTheme.Dark)]
    [InlineData("#4F8EF7", 100, UiTheme.Dark)]
    [InlineData("#F28E2B", 100, UiTheme.Light)]
    public void TheTitleEnd_RendersAsComputed(string hex, int fill, UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var color = Rgb.Parse(hex);
            const string title = "Tower - Floor Plan: Level 01 - Architectural Coordination Model";
            var (root, tab, _) = Strip(theme, double.NaN, color, titles: [title, "Tower - Sheet: A101"], fill: fill);
            var bitmap = Screenshots.Render(root);
            var text = WindowCapture.Descendants<TextBlock>(tab).Single(t => t.Text == title);
            var textRight = text.TranslatePoint(new Point(text.ActualWidth, 0), tab).X;
            var (start, end) = Shape.FadeFor(tab);
            Assert.Equal(MeasuredCloseLeft(tab) - Shape.TitleGap, textRight, 0); // the stand-in's title gap is Revit's

            var opacity = fill / 100.0;
            var share = ShareAt(textRight - 0.5, start, end);
            Assert.Equal(Shape.TitleEndShare, share, 2);
            var expected = ColorMath.Blend(color, opacity * share, HostTabs.Well(theme));
            var under = Pixel(bitmap, root, tab, textRight - 0.5, 18.5);
            Near(HostTabs.ColorOf(expected), under, 4, $"{hex} {fill} % under the title's last column");

            var chosen = ((SolidColorBrush)tab.Foreground).Color;
            var colors = TabStyleFactory.FillColors(TabTreatment.ProjectFill(color, fill), HostTabs.Well(theme), Shape);
            Assert.Equal(HostTabs.ColorOf(colors.SelectedText), chosen); // chosen against the strip behind the tab
            foreach (var point in new[] { ColorMath.Blend(color, opacity, HostTabs.Well(theme)), expected })
                Assert.True(ColorMath.ContrastRatio(colors.SelectedText, point) >= 3, $"{hex} {fill} %: {ColorMath.ContrastRatio(colors.SelectedText, point):0.00}:1");
        });
    }

    // ---- the × and restore -------------------------------------------------------------------------

    /// <summary>
    /// #36: the × sits on the fade. Per state it is the factory's black or white, chosen over the fade
    /// under its columns, never Revit's grey; and against the rendered pixels under it (the row below
    /// the glyph, across the 16 px slot) it is the better of the two at its worst.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheCloseGlyph_IsChosenOverTheFadeUnderIt(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var (root, selected, unselected) = Strip(theme, 155, Orange);
            var bitmap = Screenshots.Render(root);
            var colors = TabStyleFactory.FillColors(TabTreatment.ProjectFill(Orange, 100), HostTabs.Well(theme), Shape);
            foreach (var (tab, expected) in new[] { (selected, colors.SelectedGlyph), (unselected, colors.Glyph) })
            {
                var button = TabCloseGlyph.FindButton(tab)!;
                var glyph = ((SolidColorBrush)button.Foreground).Color;
                Assert.Equal(HostTabs.ColorOf(expected), glyph);
                Assert.NotEqual(HostTabs.CloseGrey, glyph);
                var left = MeasuredCloseLeft(tab);
                var under = Enumerable.Range(0, 16).Select(i => Pixel(bitmap, root, tab, left + i + 0.5, 18.5)).Select(p => new Rgb(p.R, p.G, p.B)).ToArray();
                Assert.DoesNotContain(HostTabs.Well(theme), under); // the × is on our fill, not the bare strip
                var mine = under.Min(u => ColorMath.ContrastRatio(new Rgb(glyph.R, glyph.G, glyph.B), u));
                var other = under.Min(u => ColorMath.ContrastRatio(glyph == Colors.Black ? ColorMath.White : ColorMath.Black, u));
                Assert.True(mine >= other, $"{theme}: {mine:0.00}:1 vs {other:0.00}:1");
                Assert.True(mine >= 3, $"{theme}: {mine:0.00}:1");
            }
        });
    }

    /// <summary>Restoring the original style removes the fill's binding, the line, and the × color: exactly what was there.</summary>
    [Fact]
    public void Restore_IsExact()
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Dark;
            var original = HostTabs.RevitLikeStyle(theme);
            var strip = HostTabs.Strip(original, theme, "Tower - Floor Plan: L1", "Tower - Sheet: A101");
            var root = HostTabs.Host(strip, theme.Surface(), padding: 12);
            var selected = HostTabs.Tab(strip, 0);
            var other = HostTabs.Tab(strip, 1);
            selected.IsSelected = true;
            HostTabs.Layout(root);
            var nativeSelected = ((SolidColorBrush)selected.Background).Color;
            var nativeOther = ((SolidColorBrush)other.Background).Color;
            var close = TabCloseGlyph.FindButton(selected)!;
            var grey = ((SolidColorBrush)close.Foreground).Color;
            var before = Screenshots.Render(root);

            var factory = new TabStyleFactory();
            foreach (var tab in new[] { selected, other })
                tab.Style = factory.Create(original, TabTreatment.ProjectFill(Blue, 100), tab.Background, theme);
            HostTabs.Layout(root);
            Assert.NotNull(BindingOperations.GetMultiBindingExpression(selected, Control.BackgroundProperty));
            Assert.NotNull(TabDecoration.FindAdorner(selected));
            Assert.NotEqual(grey, ((SolidColorBrush)close.Foreground).Color);

            foreach (var tab in new[] { selected, other }) tab.Style = original;
            HostTabs.Layout(root);
            foreach (var tab in new[] { selected, other })
            {
                Assert.Null(BindingOperations.GetMultiBindingExpression(tab, Control.BackgroundProperty));
                Assert.Equal(DependencyProperty.UnsetValue, tab.ReadLocalValue(Control.BackgroundProperty));
                Assert.Null(TabDecoration.FindAdorner(tab));
            }
            Assert.Equal(nativeSelected, ((SolidColorBrush)selected.Background).Color);
            Assert.Equal(nativeOther, ((SolidColorBrush)other.Background).Color);
            Assert.Equal(DependencyProperty.UnsetValue, close.ReadLocalValue(Control.ForegroundProperty));
            Assert.Equal(grey, ((SolidColorBrush)close.Foreground).Color);

            // Pixel for pixel, the strip is what it was.
            var after = Screenshots.Render(root);
            Assert.Equal(Pixels(before), Pixels(after));
        });
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
