using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// #30: the project fill is solid from the left edge, then fades over a fixed 24 px that ends
/// 2 px before the close button, so the × sits on the strip; the selected tab's top line takes the
/// same shape. Geometry, the hover and narrow-tab edge cases, the text and × contrast, and restore.
/// </summary>
public class ProjectFillShapeTests
{
    private static readonly Rgb Blue = SplashItDefaults.ProjectColors[0];
    private static readonly ProjectFillShape Shape = ProjectFillShape.Default;

    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    [Fact]
    public void Default_IsA24PxFade_Ending2PxBeforeThe18PxCloseSlot()
    {
        Assert.Equal(new ProjectFillShape(24, 2, 18), ProjectFillShape.Default);
        Assert.Equal(ProjectFillShape.Default, new TabStyleFactory().ProjectFill);
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

    /// <summary>Two Revit-like tabs of one project, <paramref name="width"/> px wide each, the first selected, laid out.</summary>
    private static (FrameworkElement Root, TabItem Selected, TabItem Unselected, Style Original) Strip(UiTheme theme, double width, params string[] titles)
    {
        var original = HostTabs.RevitLikeStyle(theme);
        var strip = HostTabs.Strip(original, theme, titles.Length > 0 ? titles : ["Tower - Floor Plan: L1", "Tower - Sheet: A101"]);
        var factory = new TabStyleFactory();
        foreach (TabItem tab in strip.Items)
        {
            if (!double.IsNaN(width)) tab.Width = width;
            tab.Style = factory.Create(original, TabTreatment.ProjectFill(Blue, 100), tab.Background, theme);
        }
        HostTabs.Tab(strip, 0).IsSelected = true;
        var root = HostTabs.Host(strip, theme.Surface(), padding: 12);
        HostTabs.Layout(root);
        return (root, HostTabs.Tab(strip, 0), HostTabs.Tab(strip, 1), original);
    }

    private static double CloseLeft(TabItem tab)
    {
        var button = TabCloseGlyph.FindButton(tab)!;
        return button.TransformToAncestor(tab).Transform(new Point(0, 0)).X;
    }

    private static Color Pixel(System.Windows.Media.Imaging.BitmapSource bitmap, FrameworkElement root, FrameworkElement tab, double x, double y)
    {
        var p = tab.TransformToAncestor(root).Transform(new Point(x, y));
        return Screenshots.PixelAt(bitmap, (int)p.X, (int)p.Y);
    }

    private static void Near(Color expected, Color actual, int tolerance, string what) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
                    && Math.Abs(expected.B - actual.B) <= tolerance, $"{what}: expected {expected}, got {actual}");

    /// <summary>
    /// At 101, 155, 250 and 350 px the fill and the selected tab's line end at the anchor (2 px before
    /// the button), and the fade is 24 px wide on every tab: drawn brushes, the adorner's extent and pixels.
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
            var (root, selected, unselected, _) = Strip(theme, width);
            var bitmap = Screenshots.Render(root);
            var well = HostTabs.ColorOf(HostTabs.Well(theme));
            foreach (var tab in new[] { selected, unselected })
            {
                Assert.Equal(width, tab.ActualWidth);
                var closeLeft = CloseLeft(tab);
                Assert.Equal(width - Shape.CloseSlot, closeLeft); // the stand-in's slot is Revit's
                var anchor = closeLeft - Shape.Gap;
                Assert.Equal(anchor, Shape.AnchorFor(tab));

                var brush = Assert.IsType<LinearGradientBrush>(tab.Background);
                Assert.Equal(BrushMappingMode.Absolute, brush.MappingMode);
                Assert.Equal(GradientSpreadMethod.Pad, brush.SpreadMethod);
                Assert.Equal(anchor, brush.EndPoint.X);
                Assert.Equal(Shape.FadeWidth, brush.EndPoint.X - brush.StartPoint.X);
                Assert.Equal(0, brush.GradientStops[1].Color.A);

                // Pixels at y 10: solid before the fade, about half way at its middle, the strip from its end.
                var opacity = tab.IsSelected ? 1.0 : 0.5;
                var solid = HostTabs.ColorOf(ColorMath.Blend(Blue, opacity, HostTabs.Well(theme)));
                Near(solid, Pixel(bitmap, root, tab, anchor - Shape.FadeWidth - 1.5, 10), 3, $"{width} solid");
                Near(HostTabs.ColorOf(ColorMath.Blend(Blue, opacity / 2, HostTabs.Well(theme))),
                    Pixel(bitmap, root, tab, anchor - Shape.FadeWidth / 2, 18.5), 8, $"{width} mid-fade");
                Near(well, Pixel(bitmap, root, tab, anchor + 0.5, 10), 3, $"{width} past the fade");
                Near(well, Pixel(bitmap, root, tab, width - 2.5, 18.5), 3, $"{width} under the ×");
            }

