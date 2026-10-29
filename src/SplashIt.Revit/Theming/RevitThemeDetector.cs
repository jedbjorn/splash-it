// Adapted from gantt-it src/GanttIt.Revit/UI/Theming/GanttItTheme.cs (gantt-it commit aa83f35,
// MIT, same owner). Changes: named RevitThemeDetector (SplashIt.UI's SplashItTheme, vendored by
// Lane C, holds the current theme; this class only detects it); the result is Core's UiTheme;
// the debug override reads SPLASHIT_THEME; no cached Current (App pushes each detection into
// SplashItTheme.Apply).

using System.Windows;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using SplashIt.Core.Theming;

namespace SplashIt.Revit.Theming;

/// <summary>
/// Revit's effective UI theme as plain light or dark (spec, decision #4). The ribbon icons,
/// the colorizer's text contrast and the settings window (through SplashItTheme.Apply) use it.
/// </summary>
public static class RevitThemeDetector
{
    /// <summary>
    /// Revit's effective UI theme. With Revit's "Use system setting" option on
    /// (<see cref="UIThemeManager.FollowSystemColorTheme"/>) the Windows app theme is read
    /// directly, so the result is right whether or not <see cref="UIThemeManager.CurrentTheme"/>
    /// resolves the system setting itself. Anything unreadable falls back to light, and so
    /// does Windows high contrast. Never throws.
    /// </summary>
    public static UiTheme Detect()
    {
#if DEBUG
        // Debug builds only: SPLASHIT_THEME=dark|light forces a theme for quick captures.
        switch (Environment.GetEnvironmentVariable("SPLASHIT_THEME")?.Trim().ToLowerInvariant())
        {
            case "dark": return UiTheme.Dark;
            case "light": return UiTheme.Light;
        }
#endif
        try
        {
            if (SystemParameters.HighContrast) return UiTheme.Light;
            if (UIThemeManager.FollowSystemColorTheme && WindowsAppTheme() is { } system) return system;
            return UIThemeManager.CurrentTheme == UITheme.Dark ? UiTheme.Dark : UiTheme.Light;
        }
        catch (Exception)
        {
            return UiTheme.Light;
        }
    }

    /// <summary>The Windows "app mode" (Settings › Personalization › Colors), or null when it can't be read.</summary>
    private static UiTheme? WindowsAppTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? (light == 0 ? UiTheme.Dark : UiTheme.Light) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
