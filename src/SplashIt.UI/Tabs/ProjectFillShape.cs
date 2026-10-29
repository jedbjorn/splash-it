using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// EXPLORATION ONLY (branch exp/project-fill-options, never merged): the shapes the FnB is
/// shown for a project tab's fill, after "the same fade out and length we do for the
/// families top bar would look cool for the fill on the projects tab". Internal, and
/// <see cref="P0Solid"/> (the shipped look) unless a test asks for another, so no shipped
/// behavior changes.
/// </summary>
internal enum ProjectFillShape
{
    /// <summary>P0, current: a solid fill across the whole tab; the selected tab's 3 px top line full width.</summary>
    P0Solid = 0,

    /// <summary>P1, the family bar's shape: the fill over 95 % of the width, its last 15 % fading out; the last 5 % is Revit's own.</summary>
    P1FamilyShape,

    /// <summary>P2: the fill across the full width, its last 15 % fading out to the right edge.</summary>
    P2FadeToEdge,

    /// <summary>P3: a solid fill; the selected tab's 3 px top line takes the family shape (95 %, fading over its last 15 %).</summary>
    P3ShapedLine,

    /// <summary>P4: P1's fill plus P3's line.</summary>
    P4ShapedFillAndLine,

    /// <summary>P5: a gentle gradient, 100 % of the fill at the left to 35 % at the right edge, no hard stop.</summary>
    P5Gentle,
}

/// <summary>Where along a tab each <see cref="ProjectFillShape"/> draws its fill, and how strongly.</summary>
internal static class ProjectFillProfile
{
    /// <summary>The family bar's numbers (#29), reused by P1, P3 and P4.</summary>
    public const double FamilyWidth = 0.95, FamilyFade = 0.15;

    /// <summary>P5's right-edge share of the fill opacity.</summary>
    public const double GentleEnd = 0.35;

    /// <summary>When the close button cannot be found: its centre is assumed this many pixels from the tab's right edge.</summary>
    public const double AssumedGlyphInset = 8;

    /// <summary>True when the fill is a gradient (the × may sit on a faded part).</summary>
    public static bool Fades(ProjectFillShape shape) =>
        shape is ProjectFillShape.P1FamilyShape or ProjectFillShape.P2FadeToEdge
            or ProjectFillShape.P4ShapedFillAndLine or ProjectFillShape.P5Gentle;

    /// <summary>True when the selected tab's top line takes the family shape.</summary>
    public static bool ShapedLine(ProjectFillShape shape) =>
        shape is ProjectFillShape.P3ShapedLine or ProjectFillShape.P4ShapedFillAndLine;

    /// <summary>The fill's stops: (offset across the tab 0 to 1, share of the state's opacity 0 to 1).</summary>
    public static (double Offset, double Share)[] Stops(ProjectFillShape shape) => shape switch
    {
        ProjectFillShape.P1FamilyShape or ProjectFillShape.P4ShapedFillAndLine =>
            [(0, 1), (FamilyWidth * (1 - FamilyFade), 1), (FamilyWidth, 0), (1, 0)],
        ProjectFillShape.P2FadeToEdge => [(0, 1), (1 - FamilyFade, 1), (1, 0)],
        ProjectFillShape.P5Gentle => [(0, 1), (1, GentleEnd)],
        _ => [(0, 1), (1, 1)],
    };

    /// <summary>The share of the opacity the fill shows at <paramref name="x"/> (0 to 1 across the tab).</summary>
    public static double ShareAt(ProjectFillShape shape, double x)
    {
        var stops = Stops(shape);
        x = Math.Clamp(double.IsNaN(x) ? 1 : x, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            var (o0, s0) = stops[i - 1];
            var (o1, s1) = stops[i];
            if (x > o1) continue;
            return o1 <= o0 ? s1 : s0 + (s1 - s0) * (x - o0) / (o1 - o0);
        }
        return stops[^1].Share;
    }

    /// <summary>The fill brush: <paramref name="color"/> at <paramref name="opacity"/> times the shape's share, left to right.</summary>
    public static Brush Fill(ProjectFillShape shape, Rgb color, double opacity)
    {
        var stops = new GradientStopCollection();
        foreach (var (offset, share) in Stops(shape))
        {
            var alpha = (byte)Math.Round(Math.Clamp(opacity * share, 0, 1) * 255, MidpointRounding.AwayFromZero);
            stops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), offset));
        }
        var brush = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The close glyph's color on a faded fill: black or white, whichever has the higher WCAG
    /// contrast against the fill as shown under the glyph's centre (the fill at that point,
    /// blended over the tab's own background), not the solid part's choice.
    /// </summary>
    public static Rgb GlyphColorAt(ProjectFillShape shape, Rgb color, double opacity, Rgb backdrop, double x) =>
        ColorMath.TextColorFor(color, opacity * ShareAt(shape, x), backdrop);

    /// <summary>Where the close button's centre is across <paramref name="tab"/> (0 to 1).</summary>
    public static double GlyphShare(FrameworkElement tab)
    {
        var width = tab.ActualWidth;
        if (width <= 0 || double.IsNaN(width)) return 1;
        try
        {
            if (TabCloseGlyph.FindButton(tab) is { ActualWidth: > 0 } button && tab.IsAncestorOf(button))
            {
                var centre = button.TransformToAncestor(tab).Transform(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                return Math.Clamp(centre.X / width, 0, 1);
            }
        }
        catch (InvalidOperationException)
        {
            // Not laid out together yet: fall back to the assumed inset.
        }
        return Math.Clamp((width - AssumedGlyphInset) / width, 0, 1);
    }
}

/// <summary>
/// Binds a tab's close-glyph color to where the glyph sits on a faded fill: values are the
/// tab's ActualWidth (only to re-evaluate on a resize) and the tab itself.
/// </summary>
internal sealed class ShapedGlyphConverter(ProjectFillShape shape, Rgb color, double opacity, Rgb backdrop) : IMultiValueConverter
{
    private static readonly SolidColorBrush BlackBrush = Frozen(ColorMath.Black);
    private static readonly SolidColorBrush WhiteBrush = Frozen(ColorMath.White);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var x = values.Length > 1 && values[1] is FrameworkElement tab ? ProjectFillProfile.GlyphShare(tab) : 1;
        return ProjectFillProfile.GlyphColorAt(shape, color, opacity, backdrop, x) == ColorMath.Black ? BlackBrush : WhiteBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static MultiBinding Binding(ProjectFillShape shape, Rgb color, double opacity, Rgb backdrop)
    {
        var binding = new MultiBinding { Converter = new ShapedGlyphConverter(shape, color, opacity, backdrop) };
        binding.Bindings.Add(new Binding(nameof(FrameworkElement.ActualWidth)) { RelativeSource = RelativeSource.Self });
        binding.Bindings.Add(new Binding { RelativeSource = RelativeSource.Self });
        return binding;
    }

    private static SolidColorBrush Frozen(Rgb c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
