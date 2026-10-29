using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// What shows behind a tab (the coordinator's resolution of review m1 on #30/#31): the project fill
/// replaces the tab's own background and fades to transparent, so its fading end, and the close ×,
/// show whatever is behind the tab, Revit's tab strip or pane. Text and × colors are chosen against
/// that. Read-only and bounded; never throws.
/// <para>
/// <b>How.</b> First a hit test at the centre of the tabs' panel that skips every tab and its contents:
/// the first element under it that paints an opaque <see cref="SolidColorBrush"/> (a border's or a
/// panel's background, a shape's fill) is what shows, with translucent ones above it blended over
/// it. The VM run on Revit 2026 showed why a hit test and not only the tab's ancestors: the strip
/// Revit draws behind its tabs (<c>#CCCCCC</c> light) is not an ancestor's background (the nearest
/// solid ancestor is <c>#EEEEEE</c>, 9 levels up). When the hit test finds nothing (the tree is not
/// laid out, or nothing behind the tab is hit-testable), the tab's visual parents are walked to the
/// first panel or border with an opaque solid background.
/// </para>
/// </summary>
public static class TabBackdrop
{
    /// <summary>The most visual parents the ancestor walk, and the search for the root, look at.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most hit-test results looked at.</summary>
    public const int MaxHits = 32;

    /// <summary>The color behind <paramref name="tab"/> and where it came from; null when nothing solid was found.</summary>
    public static (Rgb Color, string Source)? Behind(DependencyObject tab)
    {
        try
        {
            return HitTestBehind(tab) ?? AncestorBehind(tab);
        }
        catch (Exception)
        {
            return null; // a tree that cannot be inspected: the caller falls back
        }
    }

    /// <summary>
    /// For the log: both ways of finding what is behind <paramref name="tab"/>, so a run in Revit
    /// shows whether they agree ("hit test: …; ancestors: …").
    /// </summary>
    public static string Explain(DependencyObject tab)
    {
        static string Of((Rgb Color, string Source)? r) => r is { } v ? $"{v.Color.ToHex()} ({v.Source})" : "none";
        try
        {
            return $"hit test: {Of(HitTestBehind(tab))}; ancestors: {Of(AncestorBehind(tab))}";
        }
        catch (Exception ex)
        {
            return "not inspectable (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// The backdrop to choose text and × colors against: what is behind <paramref name="tab"/>
    /// (<see cref="Behind"/>), else the tab's <paramref name="originalBackground"/> over the theme
    /// surface, else the surface (<see cref="TabStyleFactory.BackdropFor"/>).
    /// </summary>
    public static (Rgb Color, string Source) Resolve(DependencyObject tab, Brush? originalBackground, SplashIt.Core.Theming.UiTheme theme)
    {
        if (Behind(tab) is { } behind) return behind;
        return originalBackground is SolidColorBrush { Color.A: > 0 }
            ? (TabStyleFactory.BackdropFor(originalBackground, theme), "fallback: the tab's own background")
            : (TabStyleFactory.BackdropFor(originalBackground, theme), "fallback: the theme surface");
    }

    private static (Rgb Color, string Source)? HitTestBehind(DependencyObject tab)
    {
        // At the centre of the tabs' panel (the strip), skipping every tab: one answer per pane,
        // laid out even when this tab is not (an overflowed tab has no size).
        var element = VisualTreeHelper.GetParent(tab) is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } pane
            ? pane
            : tab as FrameworkElement;
        if (element is not { ActualWidth: > 0, ActualHeight: > 0 }) return null;
        Visual? root = null;
        DependencyObject? node = element;
        for (var i = 0; node is not null && i < MaxDepth; i++)
        {
            if (node is Visual v) root = v;
            node = VisualTreeHelper.GetParent(node);
        }
        if (root is null || ReferenceEquals(root, element)) return null;

        var point = element.TransformToAncestor(root).Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var layers = new List<(Color Color, double Alpha)>();
        (Rgb Color, string Source)? found = null;
        var hits = 0;
        VisualTreeHelper.HitTest(root,
            d => ReferenceEquals(d, tab) || d is TabItem or Adorner or AdornerLayer
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            result =>
            {
                if (++hits > MaxHits) return HitTestResultBehavior.Stop;
                if (PaintOf(result.VisualHit) is not SolidColorBrush solid) return HitTestResultBehavior.Continue;
                var alpha = solid.Color.A / 255.0 * Math.Clamp(solid.Opacity, 0, 1);
                if (alpha <= 0) return HitTestResultBehavior.Continue;
                if (alpha < 1)
                {
                    layers.Add((solid.Color, alpha));
                    return HitTestResultBehavior.Continue;
                }
                found = (Composite(solid.Color, layers), Describe(result.VisualHit, "under the tab (hit test)", layers.Count));
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(point));
        return found;
    }

    private static (Rgb Color, string Source)? AncestorBehind(DependencyObject tab)
    {
        var layers = new List<(Color Color, double Alpha)>();
        var node = VisualTreeHelper.GetParent(tab);
        for (var depth = 1; node is not null && depth <= MaxDepth; depth++, node = VisualTreeHelper.GetParent(node))
        {
            if (BackgroundOf(node) is not SolidColorBrush solid) continue;
            var alpha = solid.Color.A / 255.0 * Math.Clamp(solid.Opacity, 0, 1);
            if (alpha <= 0) continue;
            if (alpha < 1)
            {
                layers.Add((solid.Color, alpha));
                continue;
            }
            return (Composite(solid.Color, layers),
                Describe(node, string.Create(CultureInfo.InvariantCulture, $"{depth} level{(depth == 1 ? "" : "s")} up"), layers.Count));
        }
        return null;
    }

    /// <summary>The opaque <paramref name="bottom"/> with the translucent <paramref name="layers"/> (nearest the tab first) blended over it.</summary>
    private static Rgb Composite(Color bottom, List<(Color Color, double Alpha)> layers)
    {
        var c = new Rgb(bottom.R, bottom.G, bottom.B);
        for (var i = layers.Count - 1; i >= 0; i--)
            c = ColorMath.Blend(new Rgb(layers[i].Color.R, layers[i].Color.G, layers[i].Color.B), layers[i].Alpha, c);
        return c;
    }

    private static string Describe(DependencyObject node, string where, int translucent)
    {
        var name = node is FrameworkElement { Name.Length: > 0 } fe ? $" '{fe.Name}'" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{node.GetType().Name}{name}, {where}{(translucent == 0 ? "" : $", with {translucent} translucent layer(s) over it")}");
    }

    /// <summary>
    /// The background a node paints: a panel's or a border's, or a shape's fill. A control's own
    /// <c>Background</c> is painted by its template's parts, so it is not counted twice.
    /// </summary>
    private static Brush? PaintOf(DependencyObject node) => node switch
    {
        Shape shape => shape.Fill,
        _ => BackgroundOf(node),
    };

    private static Brush? BackgroundOf(DependencyObject node) => node switch
    {
        Panel panel => panel.Background,
        Border border => border.Background,
        _ => null,
    };
}
