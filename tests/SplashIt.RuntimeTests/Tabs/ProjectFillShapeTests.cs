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
/// #30 and #31: the project fill is solid from the left edge, then fades over a fixed 24 px that
/// ends 2 px before the close button's measured slot, so the × sits on the strip; the selected tab's
/// top line takes the same shape. Geometry (including a button that is not where Revit's is), the
/// hover and narrow-tab edge cases, the 3:1 floor at a title's end, the × color, and restore.
/// </summary>
public class ProjectFillShapeTests
{
    private static readonly Rgb Blue = SplashItDefaults.ProjectColors[0];
    private static readonly Rgb Orange = SplashItDefaults.ProjectColors[1];
    private static readonly ProjectFillShape Shape = ProjectFillShape.Default;

    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    [Fact]
    public void Default_IsA24PxFade_Ending2PxBeforeThe18PxCloseSlot_12PxAfterTheTitle()
    {
        Assert.Equal(new ProjectFillShape(24, 2, 18, 12), ProjectFillShape.Default);
        Assert.Equal(ProjectFillShape.Default, new TabStyleFactory().ProjectFill);
        Assert.Equal(10.5 / 24, ProjectFillShape.Default.TitleEndShare, 10); // a title's last column: about 44 %
        Assert.Equal(1, new ProjectFillShape(0, 2, 18).TitleEndShare);       // no fade: the title is on the solid part
        Assert.Equal(1, new ProjectFillShape(8, 2, 18).TitleEndShare);       // the fade starts after the title
    }

    /// <summary>The anchor is the gap before the button's left edge; with no button slot, the 18 px slot from the right edge.</summary>
    [Theory]
    [InlineData(155, 137.0, 135)]
    [InlineData(155, null, 135)]     // no slot: 155 - 18 - 2
    [InlineData(350, 332.0, 330)]
    [InlineData(101, 90.0, 88)]      // a button somewhere else: its slot wins
    [InlineData(10, null, 0)]        // narrower than the slot: no fill, never negative
    [InlineData(40, -5.0, 0)]
    [InlineData(20, 30.0, 20)]       // never past the tab
    [InlineData(0, null, 0)]
    public void Anchor_IsTheGapBeforeTheCloseButton(double width, double? closeLeft, double expected) =>
        Assert.Equal(expected, Shape.AnchorFor(width, closeLeft));

    /// <summary>The fade is 24 px before the anchor; on a tab too narrow for that, it fades from the left edge.</summary>
    [Theory]
    [InlineData(135, 111, 135)]
    [InlineData(24, 0, 24)]
    [InlineData(10, 0, 10)]          // narrow: a shorter fade from the start
    [InlineData(0, 0, 0)]
    [InlineData(-5, 0, 0)]           // no negative widths
    public void Fade_Is24PxWide_OrFromTheLeftEdgeOnANarrowTab(double anchor, double start, double end)
    {
        var fade = Shape.FadeFor(anchor);
        Assert.Equal(start, fade.Start);
        Assert.Equal(end, fade.End);
        Assert.True(fade.End >= fade.Start && fade.Start >= 0);
    }

    // ---- live tabs ---------------------------------------------------------------------------------

