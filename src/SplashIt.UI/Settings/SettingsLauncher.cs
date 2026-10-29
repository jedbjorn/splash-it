using System.Windows.Interop;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Theming;

namespace SplashIt.UI.Settings;

/// <summary>
/// The settings window's entry point for the Revit side (Lane B's OpenSettings command): reads
/// the settings, then shows the window modally, owned by Revit's main window.
/// </summary>
public static class SettingsLauncher
{
    /// <summary>
    /// Shows the settings window and returns when it closes. Call on Revit's UI thread.
    /// </summary>
    /// <param name="owner">Revit's main window handle (<c>UIApplication.MainWindowHandle</c>); the window is modal to it.</param>
    /// <param name="host">The colorizer's status, apply and reset-assignments calls.</param>
    /// <param name="theme">Revit's detected theme; null keeps <see cref="SplashItTheme.Current"/>.</param>
    /// <param name="settingsPath">The settings file; null for <see cref="SplashItSettings.DefaultPath"/>.</param>
    /// <param name="log">Receives the settings read's and write's log lines (why defaults were used, write retries).</param>
    public static void Show(IntPtr owner, ISettingsHost host, UiTheme? theme = null, string? settingsPath = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (theme is { } t) SplashItTheme.Apply(t);

        var path = settingsPath ?? SplashItSettings.DefaultPath;
        var settings = SplashItSettings.Read(path, log);
        var dialogs = new DialogService();
        var model = new SettingsViewModel(settings, path, host, dialogs, log);
        var window = new SettingsWindow(model);
        dialogs.Owner = window;
        if (owner != IntPtr.Zero) _ = new WindowInteropHelper(window) { Owner = owner };
        window.ShowDialog();
    }
}
