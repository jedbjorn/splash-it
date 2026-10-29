// Adapted from gantt-it src/GanttIt.Revit/UI/Theming/GanttItTheme.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: SplashIt.UI has no Revit reference (spec #1, Project split), so detection is
// left out: the Revit side reads Revit's (or Windows') theme as gantt-it's Detect() does,
// and passes the result in through Apply.

using SplashIt.Core.Theming;

namespace SplashIt.UI.Theming;

/// <summary>
/// Which palette Splash-It's windows use: Revit's UI theme, as detected by the Revit side
/// (spec #1, Settings window; decision #4) and passed in through <see cref="Apply"/>. It is
/// read when a window loads Theme.xaml (each load builds a new <see cref="ThemePalette"/>),
/// so a window keeps the theme it opened with, as in gantt-it.
/// </summary>
public static class SplashItTheme
{
    /// <summary>The theme of the next window to open (and its dialogs). Light until the first <see cref="Apply"/>.</summary>
    public static UiTheme Current { get; private set; } = UiTheme.Light;

    public static bool IsDark => Current == UiTheme.Dark;

    /// <summary>
    /// Sets the theme for the windows opened from now on. The Revit side calls it with its
    /// detected theme before opening the settings window (gantt-it's rules: light for high
    /// contrast or anything unreadable). UI thread only.
    /// </summary>
    public static void Apply(UiTheme theme) => Current = theme;
}