            // The selected tab's line: the same shape and end (#30).
            var adorner = TabDecoration.FindAdorner(selected)!;
            var (fadeStart, end) = TabDecorationAdorner.TopAccentExtent(selected, selected.RenderSize, adorner.Spec);
            var lineAnchor = CloseLeft(selected) - Shape.Gap;
            Assert.Equal(lineAnchor, end);
            Assert.Equal(Shape.FadeWidth, end - fadeStart);
            var accent = HostTabs.ColorOf(ColorMath.Darken(Blue, TabStyleFactory.AccentDarken));
            Near(accent, Pixel(bitmap, root, selected, fadeStart - 1.5, 1.5), 3, $"{width} line before its fade");
            Near(well, Pixel(bitmap, root, selected, end + 0.5, 1.5), 3, $"{width} line ends at the anchor");
            Assert.Null(TabDecoration.FindAdorner(unselected));
        });
    }

    /// <summary>
    /// Revit shows the × only on the selected or hovered tab, but keeps its slot (Hidden): showing it
    /// moves nothing. A collapsed button (no slot) falls back to the 18 px slot: the same anchor.
    /// </summary>
    [Fact]
    public void TheFade_DoesNotMove_WhenTheCloseButtonAppears()
    {
        Screenshots.RunSta(() =>
        {
            var (root, _, tab, _) = Strip(UiTheme.Light, 155);
            var button = TabCloseGlyph.FindButton(tab)!;
            Assert.Equal(Visibility.Hidden, button.Visibility); // unselected: hidden, as in Revit
            var before = (LinearGradientBrush)tab.Background;
            var anchor = Shape.AnchorFor(tab);

            button.Visibility = Visibility.Visible; // what hover does
            HostTabs.Layout(root);
            Assert.Equal(anchor, Shape.AnchorFor(tab));
            var after = (LinearGradientBrush)tab.Background;
            Assert.Equal(before.StartPoint, after.StartPoint);
            Assert.Equal(before.EndPoint, after.EndPoint);

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
            var (root, selected, unselected, _) = Strip(UiTheme.Light, width);
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

    /// <summary>
    /// The text keeps the solid part's black or white, and the fade is anchored to the ×, so where it
    /// meets the title's end does not depend on the tab's width: a 350 px tab with a long view name
    /// has the same fill under its last letter as a short one (the share-of-width shape, P1, did not).
    /// Contrast is measured at the title's last column, on both themes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheText_OnALong350PxTab_IsOnTheSameFillAsOnAShortTab(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            const string longTitle = "Tower - Floor Plan: Level 01 - Architectural Coordination Model";
            var (_, shortTab, _, _) = Strip(theme, double.NaN, "Tower - Floor Plan: L1", "Tower - Sheet: A101");
            var (_, longTab, _, _) = Strip(theme, double.NaN, longTitle, "Tower - Sheet: A101");
            Assert.InRange(longTab.ActualWidth, 330, 400);

            var shares = new List<double>();
            var log = new System.Text.StringBuilder("#30: the selected blue tab at fill 100 %, its title's contrast\n");
            foreach (var tab in new[] { shortTab, longTab })
            {
                var text = WindowCapture.Descendants<TextBlock>(tab).Single(t => t.Text == (string)tab.Header);
                var textRight = text.TransformToAncestor(tab).Transform(new Point(text.ActualWidth, 0)).X;
                var (start, end) = Shape.FadeFor(Shape.AnchorFor(tab));
                var share = Math.Clamp((end - textRight) / (end - start), 0, 1);
                shares.Add(share);

                // The text's color is the solid fill's choice; record the contrast at its last column.
                var under = ColorMath.Blend(Blue, share, HostTabs.Well(theme));
                var textColor = ((SolidColorBrush)tab.Foreground).Color;
                var chosen = new Rgb(textColor.R, textColor.G, textColor.B);
                Assert.Equal(ColorMath.TextColorFor(Blue, 1, theme.Surface()), chosen);
                var atEnd = ColorMath.ContrastRatio(chosen, under);
                var onSolid = ColorMath.ContrastRatio(chosen, ColorMath.Blend(Blue, 1, HostTabs.Well(theme)));
                log.AppendLine($"{theme}\t{tab.ActualWidth} px\ttext {(chosen == ColorMath.Black ? "black" : "white")}\ton the solid fill {onSolid:0.00}:1\t"
                               + $"at the title's last column {atEnd:0.00}:1 (fill {share:P0} there)");
                Assert.True(atEnd >= 3, $"{theme} {tab.ActualWidth} px: {atEnd:0.00}:1 at the title's end");
            }
            System.IO.Directory.CreateDirectory(Screenshots.Folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Screenshots.Folder, $"project-fill-text-contrast-{theme.ToString().ToLowerInvariant()}.txt"), log.ToString());
            Assert.Equal(shares[0], shares[1], 2); // width-independent
        });
    }

    /// <summary>The × sits on the strip in every state: black on the light strip, white on the dark one, never Revit's grey.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheCloseGlyph_IsChosenAgainstTheStripUnderIt(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var (root, selected, unselected, _) = Strip(theme, 155);
            var bitmap = Screenshots.Render(root);
            foreach (var tab in new[] { selected, unselected })
            {
                var button = TabCloseGlyph.FindButton(tab)!;
                var glyph = ((SolidColorBrush)button.Foreground).Color;
                Assert.Equal(HostTabs.ColorOf(ColorMath.TextColorFor(theme.Surface())), glyph);
                var centre = CloseLeft(tab) + button.ActualWidth / 2;
                var under = Pixel(bitmap, root, tab, centre, 18.5);
                Near(HostTabs.ColorOf(HostTabs.Well(theme)), under, 3, "the strip under the ×");
                var underRgb = new Rgb(under.R, under.G, under.B);
                var mine = ColorMath.ContrastRatio(new Rgb(glyph.R, glyph.G, glyph.B), underRgb);
                var other = ColorMath.ContrastRatio(glyph == Colors.Black ? ColorMath.White : ColorMath.Black, underRgb);
                Assert.True(mine > other, $"{theme}: {mine:0.00}:1 vs {other:0.00}:1");
            }
            // Dark theme: the selected blue tab's text is black, its × white.
            if (theme == UiTheme.Dark)
                Assert.NotEqual(((SolidColorBrush)selected.Foreground).Color, ((SolidColorBrush)TabCloseGlyph.FindButton(selected)!.Foreground).Color);
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

    private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
