// Adapted from gantt-it src/GanttIt.Revit/UI/Theming/GanttItTheme.cs (gantt-it commit aa83f35,
// MIT, same owner). Changes: the result is SplashIt.UI's plain ThemeKind (light or dark) rather
// than gantt-it's UiTheme; the debug override reads SPLASHIT_THEME; no cached Current (callers
// detect when they need it). Lane C vendors the rest of gantt-it's theme and takes only this
// light/dark value from here.

using System.Windows;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Theming;

/// <summary>
/// Revit's effective UI theme as plain light or dark (spec, decision #4). The ribbon icons,
/// the colorizer's text contrast and (Lane C) the settings window read it.
/// </summary>
public static class SplashItTheme
{
    /// <summary>
    /// Revit's effective UI theme. With Revit's "Use system setting" option on
    /// (<see cref="UIThemeManager.FollowSystemColorTheme"/>) the Windows app theme is read
    /// directly, so the result is right whether or not <see cref="UIThemeManager.CurrentTheme"/>
    /// resolves the system setting itself. Anything unreadable falls back to light, and so
    /// does Windows high contrast. Never throws.
    /// </summary>
    public static ThemeKind Detect()
    {
#if DEBUG
        // Debug builds only: SPLASHIT_THEME=dark|light forces a theme for quick captures.
        switch (Environment.GetEnvironmentVariable("SPLASHIT_THEME")?.Trim().ToLowerInvariant())
        {
            case "dark": return ThemeKind.Dark;
            case "light": return ThemeKind.Light;
        }
#endif
        try
        {
            if (SystemParameters.HighContrast) return ThemeKind.Light;
            if (UIThemeManager.FollowSystemColorTheme && WindowsAppTheme() is { } system) return system;
            return UIThemeManager.CurrentTheme == UITheme.Dark ? ThemeKind.Dark : ThemeKind.Light;
        }
        catch (Exception)
        {
            return ThemeKind.Light;
        }
    }

    /// <summary>The Windows "app mode" (Settings › Personalization › Colors), or null when it can't be read.</summary>
    private static ThemeKind? WindowsAppTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? (light == 0 ? ThemeKind.Dark : ThemeKind.Light) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
