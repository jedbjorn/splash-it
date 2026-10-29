using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// What shows behind a tab (decision #33): the project fill replaces the tab's own background and is
/// translucent (it fades to half its opacity at the tab's edge, #36), so whatever is behind the tab,
/// Revit's tab strip or pane, shows through it. Text and × colors are chosen over the fill blended on
/// that. Read-only and bounded; never throws.
/// The order (#33): (1) <see cref="Rendered(DependencyObject, out string?)"/>, a sample of what the
/// window draws; (2) the painted-layer search; (3) the ancestor walk; then the caller's fallbacks,
/// the tab's original background and the theme surface.
/// <para>
/// <b>The painted-layer search (2).</b> A search of what is painted under the centre of the tabs' panel, from the nearest
/// layer down: at each of the panel's ancestors, first the siblings drawn below the branch that
/// holds the tabs (their subtrees, topmost first), then the ancestor's own background. Every tab is
/// skipped. The first border or panel background, or shape fill, that covers the point and is an
/// opaque <see cref="SolidColorBrush"/> is what shows; translucent ones above it are blended over
/// it. It reads the tree rather than hit-testing it, so elements that are not hit-testable count.
/// The VM run on Revit 2026 showed why the tab's ancestors alone are not enough: the strip Revit
/// draws behind its tabs (<c>#CCCCCC</c> light, <c>#454F61</c> dark) is not an ancestor's background
/// (the nearest solid ancestor is <c>#EEEEEE</c> / <c>#2E3440</c>, 9 levels up), and a hit test found
/// nothing. When the search finds nothing it falls back to that ancestor walk.
/// </para>
/// </summary>
public static class TabBackdrop
{
    /// <summary>The most visual parents looked at.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most elements the painted-layer search visits.</summary>
    public const int MaxVisited = 5000;

    /// <summary>The color behind <paramref name="tab"/> and where it came from; null when nothing solid was found.</summary>
    public static (Rgb Color, string Source)? Behind(DependencyObject tab)
    {
        try
        {
            return Rendered(tab) ?? PaintedBehind(tab, null) ?? AncestorBehind(tab);
        }
        catch (Exception)
        {
            return null; // a tree that cannot be inspected: the caller falls back
        }
    }

