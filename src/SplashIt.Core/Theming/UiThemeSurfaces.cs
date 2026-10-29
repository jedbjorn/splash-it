using SplashIt.Core.Colors;
using SplashIt.Core.Settings;

namespace SplashIt.Core.Theming;

/// <summary>The theme surface tokens as <see cref="Rgb"/>, for tab text contrast (spec, Shapes and settings).</summary>
public static class UiThemeSurfaces
{
    /// <summary>
    /// The theme's surface token: <see cref="SplashItDefaults.LightSurface"/> (#FFFFFF) or
    /// <see cref="SplashItDefaults.DarkSurface"/> (#2A2F37), the same values as
    /// <see cref="UiPalette"/>'s <c>SurfaceBrush</c>. It is the backdrop for tab text contrast
    /// when a tab's own background is not a solid brush.
    /// </summary>
    public static Rgb Surface(this UiTheme theme) =>
        theme == UiTheme.Dark ? SplashItDefaults.DarkSurface : SplashItDefaults.LightSurface;
}