    /// <summary>A strip of project tabs on <paramref name="original"/> (default: the Revit-like style), the first selected, laid out.</summary>
    private static (FrameworkElement Root, TabItem Selected, TabItem Unselected) Strip(UiTheme theme, double width,
        Rgb? color = null, Style? original = null, string[]? titles = null, int fill = 100)
    {
        original ??= HostTabs.RevitLikeStyle(theme);
        var strip = HostTabs.Strip(original, theme, titles ?? ["Tower - Floor Plan: L1", "Tower - Sheet: A101"]);
        var factory = new TabStyleFactory();
        foreach (TabItem tab in strip.Items)
        {
            if (!double.IsNaN(width)) tab.Width = width;
            tab.Style = factory.Create(original, TabTreatment.ProjectFill(color ?? Blue, fill), tab.Background, theme);
        }
        HostTabs.Tab(strip, 0).IsSelected = true;
        var root = HostTabs.Host(strip, theme.Surface(), padding: 12);
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

    /// <summary>
    /// At 101, 155, 250 and 350 px the fill and the selected tab's line end 2 px before the measured
    /// button, and the fade is 24 px wide on every tab: drawn brushes, the adorner's extent and pixels.
    /// </summary>
    [Theory]
    [InlineData(101)]
    [InlineData(155)]
    [InlineData(250)]
    [InlineData(350)]
    public void TheFade_EndsAtTheAnchor_AndIs24PxWide_AtEveryWidth(double width)
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Light;
            var (root, selected, unselected) = Strip(theme, width);
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.ColorOf(HostTabs.Well(theme));
            foreach (var tab in new[] { selected, unselected })
            {
                Assert.Equal(width, tab.ActualWidth);
                var anchor = MeasuredCloseLeft(tab) - Shape.Gap;
                Assert.Equal(anchor, Shape.AnchorFor(tab));

                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(BrushMappingMode.Absolute, brush.MappingMode);
                Assert.Equal(GradientSpreadMethod.Pad, brush.SpreadMethod);
                Assert.Equal(anchor, brush.EndPoint.X);
                Assert.Equal(Shape.FadeWidth, brush.EndPoint.X - brush.StartPoint.X);
                Assert.Equal(0, brush.GradientStops[1].Color.A);

                // Pixels: solid before the fade, about half way at its middle, the strip from its end.
                var opacity = tab.IsSelected ? 1.0 : 0.5;
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity, HostTabs.Well(theme))),
                    Pixel(bitmap, root, tab, anchor - Shape.FadeWidth - 1.5, 10), 3, $"{width} solid");
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity / 2, HostTabs.Well(theme))),
                    Pixel(bitmap, root, tab, anchor - Shape.FadeWidth / 2, 18.5), 8, $"{width} mid-fade");
                Near(well, Pixel(bitmap, root, tab, anchor + 0.5, 10), 3, $"{width} past the fade");
            }

            // The selected tab's line: the same shape and end (#30).
            var adorner = TabDecoration.FindAdorner(selected)!;
            var (fadeStart, end) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, adorner.Spec);
            Assert.Equal(MeasuredCloseLeft(selected) - Shape.Gap, end);
            Assert.Equal(Shape.FadeWidth, end - fadeStart);
            var accent = HostTabs.ColorOf(ColorMath.Darken(Blue, TabStyleFactory.AccentDarken));
            Near(accent, Pixel(bitmap, root, selected, fadeStart - 1.5, 1.5), 3, $"{width} line before its fade");
            Near(well, Pixel(bitmap, root, selected, end + 0.5, 1.5), 3, $"{width} line ends at the anchor");
            Assert.Null(TabDecoration.FindAdorner(unselected));
        });
    }

    /// <summary>
    /// Review M2: a template whose close button is NOT at width - 18 (24 px wide, 7 px from the
    /// edge, so its left edge is at width - 32). The fill and the line end 2 px before the measured
    /// button, not where the right-edge fallback would put them.
    /// </summary>
    [Theory]
    [InlineData(155)]
    [InlineData(250)]
    public void TheAnchor_IsMeasuredFromTheButton_NotTheRightEdge(double width)
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Dark;
            var original = HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 7);
            var (root, selected, unselected) = Strip(theme, width, original: original);
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.ColorOf(HostTabs.Well(theme));
            var fallback = width - Shape.CloseSlot - Shape.Gap;
            foreach (var tab in new[] { selected, unselected })
            {
                var measured = MeasuredCloseLeft(tab);
                Assert.Equal(width - 32, measured);
                Assert.Equal(measured, ProjectFillShape.CloseButtonLeft(tab));
                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(measured - Shape.Gap, brush.EndPoint.X);
                Assert.NotEqual(fallback, brush.EndPoint.X);
                Assert.Equal(Shape.FadeWidth, brush.EndPoint.X - brush.StartPoint.X);
                // Between the measured anchor and the fallback the strip shows, not the fill.
                Near(well, Pixel(bitmap, root, tab, measured - Shape.Gap + 0.5, 18.5), 3, "past the measured anchor");
                Near(well, Pixel(bitmap, root, tab, fallback - 1.5, 18.5), 3, "where a right-edge fade would still be drawing");
            }
            var spec = TabDecoration.FindAdorner(selected)!.Spec;
            var (_, end) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, spec);
            Assert.Equal(MeasuredCloseLeft(selected) - Shape.Gap, end);
        });
    }

    /// <summary>The right-edge fallback is used only when the tab has no close button to measure.</summary>
    [Fact]
    public void TheFallback_IsUsedOnlyWhenThereIsNoButton()
    {
        Screenshots.RunSta(() =>
        {
            var theme = UiTheme.Light;
            var noButton = HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 7, closeName: "SomethingElse");
            var (_, selected, unselected) = Strip(theme, 155, original: noButton);
            foreach (var tab in new[] { selected, unselected })
            {
                Assert.Null(TabCloseGlyph.FindButton(tab));
                Assert.Null(ProjectFillShape.CloseButtonLeft(tab));
                Assert.Equal(155 - Shape.CloseSlot - Shape.Gap, ((LinearGradientBrush)tab.Background).EndPoint.X);
            }

            var (_, other, _) = Strip(theme, 155, original: HostTabs.RevitLikeStyle(theme, closeWidth: 24, closeRightInset: 7));
            Assert.NotNull(ProjectFillShape.CloseButtonLeft(other));
            Assert.NotEqual(155 - Shape.CloseSlot, ProjectFillShape.CloseButtonLeft(other));
        });
    }

    /// <summary>
    /// Revit shows the × only on the selected or hovered tab, but keeps its slot (Hidden). On one
    /// unselected tab: show the × (what hover does), re-evaluate the fill and render; the fill's
    /// pixels and the anchor are the same as before. A collapsed button falls back to the 18 px slot,
    /// which on Revit's geometry is the same anchor.
    /// </summary>
    [Fact]
    public void TheFade_DoesNotMove_WhenTheCloseButtonAppears()
    {
        Screenshots.RunSta(() =>
        {
            var (root, _, tab) = Strip(UiTheme.Light, 155);
            var button = TabCloseGlyph.FindButton(tab)!;
            Assert.Equal(Visibility.Hidden, button.Visibility); // unselected: hidden, as in Revit
            var anchor = Shape.AnchorFor(tab);
            var before = Row(Screenshots.Render(root), root, tab, 18.5);

            foreach (var visibility in new[] { Visibility.Visible, Visibility.Hidden, Visibility.Visible })
            {
                button.Visibility = visibility;
                HostTabs.Layout(root);
                BindingOperations.GetMultiBindingExpression(tab, Control.BackgroundProperty)!.UpdateTarget(); // re-measure now
                HostTabs.Layout(root);
                Assert.Equal(anchor, Shape.AnchorFor(tab));
                Assert.Equal(anchor, ((LinearGradientBrush)tab.Background).EndPoint.X);
                Assert.Equal(before, Row(Screenshots.Render(root), root, tab, 18.5)); // the row under the ×, pixel for pixel
            }

            button.Visibility = Visibility.Collapsed; // a template that collapses it: the fallback slot
            HostTabs.Layout(root);
            Assert.Null(ProjectFillShape.CloseButtonLeft(tab));
            Assert.Equal(anchor, Shape.AnchorFor(tab));
        });
    }

    /// <summary>A tab narrower than the fade fades from its left edge; one with no room has no fill. No negative widths.</summary>
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
                var anchor = Shape.AnchorFor(tab);
                Assert.InRange(anchor, 0, width);
                var (start, end) = Shape.FadeFor(anchor);
                Assert.True(start >= 0 && end >= start, $"{width}: {start}..{end}");
                if (end <= 0)
                    Assert.Equal(0, Assert.IsType<SolidColorBrush>(tab.Background).Color.A); // no room: no fill
                else
                {
                    var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                    Assert.Equal(start, brush.StartPoint.X);
                    Assert.Equal(end, brush.EndPoint.X);
                    if (end < Shape.FadeWidth) Assert.Equal(0, start); // the fade starts at the left edge
                }
            }
            var spec = TabDecoration.FindAdorner(selected)?.Spec;
            if (spec is not null)
            {
                var (fadeStart, lineEnd) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, spec);
                Assert.True(fadeStart >= 0 && lineEnd >= fadeStart);
            }
        });
    }

    // ---- the 3:1 floor at a title's end (#31) ------------------------------------------------------

    private static readonly int[] Fills = [10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100];

    /// <summary>
    /// #31, review M1: for every default project color, fill 10 to 100 % (and 85, 95), Unselected tab
    /// opacity 10 to 100 %, and each state (unselected, hover, selected), the factory's text color
    /// reaches 3:1 both on the solid part and at a title's last column in the fade, each blended over
    /// the backdrop the factory is given: the theme surface (Revit's tabs have a transparent background)
    /// and the settings preview's tab color. Computed with the same blend and contrast math as the
    /// factory; a handful of cases are checked against rendered pixels below. The worst cases, and
    /// the same choices measured over the stand-in's strip and Revit's dark tab area, go to a .txt.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheTitleFloor_Holds_ForEveryDefaultColorFillShareAndState(UiTheme theme)
    {
        var palette = UiPalette.For(theme);
        var previewTab = palette[UiPalette.Hover];
        var backdrops = new (string Name, Rgb Color, bool Asserted)[]
        {
            ("theme surface (the factory's backdrop for Revit's transparent tabs)", theme.Surface(), true),
            ("settings preview tab", new Rgb(previewTab.R, previewTab.G, previewTab.B), true),
            ("stand-in strip (what renders under the stand-in's fade)", HostTabs.Well(theme), false),
        };
        if (theme == UiTheme.Dark)
            backdrops = [.. backdrops, ("Revit dark tab area (Lane B capture; AC11 to confirm)", Rgb.Parse("#454F61"), false)];
        var end = Shape.TitleEndShare;
        var log = new StringBuilder($"#31 title floor, {theme}: text chosen over the backdrop; min contrast over (solid, title end at {end:P1} of the opacity)\n");
        var failures = new List<string>();
        foreach (var (name, backdrop, asserted) in backdrops)
        {
            var chooseOver = asserted ? backdrop : theme.Surface(); // unasserted rows: the factory still chooses over the surface
            var worst = (Value: double.MaxValue, What: "");
            var below = 0;
            var count = 0;
            foreach (var color in SplashItDefaults.ProjectColors)
            foreach (var fill in Fills)
            for (var share = 10; share <= 100; share += 10)
            {
                var colors = TabStyleFactory.FillColors(TabTreatment.ProjectFill(color, fill, share), chooseOver, Shape);
                foreach (var (state, c, opacity, text) in new[]
                         {
                             ("unselected", colors.Fill, colors.MutedOpacity, colors.Text),
                             ("hover", colors.Hover, colors.MutedOpacity, colors.HoverText),
                             ("selected", colors.Selected, colors.Opacity, colors.SelectedText),
                         })
                {
                    count++;
                    var min = Math.Min(ColorMath.ContrastRatio(text, ColorMath.Blend(c, opacity, backdrop)),
                        ColorMath.ContrastRatio(text, ColorMath.Blend(c, opacity * end, backdrop)));
                    if (min < worst.Value) worst = (min, $"{color.ToHex()} fill {fill} % unselected {share} % {state}");
                    if (min < 3)
                    {
                        below++;
                        if (asserted) failures.Add($"{name}: {color.ToHex()} fill {fill} % unselected {share} % {state}: {min:0.00}:1");
                    }
                }
            }
            log.AppendLine($"{name} {backdrop.ToHex()}: worst {worst.Value:0.00}:1 ({worst.What}); below 3:1: {below} of {count}{(asserted ? "" : " (not asserted)")}");
        }
        System.IO.Directory.CreateDirectory(Screenshots.Folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Screenshots.Folder, $"project-fill-title-floor-{theme.ToString().ToLowerInvariant()}.txt"), log.ToString());
        Assert.True(failures.Count == 0, $"{failures.Count} below 3:1, e.g. {string.Join("; ", failures.Take(5))}");
    }

    /// <summary>
    /// The analytic floor against rendered pixels: on a long-title tab (it fills its tab, so its last
    /// column sits in the fade), the pixel under the title's last column (below the letters) is the
    /// fill at <see cref="ProjectFillShape.TitleEndShare"/> blended over what is behind the tab, and
    /// the title really ends <see cref="ProjectFillShape.TitleGap"/> px before the ×. Includes the
    /// review's failing cases (red at 85 and 90 %, brown at 90 %, dark).
    /// </summary>
    [Theory]
    [InlineData("#E15759", 90, UiTheme.Dark)]
    [InlineData("#E15759", 85, UiTheme.Dark)]
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
            var anchor = Shape.AnchorFor(tab);
            Assert.Equal(MeasuredCloseLeft(tab) - Shape.TitleGap, textRight, 0); // the stand-in's title gap is Revit's

            var opacity = fill / 100.0;
            var share = (anchor - (textRight - 0.5)) / Shape.FadeWidth;
            Assert.Equal(Shape.TitleEndShare, share, 2);
            var expected = ColorMath.Blend(color, opacity * share, HostTabs.Well(theme));
            var under = Pixel(bitmap, root, tab, textRight - 0.5, 18.5);
            Near(HostTabs.ColorOf(expected), under, 4, $"{hex} {fill} % under the title's last column");

            var chosen = ((SolidColorBrush)tab.Foreground).Color;
            var colors = TabStyleFactory.FillColors(TabTreatment.ProjectFill(color, fill), theme.Surface(), Shape);
            Assert.Equal(HostTabs.ColorOf(colors.SelectedText), chosen);
        });
    }

    // ---- the × and restore -------------------------------------------------------------------------

    /// <summary>The × sits on the strip in every state: black on the light strip, white on the dark one, never Revit's grey.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheCloseGlyph_IsChosenAgainstTheStripUnderIt(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var (root, selected, unselected) = Strip(theme, 155, Orange);
            var bitmap = Screenshots.Render(root);
            foreach (var tab in new[] { selected, unselected })
            {
                var button = TabCloseGlyph.FindButton(tab)!;
                var glyph = ((SolidColorBrush)button.Foreground).Color;
                Assert.Equal(HostTabs.ColorOf(ColorMath.TextColorFor(theme.Surface())), glyph);
                var centre = MeasuredCloseLeft(tab) + button.ActualWidth / 2;
                var under = Pixel(bitmap, root, tab, centre, 18.5);
                Near(HostTabs.ColorOf(HostTabs.Well(theme)), under, 3, "the strip under the ×");
                var underRgb = new Rgb(under.R, under.G, under.B);
                var mine = ColorMath.ContrastRatio(new Rgb(glyph.R, glyph.G, glyph.B), underRgb);
                var other = ColorMath.ContrastRatio(glyph == Colors.Black ? ColorMath.White : ColorMath.Black, underRgb);
                Assert.True(mine > other, $"{theme}: {mine:0.00}:1 vs {other:0.00}:1");
            }
            // Dark theme: the selected orange tab's text is black, its × white.
            if (theme == UiTheme.Dark)
            {
                Assert.Equal(Colors.Black, ((SolidColorBrush)selected.Foreground).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)TabCloseGlyph.FindButton(selected)!.Foreground).Color);
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