    /// <summary>
    /// What the tree says is behind <paramref name="tab"/>, from properties alone (no rendering): the
    /// painted-layer search, else the ancestor walk. Current even before the next layout pass.
    /// </summary>
    public static (Rgb Color, string Source)? FromTree(DependencyObject tab)
    {
        try
        {
            return PaintedBehind(tab, null) ?? AncestorBehind(tab);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// What is actually drawn behind the tabs of <paramref name="tab"/>'s pane (decision #33, step 1),
    /// sampled from the rendered window at a point that shows only the backdrop: the tabs' panel to the
    /// right of its last drawn tab (#36). Since #36 a project fill runs, translucent, to the tab's right
    /// edge, so no point on a filled tab shows the backdrop alone; the strip past the last tab does,
    /// and it is never on a tab (the point lies outside every drawn tab's bounds) nor on a decoration
    /// (adorners draw inside their tab). Only tabs that are drawn count (<c>Visibility</c> Visible:
    /// Revit leaves Hidden tabs at stale positions, and they are not drawn). One render at most per
    /// call. Null when the panel has no room past its last tab (an overflowing strip: the caller falls
    /// back to the painted-layer search), or the sample is not opaque; <paramref name="failure"/> then
    /// says why. The VM run on Revit 2026 showed why a sample comes first: the strip there
    /// (<c>#CCCCCC</c> light, <c>#454F61</c> dark) is not an ancestor's background.
    /// </summary>
    public static (Rgb Color, string Source)? Rendered(DependencyObject tab, out string? failure)
    {
        failure = null;
        try
        {
            if (VisualTreeHelper.GetParent(tab) is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } pane)
            {
                failure = "no laid-out tab panel";
                return null;
            }
            if (SamplePoint(pane) is not { } point)
            {
                failure = "no strip past the last tab";
                return null;
            }
            if (Sample(pane, point) is { } color) return (color, "rendered, the strip past the last tab");
            failure = "the sample was not opaque";
        }
        catch (Exception ex)
        {
            failure = "not inspectable (" + ex.GetType().Name + ")"; // best effort: the caller falls back
        }
        return null;
    }

    /// <inheritdoc cref="Rendered(DependencyObject, out string?)"/>
    public static (Rgb Color, string Source)? Rendered(DependencyObject tab) => Rendered(tab, out _);

    /// <summary>A rendered sample's outcome, for <see cref="Explain"/>.</summary>
    public static string Describe((Rgb Color, string Source)? rendered, string? failure) =>
        rendered is { } v ? $"{v.Color.ToHex()} ({v.Source})" : $"none ({failure ?? "not taken"})";

    /// <summary>
    /// Where the tabs' <paramref name="pane"/> shows only its backdrop, in the pane's coordinates: past
    /// the right edge of its rightmost drawn tab (by up to 4 px, and at least 1 px inside the pane), at
    /// the height of that tab's middle; null when the pane has under 2 px there, or when the point would
    /// still fall on a drawn tab (tabs in more than one row).
    /// </summary>
    internal static Point? SamplePoint(FrameworkElement pane)
    {
        var right = double.NegativeInfinity;
        var mid = 0.0;
        var count = VisualTreeHelper.GetChildrenCount(pane);
        for (var i = 0; i < count; i++)
        {
            // Visibility, not IsVisible: IsVisible is false off-screen (no presentation source).
            if (VisualTreeHelper.GetChild(pane, i) is not TabItem { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 } tab)
                continue;
            var bounds = tab.TransformToAncestor(pane).TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight));
            if (bounds.Right > right)
            {
                right = bounds.Right;
                mid = bounds.Top + bounds.Height / 2;
            }
        }
        if (double.IsNegativeInfinity(right)) return null;
        var room = pane.ActualWidth - right;
        if (room < 2) return null;
        var point = new Point(right + Math.Min(4, room / 2), mid);
        if (point.Y < 0 || point.Y >= pane.ActualHeight) return null;
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(pane, i) is not TabItem { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 } tab)
                continue;
            // The sample reads the pixel the point falls in (floored in the window), so 1 px to its left too must be off every drawn tab.
            var bounds = tab.TransformToAncestor(pane).TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight));
            if (bounds.Left < point.X && bounds.Right > point.X - 1 && bounds.Top <= point.Y && bounds.Bottom >= point.Y) return null;
        }
        return point;
    }

    /// <summary>The opaque color the window draws at <paramref name="point"/> of <paramref name="element"/>; null when not opaque.</summary>
    private static Rgb? Sample(FrameworkElement element, Point point)
    {
        Visual? root = null;
        DependencyObject? up = element;
        for (var i = 0; up is not null && i < MaxDepth; i++)
        {
            if (up is Visual v) root = v;
            up = VisualTreeHelper.GetParent(up);
        }
        if (root is null || ReferenceEquals(root, element)) return null;
        var p = element.TransformToAncestor(root).Transform(point);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(root)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(Math.Floor(p.X), Math.Floor(p.Y), 1, 1),
                Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, 1, 1));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixel = new byte[4];
        bitmap.CopyPixels(pixel, 4, 0);
        return pixel[3] == 255 ? new Rgb(pixel[2], pixel[1], pixel[0]) : null;
    }

    /// <summary>
    /// For the log: what each method gave, and the layers the search met on the way (type, name and
    /// brush), so a run in Revit shows where the color came from, or why nothing was found.
    /// <paramref name="rendered"/> is the rendered sample's outcome as already taken (<see cref="Describe"/>):
    /// it is not sampled again.
    /// </summary>
    public static string Explain(DependencyObject tab, string rendered)
    {
        static string Of((Rgb Color, string Source)? r) => r is { } v ? $"{v.Color.ToHex()} ({v.Source})" : "none";
        try
        {
            var trail = new List<string>();
            var painted = PaintedBehind(tab, trail);
            var ancestors = new StringBuilder();
            var node = VisualTreeHelper.GetParent(tab);
            for (var depth = 1; node is not null && depth <= 14; depth++, node = VisualTreeHelper.GetParent(node))
                ancestors.Append(depth == 1 ? "" : " < ").Append(Name(node)).Append(BrushText(AnyBackground(node)));
            return string.Create(CultureInfo.InvariantCulture,
                $"rendered: {rendered}; painted: {Of(painted)}; layers met: {(trail.Count == 0 ? "none" : string.Join(" | ", trail.Take(10)))}; ancestors: {Of(AncestorBehind(tab))}; chain: {ancestors}");
        }
        catch (Exception ex)
        {
            return "rendered: " + rendered + "; tree not inspectable (" + ex.GetType().Name + ")";
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

    // ---- the painted-layer search ------------------------------------------------------------------

    private sealed class Search(Visual root, Point point, List<string>? trail)
    {
        public readonly List<(Color Color, double Alpha)> Layers = [];
        public int Visited;
        public (Rgb Color, string Source)? Found;
        public bool Stop;

        /// <summary>One painted element covering the point, met from the top down. True when the search ends.</summary>
        public bool Meet(DependencyObject node, Brush brush, string where)
        {
            trail?.Add(Name(node) + BrushText(brush));
            if (brush is not SolidColorBrush solid)
            {
                // A gradient or a picture under the tabs: its color is not one number. Unknown: stop.
                if (brush.Opacity > 0) Stop = true;
                return Stop;
            }
            var alpha = solid.Color.A / 255.0 * Math.Clamp(solid.Opacity, 0, 1);
            if (alpha <= 0) return false;
            if (alpha < 1)
            {
                Layers.Add((solid.Color, alpha));
                return false;
            }
            Found = (Composite(solid.Color, Layers), Describe(node, where, Layers.Count));
            Stop = true;
            return true;
        }

        public bool Covers(DependencyObject node)
        {
            if (node is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } fe) return false;
            try
            {
                return fe.TransformToAncestor(root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight)).Contains(point);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>What <paramref name="node"/>'s subtree paints under the point, topmost first.</summary>
        public bool Subtree(DependencyObject node, string where)
        {
            if (Stop || ++Visited > MaxVisited) return Stop = true;
            if (node is TabItem or Adorner or AdornerLayer) return false;
            if (node is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return false;
            // A laid-out element away from the point: its subtree is elsewhere (children rarely leave their parent).
            if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } && !Covers(node)) return false;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = count - 1; i >= 0; i--)
                if (Subtree(VisualTreeHelper.GetChild(node, i), where)) return true;
            return PaintOf(node) is { } brush && Covers(node) && Meet(node, brush, where);
        }
    }

    private static (Rgb Color, string Source)? PaintedBehind(DependencyObject tab, List<string>? trail)
    {
        // The centre of the tabs' panel (the strip): one answer per pane, and laid out even when this
        // tab is not (an overflowed tab has no size).
        var start = VisualTreeHelper.GetParent(tab) is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } pane
            ? pane
            : tab as FrameworkElement;
        if (start is not { ActualWidth: > 0, ActualHeight: > 0 }) return null;
        Visual? root = null;
        DependencyObject? up = start;
        for (var i = 0; up is not null && i < MaxDepth; i++)
        {
            if (up is Visual v) root = v;
            up = VisualTreeHelper.GetParent(up);
        }
        if (root is null || ReferenceEquals(root, start)) return null;

        var point = start.TransformToAncestor(root).Transform(new Point(start.ActualWidth / 2, start.ActualHeight / 2));
        var search = new Search(root, point, trail);

        // The panel's own background is right behind the tabs.
        if (!ReferenceEquals(start, tab) && PaintOf(start) is { } own && search.Meet(start, own, "the tabs' panel"))
            return search.Found;

        DependencyObject child = start;
        var parent = VisualTreeHelper.GetParent(child);
        for (var depth = 1; parent is not null && depth <= MaxDepth && !search.Stop; depth++)
        {
            var where = string.Create(CultureInfo.InvariantCulture, $"behind the tabs, {depth} level{(depth == 1 ? "" : "s")} up");
            // Siblings drawn below the branch that holds the tabs: those before it, nearest first.
            var count = VisualTreeHelper.GetChildrenCount(parent);
            var index = -1;
            for (var i = 0; i < count; i++)
                if (ReferenceEquals(VisualTreeHelper.GetChild(parent, i), child)) { index = i; break; }
            for (var i = index - 1; i >= 0 && !search.Stop; i--)
                search.Subtree(VisualTreeHelper.GetChild(parent, i), where);
            if (search.Stop) break;
            if (PaintOf(parent) is { } brush && search.Covers(parent) && search.Meet(parent, brush, where)) break;
            child = parent;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return search.Found;
    }

    // ---- the ancestor walk (fallback) ---------------------------------------------------------------

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
                Describe(node, string.Create(CultureInfo.InvariantCulture, $"ancestor, {depth} level{(depth == 1 ? "" : "s")} up"), layers.Count));
        }
        return null;
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>The opaque <paramref name="bottom"/> with the translucent <paramref name="layers"/> (nearest the tab first) blended over it.</summary>
    private static Rgb Composite(Color bottom, List<(Color Color, double Alpha)> layers)
    {
        var c = new Rgb(bottom.R, bottom.G, bottom.B);
        for (var i = layers.Count - 1; i >= 0; i--)
            c = ColorMath.Blend(new Rgb(layers[i].Color.R, layers[i].Color.G, layers[i].Color.B), layers[i].Alpha, c);
        return c;
    }

    private static string Name(DependencyObject node) =>
        node is FrameworkElement { Name.Length: > 0 } fe ? $"{node.GetType().Name}'{fe.Name}'" : node.GetType().Name;

    private static string BrushText(Brush? brush) => brush switch
    {
        null => "",
        SolidColorBrush s => string.Create(CultureInfo.InvariantCulture,
            $"[#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}{(s.Opacity < 1 ? $"@{s.Opacity:0.##}" : "")}]"),
        _ => $"[{brush.GetType().Name}]",
    };

    private static string Describe(DependencyObject node, string where, int translucent) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Name(node).Replace('\'', ' ').TrimEnd()}, {where}{(translucent == 0 ? "" : $", with {translucent} translucent layer(s) over it")}");

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

    /// <summary>For the log's chain only: any background a node holds, a control's included.</summary>
    private static Brush? AnyBackground(DependencyObject node) => node switch
    {
        Control control => control.Background,
        _ => PaintOf(node),
    };
}
