using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// The project fill's shape (FnB decision #36, which supersedes #34 and #30's end point): not a setting,
/// a constant of the look. The fill is solid from the tab's left edge to the fade start, which stays
/// where #30 put it, <see cref="FadeWidth"/> + <see cref="Gap"/> pixels before the close button's left
/// edge; from there it fades linearly through the × to <see cref="EdgeShare"/> of its opacity at the
/// tab's right edge. It is never transparent, so tabs of one project meet with no colorless gap. The
/// selected tab's top line takes the same shape. The start is anchored to the ×, so the solid part and
/// where titles sit are what #30 gave, whatever the title's length.
/// <para>
/// <b>The anchor.</b> The close button is found by name (<see cref="TabCloseGlyph.ButtonNames"/>).
/// Revit keeps it <c>Hidden</c> (not collapsed) on tabs that are not selected or hovered
/// (<c>shared/evidence/lane-b/vm/2026-review/diag-2026-close-button.txt</c>: a 16 x 16 button flush
/// with the right of a content area inset 2 px), so its layout slot is there on every tab and
/// showing it on hover moves nothing. When the button is missing or collapsed (no slot), the anchor
/// is the tab's right edge less <see cref="CloseSlot"/>, the slot Revit's button takes. A tab too
/// narrow for the solid part fades from its left edge. No width is ever negative.
/// </para>
/// </summary>
/// <param name="FadeWidth">With <see cref="Gap"/>, how far before the × the fade starts: #30's 24 px fade, which ended <see cref="Gap"/> before the ×.</param>
/// <param name="Gap">Pixels between #30's fade end and the close button's left edge (2).</param>
/// <param name="CloseSlot">Pixels from the tab's right edge to the close button's left edge when the button has no slot to measure (18).</param>
/// <param name="TitleGap">Pixels from a title's end to the close button's left edge in Revit's template (12): how far
/// into the fade a title that fills its tab reaches (#31).</param>
/// <param name="EdgeShare">The share of the fill's opacity left at the tab's right edge (0.5, #36).</param>
/// <param name="CloseWidth">The close button's width, pixels (16, Revit's): the span the × color is chosen over.</param>
public readonly record struct ProjectFillShape(
    double FadeWidth, double Gap, double CloseSlot, double TitleGap = 12, double EdgeShare = 0.5, double CloseWidth = 16)
{
    /// <summary>
    /// The shape in use, FnB decision #36: the fade starts 26 px before the ×, where #30's did, and runs
    /// to 50 % of the opacity at the tab's right edge; Revit's × slot is 18 px from the edge, 12 px after
    /// the title.
    /// </summary>
    public static ProjectFillShape Default { get; } = new(FadeWidth: 24, Gap: 2, CloseSlot: 18, TitleGap: 12, EdgeShare: 0.5, CloseWidth: 16);

    /// <summary>How far before the close button's left edge the fade starts (26).</summary>
    public double StartBeforeClose => Math.Max(0, FadeWidth) + Math.Max(0, Gap);

    /// <summary>The fade's length on a tab whose × is in Revit's slot: from its start to the right edge (44).</summary>
    public double TypicalRun => StartBeforeClose + Math.Max(0, CloseSlot);

    /// <summary>The share of the opacity left at the right edge, 0 to 1.</summary>
    public double Edge => double.IsNaN(EdgeShare) ? 1 : Math.Clamp(EdgeShare, 0, 1);

    /// <summary>
    /// The share of the fill's opacity <paramref name="intoFade"/> pixels past the fade start, on a fade
    /// <paramref name="run"/> pixels long: 1 before it, falling linearly to <see cref="Edge"/> at its end.
    /// </summary>
    public double ShareAt(double intoFade, double run)
    {
        if (run <= 0 || double.IsNaN(run) || double.IsNaN(intoFade)) return 1;
        return 1 - (1 - Edge) * Math.Clamp(intoFade / run, 0, 1);
    }

    /// <summary>
    /// The share of the fill's opacity under a title's last pixel column (#31) on Revit's geometry: its
    /// centre is <see cref="TitleGap"/> + 0.5 px before the ×, 13.5 px into the 44 px fade (about 85 %).
    /// It is the title's least opaque point; 1 when the title ends before the fade starts.
    /// </summary>
    public double TitleEndShare => ShareAt(StartBeforeClose - TitleGap - 0.5, TypicalRun);

    /// <summary>
    /// The shares of the fill's opacity under the × on Revit's geometry, one per pixel column of the
    /// <see cref="CloseWidth"/> px button (#36: the × sits on the fade). The × is chosen over all of them.
    /// </summary>
    public double[] CloseShares()
    {
        var columns = Math.Max(1, (int)Math.Round(double.IsNaN(CloseWidth) ? 1 : Math.Max(1, CloseWidth)));
        var shares = new double[columns];
        for (var i = 0; i < columns; i++) shares[i] = ShareAt(StartBeforeClose + i + 0.5, TypicalRun);
        return shares;
    }

    /// <summary>
    /// The fade on a tab <paramref name="tabWidth"/> wide whose close button's left edge is at
    /// <paramref name="closeLeft"/> (null: unknown), in the tab's own coordinates: from
    /// <see cref="StartBeforeClose"/> before the button (never below 0 or beyond the tab) to the tab's
    /// right edge.
    /// </summary>
    public (double Start, double End) FadeFor(double tabWidth, double? closeLeft)
    {
        if (tabWidth <= 0 || double.IsNaN(tabWidth)) return (0, 0);
        var left = closeLeft is { } c && !double.IsNaN(c) ? c : tabWidth - Math.Max(0, CloseSlot);
        return (Math.Clamp(left - StartBeforeClose, 0, tabWidth), tabWidth);
    }

    /// <summary>The fade on <paramref name="tab"/>, its start measured from its close button's layout slot when it has one.</summary>
    public (double Start, double End) FadeFor(FrameworkElement tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return FadeFor(tab.ActualWidth, CloseButtonLeft(tab));
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
/// The project fill's brush on one tab (#36), bound in the derived style to the tab's
/// <c>ActualWidth</c> and the tab itself: <see cref="Color"/> at <see cref="Opacity"/> from the
/// left edge, fading to <see cref="ProjectFillShape.EdgeShare"/> of that opacity over the fade
/// <see cref="ProjectFillShape.FadeFor(FrameworkElement)"/> gives (to the tab's right edge), in
/// absolute pixels. Re-evaluated when the tab's width changes; brushes are frozen and reused per
/// fade position.
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
            return BrushFor(Shape.FadeFor(tab));
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
        if (key.Item2 <= key.Item1) return Solid; // no room for a fade: the plain fill
        if (_brushes.TryGetValue(key, out var cached)) return cached;
        var brush = Frozen(new LinearGradientBrush(new GradientStopCollection { new(Media(Opacity), 0), new(Media(Opacity * Shape.Edge), 1) })
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(key.Item1, 0),
            EndPoint = new Point(key.Item2, 0),
            SpreadMethod = GradientSpreadMethod.Pad, // solid before the fade
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
