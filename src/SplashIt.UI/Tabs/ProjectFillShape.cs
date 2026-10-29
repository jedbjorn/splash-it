using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// The project fill's shape (FnB decision #30, option P4 anchored to the close ×): not a setting,
/// a constant of the look. The fill is solid from the tab's left edge, then fades linearly to
/// transparent over <see cref="FadeWidth"/> pixels, ending <see cref="Gap"/> pixels before the
/// close button's left edge, so the × sits on Revit's own strip. The selected tab's top line
/// takes the same shape and end. The fade is fixed in pixels and anchored to the ×, so its width
/// is the same on every tab, whatever the title's length.
/// <para>
/// <b>The anchor.</b> The close button is found by name (<see cref="TabCloseGlyph.ButtonNames"/>).
/// Revit keeps it <c>Hidden</c> (not collapsed) on tabs that are not selected or hovered
/// (<c>shared/evidence/lane-b/vm/2026-review/diag-2026-close-button.txt</c>: a 16 x 16 button flush
/// with the right of a content area inset 2 px), so its layout slot is there on every tab and
/// showing it on hover moves nothing. When the button is missing or collapsed (no slot), the anchor
/// is the tab's right edge less <see cref="CloseSlot"/>, the slot Revit's button takes. A tab too
/// narrow for the whole fade fades from its left edge; a tab with no room at all shows no fill.
/// No width is ever negative.
/// </para>
/// </summary>
/// <param name="FadeWidth">The fade's width, pixels (24: about 15 % of a typical 155 px tab, as on the option sheet).</param>
/// <param name="Gap">Pixels between the fade's end and the close button's left edge.</param>
/// <param name="CloseSlot">Pixels from the tab's right edge to the close button's left edge when the button has no slot to measure.</param>
public readonly record struct ProjectFillShape(double FadeWidth, double Gap, double CloseSlot)
{
    /// <summary>The shape in use, FnB decision #30: a 24 px fade ending 2 px before the ×; Revit's × slot is 18 px.</summary>
    public static ProjectFillShape Default { get; } = new(FadeWidth: 24, Gap: 2, CloseSlot: 18);

    /// <summary>
    /// Where the fill ends (fully transparent) on a tab <paramref name="tabWidth"/> wide whose close
    /// button's left edge is at <paramref name="closeLeft"/> (null: unknown), in the tab's own
    /// coordinates: <see cref="Gap"/> before the button, never below 0 or beyond the tab.
    /// </summary>
    public double AnchorFor(double tabWidth, double? closeLeft)
    {
        if (tabWidth <= 0 || double.IsNaN(tabWidth)) return 0;
        var left = closeLeft is { } c && !double.IsNaN(c) ? c : tabWidth - Math.Max(0, CloseSlot);
        return Math.Clamp(left - Math.Max(0, Gap), 0, tabWidth);
    }

    /// <summary>The anchor on <paramref name="tab"/>, measured from its close button's layout slot when it has one.</summary>
    public double AnchorFor(FrameworkElement tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return AnchorFor(tab.ActualWidth, CloseButtonLeft(tab));
    }

    /// <summary>
    /// The fade on a tab whose fill ends at <paramref name="anchor"/>: it starts <see cref="FadeWidth"/>
    /// before the anchor, or at the tab's left edge when the tab is narrower than that.
    /// </summary>
    public (double Start, double End) FadeFor(double anchor)
    {
        var end = double.IsNaN(anchor) ? 0 : Math.Max(0, anchor);
        return (Math.Max(0, end - Math.Max(0, FadeWidth)), end);
    }

    /// <summary>
    /// The left edge of <paramref name="tab"/>'s close button, in the tab's coordinates, from its
    /// layout slot; null when there is none (no button, collapsed, or not laid out yet).
    /// A hidden button still has its slot.
    /// </summary>
    public static double? CloseButtonLeft(FrameworkElement tab)
    {
        try
        {
            if (TabCloseGlyph.FindButton(tab) is not { } button) return null;
            if (button.Visibility == Visibility.Collapsed || button.ActualWidth <= 0 || !tab.IsAncestorOf(button)) return null;
            return button.TransformToAncestor(tab).Transform(new Point(0, 0)).X;
        }
        catch (InvalidOperationException)
        {
            return null; // not in one tree yet
        }
    }
}

/// <summary>
/// The project fill's brush on one tab (#30), bound in the derived style to the tab's
/// <c>ActualWidth</c> and the tab itself: <see cref="Color"/> at <see cref="Opacity"/> from the
/// left edge, fading to transparent over the fade <see cref="ProjectFillShape.FadeFor"/> gives,
/// in absolute pixels. Re-evaluated when the tab's width changes; brushes are frozen and
/// reused per fade position.
/// </summary>
internal sealed class ProjectFillConverter(Rgb color, double opacity, ProjectFillShape shape) : IMultiValueConverter
{
    /// <summary>Above this many cached brushes the cache starts over (a safety bound).</summary>
    private const int MaxCached = 64;

    private readonly Dictionary<(double, double), Brush> _brushes = new();

    public Rgb Color { get; } = color;

    public double Opacity { get; } = opacity;

    public ProjectFillShape Shape { get; } = shape;

    /// <summary>The fill with no fade: <see cref="Color"/> at <see cref="Opacity"/>.</summary>
    public Brush Solid => _solid ??= Frozen(new SolidColorBrush(Media(Opacity)));

    private Brush? _solid;

    private System.Windows.Media.Color Media(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);
        return System.Windows.Media.Color.FromArgb(alpha, Color.R, Color.G, Color.B);
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            // Not laid out yet: the plain fill until the first layout pass gives the tab a width
            // (it is never rendered before then).
            if (values.Length < 2 || values[1] is not FrameworkElement { ActualWidth: > 0 } tab) return Solid;
            return BrushFor(Shape.FadeFor(Shape.AnchorFor(tab)));
        }
        catch (Exception)
        {
            return Solid; // never into the binding engine: the plain fill rather than a fault
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>The brush for a fade from <paramref name="fade"/>.Start to .End (pixels from the left edge).</summary>
    internal Brush BrushFor((double Start, double End) fade)
    {
        var key = (Math.Round(fade.Start, 2), Math.Round(fade.End, 2));
        if (_brushes.TryGetValue(key, out var cached)) return cached;
        var from = Media(Opacity);
        var to = Media(0);
        var brush = Frozen(key.Item2 <= key.Item1
            ? new SolidColorBrush(to) // no room for a fill
            : new LinearGradientBrush(new GradientStopCollection { new(from, 0), new(to, 1) })
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(key.Item1, 0),
                EndPoint = new Point(key.Item2, 0),
                SpreadMethod = GradientSpreadMethod.Pad, // solid before the fade, transparent after it
            });
        if (_brushes.Count >= MaxCached) _brushes.Clear();
        _brushes[key] = brush;
        return brush;
    }

    /// <summary>The binding a style setter uses: the tab's width (to re-evaluate on a resize) and the tab.</summary>
    public static MultiBinding Binding(Rgb color, double opacity, ProjectFillShape shape)
    {
        var binding = new MultiBinding { Converter = new ProjectFillConverter(color, opacity, shape) };
        binding.Bindings.Add(new Binding(nameof(FrameworkElement.ActualWidth)) { RelativeSource = RelativeSource.Self });
        binding.Bindings.Add(new Binding { RelativeSource = RelativeSource.Self });
        return binding;
    }
}
