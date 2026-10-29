using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;
using SplashIt.Core.Theming;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// Screenshots of tab strips styled by the real <see cref="TabStyleFactory"/> (the FnB
/// approves the selected-tab look from them), on the light (#FFFFFF) and dark (#2A2F37)
/// surfaces. The tabs use <see cref="HostTabs.RevitLikeStyle"/>, a stand-in modeled on
/// Revit's own TabItem template as read on the VM; the <c>tabs-aero2-*</c> pair uses WPF's
/// stock template instead, to show how a template that paints its own selected background
/// limits the fill. Each test also checks a few pixels.
/// </summary>
public class TabStripScreenshotTests
{
    private static readonly Rgb P1 = SplashItDefaults.ProjectColors[0];
    private static readonly Rgb P2 = SplashItDefaults.ProjectColors[1];
    private static readonly Rgb F1 = SplashItDefaults.FamilyColors[0];

    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    private static string Suffix(UiTheme theme) => theme == UiTheme.Dark ? "dark" : "light";

    private static TextBlock Label(string text, UiTheme theme) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12,
        Margin = new Thickness(0, 10, 0, 4),
        Foreground = HostTabs.Brush(ColorMath.TextColorFor(theme.Surface())),
    };

    /// <summary>One strip of tabs, each colored by a treatment (null = native), with the given tab selected.</summary>
    private static TabControl Styled(TabStyleFactory factory, Style? original, UiTheme theme, int selected,
        params (string Title, TabTreatment? Treatment)[] tabs)
    {
        var strip = HostTabs.Strip(original, theme, tabs.Select(t => t.Title).ToArray());
        for (var i = 0; i < tabs.Length; i++)
        {
            var tab = HostTabs.Tab(strip, i);
            if (tabs[i].Treatment is { } treatment)
                tab.Style = factory.Create(original, treatment, tab.Background, theme);
        }
        HostTabs.Tab(strip, selected).IsSelected = true;
        return strip;
    }

    private static (BitmapSource Bitmap, FrameworkElement Root) Shoot(string name, UiTheme theme, params UIElement[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        var root = HostTabs.Host(stack, theme.Surface(), padding: 12);
        HostTabs.Layout(root);
        var bitmap = Screenshots.Render(root);
        Screenshots.Save(bitmap, name + "-" + Suffix(theme));
        Assert.True(Screenshots.DistinctPixels(bitmap) > 20, "the render looks blank");
        return (bitmap, root);
    }

    /// <summary>The rendered pixel at a point inside <paramref name="element"/> (relative to its top-left).</summary>
    private static Color PixelIn(BitmapSource bitmap, FrameworkElement root, FrameworkElement element, double x, double y)
    {
        var p = element.TransformToAncestor(root).Transform(new Point(x, y));
        return Screenshots.PixelAt(bitmap, (int)p.X, (int)p.Y);
    }

    private static Color Shown(Rgb fill, double opacity, Rgb backdrop) => HostTabs.ColorOf(ColorMath.Blend(fill, opacity, backdrop));

    private static void Near(Color expected, Color actual, int tolerance = 3)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
                    && Math.Abs(expected.B - actual.B) <= tolerance, $"expected {expected}, got {actual}");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TwoProjects_OneSelected(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var strip = Styled(factory, original, theme, selected: 1,
                ("Project1 - Floor Plan: L1", TabTreatment.ProjectFill(P1, 100)),
                ("Project1 - Sheet: A101", TabTreatment.ProjectFill(P1, 100)),
                ("Project2 - Floor Plan: L1", TabTreatment.ProjectFill(P2, 100)),
                ("Project2 - 3D View: {3D}", TabTreatment.ProjectFill(P2, 100)));
            var (bitmap, root) = Shoot("tabs-two-projects", theme,
                Label("Two projects (colors 1 and 2), the 2nd tab selected: the fill with a 3 px darker top line; the others at 50 % (#23)", theme), strip);

            var well = HostTabs.Well(theme);
            var unselected = HostTabs.Tab(strip, 0);
            var selected = HostTabs.Tab(strip, 1);
            Near(Shown(P1, 0.5, well), PixelIn(bitmap, root, unselected, 3, 10));
            Near(Shown(P2, 0.5, well), PixelIn(bitmap, root, HostTabs.Tab(strip, 2), 3, 10));
            Near(HostTabs.ColorOf(ColorMath.Darken(P1, TabStyleFactory.AccentDarken)), PixelIn(bitmap, root, selected, 20, 0.5));
            Near(HostTabs.ColorOf(ColorMath.Darken(P1, TabStyleFactory.AccentDarken)), PixelIn(bitmap, root, selected, 20, 2.5)); // 3 px
            Near(HostTabs.ColorOf(P1), PixelIn(bitmap, root, selected, 20, 10));
            // The close glyph follows the text (review M1), on every colored tab.
            foreach (var i in new[] { 0, 1, 2, 3 })
                Assert.Equal(((SolidColorBrush)HostTabs.Tab(strip, i).Foreground).Color,
                    ((SolidColorBrush)TabCloseGlyph.FindButton(HostTabs.Tab(strip, i))!.Foreground).Color);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void OneFamily_AtBarWidths1_3_6(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var rows = new List<UIElement>();
            var strips = new Dictionary<int, TabControl>();
            foreach (var width in new[] { 1, 3, 6 })
            {
                var strip = Styled(factory, original, theme, selected: 1,
                    ("Family1 - Floor Plan: Ref. Level", TabTreatment.FamilyBar(F1, width, 100)),
                    ("Family1 - Elevation: Front", TabTreatment.FamilyBar(F1, width, 100)),
                    ("Project1 - Floor Plan: L1", TabTreatment.ProjectFill(P1, 100)));
                strips[width] = strip;
                rows.Add(Label($"Family bar {width} px at 100 % (second tab selected; selection stays native)", theme));
                rows.Add(strip);
            }
            var (bitmap, root) = Shoot("tabs-family-bars", theme, rows.ToArray());

            foreach (var (width, strip) in strips)
            {
                var tab = HostTabs.Tab(strip, 0);
                Near(HostTabs.ColorOf(F1), PixelIn(bitmap, root, tab, 0.5, 10));
                Near(HostTabs.ColorOf(F1), PixelIn(bitmap, root, tab, width - 0.5, 10));
                Assert.NotEqual(HostTabs.ColorOf(F1), PixelIn(bitmap, root, tab, width + 1.5, 10));
                Near(HostTabs.ColorOf(F1), PixelIn(bitmap, root, tab, 0.5, 0.5));   // full height: top...
                Near(HostTabs.ColorOf(F1), PixelIn(bitmap, root, tab, 0.5, 19.5));  // ...and bottom
            }
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ProjectFill_At30And100Percent(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var rows = new List<UIElement>();
            var strips = new Dictionary<int, TabControl>();
            foreach (var opacity in new[] { 30, 100 })
            {
                var strip = Styled(factory, original, theme, selected: 2,
                    ("Project1 - Floor Plan: L1", TabTreatment.ProjectFill(P1, opacity)),
                    ("Project2 - Floor Plan: L1", TabTreatment.ProjectFill(P2, opacity)),
                    ("Project1 - Sheet: A101", TabTreatment.ProjectFill(P1, opacity)),
                    ("Project3 - Floor Plan: L1", TabTreatment.ProjectFill(SplashItDefaults.ProjectColors[6], opacity)));
                strips[opacity] = strip;
                rows.Add(Label($"Project fill at {opacity} %, unselected tabs at 50 % of it (third tab selected)", theme));
                rows.Add(strip);
            }
            var (bitmap, root) = Shoot("tabs-fill-opacity", theme, rows.ToArray());

            // Unselected: the fill blended over the strip behind the tab (Revit-like tabs are transparent).
            var well = HostTabs.Well(theme);
            Near(Shown(P1, 0.15, well), PixelIn(bitmap, root, HostTabs.Tab(strips[30], 0), 3, 10));  // 50 % of 30 %
            Near(Shown(P1, 0.5, well), PixelIn(bitmap, root, HostTabs.Tab(strips[100], 0), 3, 10));
            Near(Shown(P1, 0.3, well), PixelIn(bitmap, root, HostTabs.Tab(strips[30], 2), 3, 10));   // selected: the fill
            Near(HostTabs.ColorOf(P1), PixelIn(bitmap, root, HostTabs.Tab(strips[100], 2), 3, 10));
        });
    }

    /// <summary>#23: the Unselected tab opacity at 20, 50 (default) and 100 % of a 100 % fill.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void UnselectedTabOpacity_At20_50And100(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var rows = new List<UIElement>();
            var strips = new Dictionary<int, TabControl>();
            foreach (var share in new[] { 20, 50, 100 })
            {
                var strip = Styled(factory, original, theme, selected: 1,
                    ("Project1 - Floor Plan: L1", TabTreatment.ProjectFill(P1, 100, share)),
                    ("Project1 - Sheet: A101", TabTreatment.ProjectFill(P1, 100, share)),
                    ("Project2 - Floor Plan: L1", TabTreatment.ProjectFill(P2, 100, share)),
                    ("Project2 - 3D View: {3D}", TabTreatment.ProjectFill(P2, 100, share)));
                strips[share] = strip;
                rows.Add(Label($"Unselected tab opacity {share} % of a 100 % fill (2nd tab selected){(share == 100 ? ": only the top line marks it" : "")}", theme));
                rows.Add(strip);
            }
            var (bitmap, root) = Shoot("tabs-unselected-opacity", theme, rows.ToArray());

            var well = HostTabs.Well(theme);
            foreach (var (share, strip) in strips)
            {
                Near(Shown(P1, share / 100.0, well), PixelIn(bitmap, root, HostTabs.Tab(strip, 0), 3, 10));
                Near(HostTabs.ColorOf(P1), PixelIn(bitmap, root, HostTabs.Tab(strip, 1), 20, 10));
                Near(HostTabs.ColorOf(ColorMath.Darken(P1, TabStyleFactory.AccentDarken)), PixelIn(bitmap, root, HostTabs.Tab(strip, 1), 20, 1.5));
            }
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TwelveProjects_AllDefaultColors(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var first = Enumerable.Range(0, 6).Select(i => ($"P{i + 1} - L1", (TabTreatment?)TabTreatment.ProjectFill(SplashItDefaults.ProjectColors[i], 100))).ToArray();
            var second = Enumerable.Range(6, 6).Select(i => ($"P{i + 1} - L1", (TabTreatment?)TabTreatment.ProjectFill(SplashItDefaults.ProjectColors[i], 100))).ToArray();
            var a = Styled(factory, original, theme, selected: 0, first);
            var b = Styled(factory, original, theme, selected: 5, second);
            var (bitmap, root) = Shoot("tabs-twelve-projects", theme,
                Label("The 12 default Project colors (each its own project; P1 and P12 selected, the rest at 50 %)", theme), a, b);

            var well = HostTabs.Well(theme);
            Near(HostTabs.ColorOf(SplashItDefaults.ProjectColors[0]), PixelIn(bitmap, root, HostTabs.Tab(a, 0), 3, 10));
            for (var i = 1; i < 6; i++)
                Near(Shown(SplashItDefaults.ProjectColors[i], 0.5, well), PixelIn(bitmap, root, HostTabs.Tab(a, i), 3, 10));
            for (var i = 0; i < 5; i++)
                Near(Shown(SplashItDefaults.ProjectColors[6 + i], 0.5, well), PixelIn(bitmap, root, HostTabs.Tab(b, i), 3, 10));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void NativeRteTab_StaysNative(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var original = HostTabs.RevitLikeStyle(theme);
            var strip = Styled(factory, original, theme, selected: 3,
                ("Project1 - Floor Plan: L1", TabTreatment.ProjectFill(P1, 100)),
                ("Template1 - Floor Plan: Level 1", null),
                ("Family1 - Ref. Level", TabTreatment.FamilyBar(F1, 3, 100)),
                ("Template1 - 3D View: {3D}", null));
            var (bitmap, root) = Shoot("tabs-rte-native", theme,
                Label("An .rte template's tabs (2nd, and the selected 4th) stay native beside a project and a family", theme), strip);

            var rte = HostTabs.Tab(strip, 1);
            Assert.Same(original, rte.Style);
            Assert.Null(TabDecoration.FindAdorner(rte));
            Near(HostTabs.ColorOf(HostTabs.Well(theme)), PixelIn(bitmap, root, rte, 1.5, 10));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Aero2_StockTemplate_ShowsTheLimitOfTheFill(UiTheme theme)
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            // No original style: WPF's own theme style (Aero2 on Windows 10/11) supplies the template.
            var strip = Styled(factory, null, theme, selected: 1,
                ("Project1 - L1", TabTreatment.ProjectFill(P1, 100)),
                ("Project1 - L2", TabTreatment.ProjectFill(P1, 100)),
                ("Project2 - L1", TabTreatment.ProjectFill(P2, 100)),
                ("Family1 - Ref. Level", TabTreatment.FamilyBar(F1, 3, 100)));
            strip.ClearValue(Control.TemplateProperty); // WPF's own TabControl template too
            strip.ClearValue(Control.BackgroundProperty);
            var (bitmap, root) = Shoot("tabs-aero2", theme,
                Label("WPF's stock (Aero2) TabItem template: it paints its own selected background over the fill", theme), strip);

            Near(Shown(P2, 0.5, theme.Surface()), PixelIn(bitmap, root, HostTabs.Tab(strip, 2), 4, 8));
            Near(HostTabs.ColorOf(F1), PixelIn(bitmap, root, HostTabs.Tab(strip, 3), 0.5, 8));
        });
    }
}
