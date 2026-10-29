using SplashIt.Core.Colors;
using SplashIt.Core.Settings;

namespace SplashIt.UI.Tabs;

/// <summary>
/// Revit's UI theme as plain light or dark. SplashIt.Revit detects it
/// (<c>SplashItTheme.Detect</c>, which reads Revit's <c>UIThemeManager</c>) and hands it
/// to SplashIt.UI as this value, so nothing here calls Revit (spec, Project split).
/// </summary>
public enum ThemeKind
{
    Light = 0,
    Dark,
}

public static class ThemeKinds
{
    /// <summary>
    /// The theme's surface token: <see cref="SplashItDefaults.LightSurface"/> (#FFFFFF) or
    /// <see cref="SplashItDefaults.DarkSurface"/> (#2A2F37). It is the backdrop for tab text
    /// contrast when a tab's own background is not a solid brush.
    /// </summary>
    public static Rgb Surface(this ThemeKind theme) =>
        theme == ThemeKind.Dark ? SplashItDefaults.DarkSurface : SplashItDefaults.LightSurface;
}
