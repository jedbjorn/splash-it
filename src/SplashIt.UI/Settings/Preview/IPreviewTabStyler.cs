using System.Windows;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.UI.Tabs;
using SplashIt.UI.Theming;

namespace SplashIt.UI.Settings.Preview;

/// <summary>
/// Builds the preview strip's tab styles. The spec has the preview use the same style factory
/// as the colorizer: <see cref="FactoryTabStyler"/> is the colorizer's <see cref="TabStyleFactory"/>.
/// The seam stays so a test can substitute it.
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
/// The preview's styler: the colorizer's own <see cref="TabStyleFactory"/> (Lane B), so the
/// preview is exactly what the tabs get, including the adorner-drawn top line and bar (the
/// strip has its own adorner layer). It keeps its own factory instance, so dragging a slider
/// never churns the colorizer's style cache. The backdrop given is the color the tabs sit on;
/// text contrast is measured against it.
/// </summary>
internal sealed class FactoryTabStyler : IPreviewTabStyler
{
    private readonly TabStyleFactory _factory = new();

    public Style ProjectStyle(Style baseStyle, Rgb color, int fillOpacity, int unselectedShare, Rgb backdrop) =>
        _factory.Create(baseStyle, TabTreatment.ProjectFill(color, fillOpacity, unselectedShare), Solid(backdrop), SplashItTheme.Current);

    public Style FamilyStyle(Style baseStyle, Rgb color, int barWidth, int barOpacity) =>
        _factory.Create(baseStyle, TabTreatment.FamilyBar(color, barWidth, barOpacity), null, SplashItTheme.Current);

    private static SolidColorBrush Solid(Rgb c)
    {
        var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
