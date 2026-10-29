using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;

namespace SplashIt.UI.Tabs;

/// <summary>
/// What shows behind a tab (the coordinator's resolution of review m1 on #30/#31): the project fill
/// replaces the tab's own background and fades to transparent, so its fading end, and the close ×,
/// show whatever is behind the tab, Revit's tab strip or pane. Text and × colors are chosen against
/// that. Found by walking the tab's visual parents to the first panel or border whose
/// <c>Background</c> is an opaque <see cref="SolidColorBrush"/>; translucent ones on the way are
/// blended over it. Read-only and bounded (<see cref="MaxDepth"/> parents); never throws.
/// </summary>
public static class TabBackdrop
{
    /// <summary>The most visual parents the walk looks at.</summary>
    public const int MaxDepth = 32;

    /// <summary>The color behind <paramref name="tab"/> and where it came from; null when no parent has a solid background.</summary>
    public static (Rgb Color, string Source)? Behind(DependencyObject tab)
    {
        try
        {
            List<(Color Color, double Alpha)>? translucent = null;
            var node = VisualTreeHelper.GetParent(tab);
            for (var depth = 1; node is not null && depth <= MaxDepth; depth++, node = VisualTreeHelper.GetParent(node))
            {
                if (BackgroundOf(node) is not SolidColorBrush solid) continue;
                var alpha = solid.Color.A / 255.0 * Math.Clamp(solid.Opacity, 0, 1);
                if (alpha <= 0) continue;
                if (alpha < 1)
                {
                    (translucent ??= []).Add((solid.Color, alpha));
                    continue;
                }

                var c = new Rgb(solid.Color.R, solid.Color.G, solid.Color.B);
                if (translucent is not null)
                    for (var i = translucent.Count - 1; i >= 0; i--) // the nearest layer is on top
                        c = ColorMath.Blend(new Rgb(translucent[i].Color.R, translucent[i].Color.G, translucent[i].Color.B), translucent[i].Alpha, c);
                var name = node is FrameworkElement { Name.Length: > 0 } fe ? $" '{fe.Name}'" : "";
                return (c, string.Create(CultureInfo.InvariantCulture,
                    $"{node.GetType().Name}{name}, {depth} level{(depth == 1 ? "" : "s")} up{(translucent is null ? "" : $", with {translucent.Count} translucent layer(s) over it")}"));
            }
        }
        catch (Exception)
        {
            // A tree that cannot be walked: the caller falls back.
        }
        return null;
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

    /// <summary>
    /// The background a node paints: a panel's or a border's. A control's own <c>Background</c> is
    /// painted by its template's parts (met on the way up), so it is not counted twice.
    /// </summary>
    private static Brush? BackgroundOf(DependencyObject node) => node switch
    {
        Panel panel => panel.Background,
        Border border => border.Background,
        _ => null,
    };
}
