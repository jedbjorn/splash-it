using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Settings.Preview;

/// <summary>
/// Builds the preview strip's tab styles. The spec has the preview use the same style factory
/// as the colorizer; that factory (ITabStyleFactory, Lane B) is built in parallel, so the
/// preview goes through this small seam and <see cref="PlaceholderTabStyler"/> stands in until
/// the factory lands, when an adapter over it replaces the placeholder in
/// <see cref="PreviewStrip.Styler"/>.
/// </summary>
internal interface IPreviewTabStyler
{
    /// <summary>
    /// A project tab (spec, Shapes and settings; decision #23): the selected tab shows
    /// <paramref name="color"/> at <paramref name="fillOpacity"/> percent over <paramref name="backdrop"/>
    /// with a 3 px darker line along its top; the project's other tabs show it at fill ×
    /// <paramref name="unselectedShare"/> percent. Text and the close glyph are black or white,
    /// whichever reads better on the color as shown. <see cref="Style.BasedOn"/> is <paramref name="baseStyle"/>.
    /// </summary>
    Style ProjectStyle(Style baseStyle, Rgb color, int fillOpacity, int unselectedShare, Rgb backdrop);

    /// <summary>
    /// A family tab: a <paramref name="barWidth"/> px bar of <paramref name="color"/> at
    /// <paramref name="barOpacity"/> percent on the left edge; everything else stays the base style's.
    /// </summary>
    Style FamilyStyle(Style baseStyle, Rgb color, int barWidth, int barOpacity);
}

/// <summary>
/// Stand-in for Lane B's tab style factory: draws the spec's treatments (Shapes and settings)
/// on the preview's own flat tab template through <see cref="PreviewTab"/>'s attached properties.
/// </summary>
internal sealed class PlaceholderTabStyler : IPreviewTabStyler
{
    /// <summary>How much hover lightens a project fill.</summary>
    private const double HoverLighten = 0.15;

    /// <summary>How much the selected tab's top accent darkens the color.</summary>
    private const double AccentDarken = 0.3;

    public Style ProjectStyle(Style baseStyle, Rgb color, int fillOpacity, int unselectedShare, Rgb backdrop)
    {
        var selectedFill = ColorMath.Blend(color, fillOpacity / 100.0, backdrop);
        var shown = ColorMath.Blend(color, fillOpacity / 100.0 * (unselectedShare / 100.0), backdrop);
        var hover = ColorMath.Lighten(shown, HoverLighten);

        var style = new Style(typeof(TabItem), baseStyle);
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(shown)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(ColorMath.TextColorFor(shown))));

        var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(Control.BackgroundProperty, Brush(hover)));
        hovered.Setters.Add(new Setter(Control.ForegroundProperty, Brush(ColorMath.TextColorFor(hover))));
        style.Triggers.Add(hovered);

        // Last, so it wins over hover: the full fill opacity and a 3 px darker line along the top.
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush(selectedFill)));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brush(ColorMath.TextColorFor(selectedFill))));
        selected.Setters.Add(new Setter(PreviewTab.AccentBrushProperty, Brush(ColorMath.Darken(color, AccentDarken))));
        style.Triggers.Add(selected);

        style.Seal();
        return style;
    }

    public Style FamilyStyle(Style baseStyle, Rgb color, int barWidth, int barOpacity)
    {
        var style = new Style(typeof(TabItem), baseStyle);
        style.Setters.Add(new Setter(PreviewTab.BarBrushProperty, Brush(color, barOpacity / 100.0)));
        style.Setters.Add(new Setter(PreviewTab.BarWidthProperty, (double)barWidth));
        style.Seal();
        return style;
    }

    private static SolidColorBrush Brush(Rgb c, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }
}

/// <summary>Attached properties the preview's tab template draws: the selected tab's top accent and the family bar.</summary>
public static class PreviewTab
{
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.RegisterAttached(
        "AccentBrush", typeof(Brush), typeof(PreviewTab), new FrameworkPropertyMetadata(null));

    public static Brush? GetAccentBrush(DependencyObject d) => (Brush?)d.GetValue(AccentBrushProperty);

    public static void SetAccentBrush(DependencyObject d, Brush? value) => d.SetValue(AccentBrushProperty, value);

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.RegisterAttached(
        "BarBrush", typeof(Brush), typeof(PreviewTab), new FrameworkPropertyMetadata(null));

    public static Brush? GetBarBrush(DependencyObject d) => (Brush?)d.GetValue(BarBrushProperty);

    public static void SetBarBrush(DependencyObject d, Brush? value) => d.SetValue(BarBrushProperty, value);

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.RegisterAttached(
        "BarWidth", typeof(double), typeof(PreviewTab), new FrameworkPropertyMetadata(0.0));

    public static double GetBarWidth(DependencyObject d) => (double)d.GetValue(BarWidthProperty);

    public static void SetBarWidth(DependencyObject d, double value) => d.SetValue(BarWidthProperty, value);
}
