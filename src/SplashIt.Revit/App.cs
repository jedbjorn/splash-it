// The ribbon and icon handling is adapted from gantt-it src/GanttIt.Revit/App.cs
// (gantt-it commit aa83f35, MIT, same owner). The settings read, the colorizer and the
// file log are Splash-It's own.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SplashIt.Core.Logging;
using SplashIt.Core.Settings;
using SplashIt.Revit.Commands;
using SplashIt.Revit.Resources;
using SplashIt.Revit.Tabs;
using SplashIt.Revit.Theming;
using SplashIt.UI.Tabs;
using SplashIt.Core.Theming;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace SplashIt.Revit;

/// <summary>
/// Revit entry point (spec, App): adds the "Splash-It" panel to the built-in Add-Ins tab with
/// two buttons, Splash-It (the settings) and Group Tabs (spec #2), swaps their icons with the
/// theme, and at <c>ApplicationInitialized</c> reads the settings and starts the tab colorizer
/// when enabled. Nothing here can fail Revit's startup:
/// each step catches its own exceptions and logs them.
/// </summary>
public sealed class App : IExternalApplication
{
    public const string PanelName = "Splash-It";

    private UIControlledApplication? _application;
    private PushButton? _button;
    private PushButton? _groupButton;

    /// <summary>The add-in's log (<c>%LocalAppData%\SplashIt\logs</c>).</summary>
    internal static ILog Log { get; private set; } = NullLog.Instance;

    /// <summary>The tab colorizer, once <c>ApplicationInitialized</c> has created it.</summary>
    internal static DocumentTabColorizer? Colorizer { get; private set; }

    /// <summary>
    /// The open documents and the tab surface the colorizer was built with, once
    /// <c>ApplicationInitialized</c> has created them. Group Tabs (spec #2) reuses them for its
    /// tab discovery and document keys, whether coloring is on or off.
    /// </summary>
    internal static RevitDocuments? Documents { get; private set; }

    /// <inheritdoc cref="Documents"/>
    internal static RevitTabSurface? TabSurface { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        _application = application;
        try
        {
            Log = new FileLog(FileLog.DefaultFolder);
            var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            Log.Info($"Splash-It {version} starting in Revit {application.ControlledApplication.VersionNumber}.");

            // CreateRibbonPanel without a tab name targets the built-in Add-Ins tab.
            var panel = application.CreateRibbonPanel(PanelName);
            var data = new PushButtonData(
                "SplashIt.OpenSettings",
                RevitStrings.Ribbon_ButtonText,
                Assembly.GetExecutingAssembly().Location,
                typeof(OpenSettings).FullName)
            {
                ToolTip = RevitStrings.Ribbon_Tooltip,
                AvailabilityClassName = typeof(AlwaysAvailable).FullName,
            };
            _button = panel.AddItem(data) as PushButton;
            var group = new PushButtonData(
                "SplashIt.GroupTabs",
                RevitStrings.GroupTabs_ButtonText,
                Assembly.GetExecutingAssembly().Location,
                typeof(GroupTabs).FullName)
            {
                ToolTip = RevitStrings.GroupTabs_Tooltip,
                AvailabilityClassName = typeof(AlwaysAvailable).FullName,
            };
            _groupButton = panel.AddItem(group) as PushButton;
            OnTheme(RevitThemeDetector.Detect());
        }
        catch (Exception ex)
        {
            Log.Error("startup: adding the ribbon buttons failed (" + ex.GetType().Name + ").");
        }

        try
        {
            application.ThemeChanged += OnThemeChanged;
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
        }
        catch (Exception ex)
        {
            Log.Error("startup: subscribing to Revit events failed (" + ex.GetType().Name + ").");
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            Colorizer?.Stop();
        }
        catch (Exception ex)
        {
            Log.Error("shutdown: stopping the colorizer failed (" + ex.GetType().Name + ").");
        }
        try
        {
            application.ThemeChanged -= OnThemeChanged;
            application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
        }
        catch (Exception)
        {
            // Revit is closing; nothing left to unsubscribe from.
        }
        Log.Info("Splash-It shut down.");
        return Result.Succeeded;
    }

    private void OnApplicationInitialized(object? sender, ApplicationInitializedEventArgs e)
    {
        // A colorizer failure never fails Revit: everything here is caught and logged.
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var settings = SplashItSettings.Read(SplashItSettings.DefaultPath, Log.AsAction());
            var settingsMs = stopwatch.ElapsedMilliseconds;
            var revit = sender as RevitApplication;
            var documents = new RevitDocuments(() => revit, Log);
            var ui = _application!;
            var surface = new RevitTabSurface(() => ui.MainWindowHandle, documents, Log);
            Documents = documents;
            TabSurface = surface;
            var events = new RevitColorizerEvents(ui, documents, Log);
            Colorizer = new DocumentTabColorizer(surface, documents, events, new TabStyleFactory(), new PyRevitGuard(log: Log), Log);
            Colorizer.Apply(settings);
            stopwatch.Stop();
            Log.Info($"colorizer: start took {stopwatch.ElapsedMilliseconds} ms (settings read {settingsMs} ms; enabled {settings.Enabled}, status {Colorizer.Status}).");
        }
        catch (Exception ex)
        {
            Log.Error("colorizer: could not start (" + ex.GetType().Name + " in " + (ex.TargetSite?.Name ?? "?") + ").");
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        try
        {
            if (e.ThemeChangedType == ThemeType.UITheme) OnTheme(RevitThemeDetector.Detect());
        }
        catch (Exception ex)
        {
            Log.Error("theme: swapping the ribbon icons failed (" + ex.GetType().Name + ").");
        }
    }

    /// <summary>The ribbon icons follow the theme, and the settings window opens in it (SplashItTheme).</summary>
    private void OnTheme(UiTheme theme)
    {
        SplashIt.UI.Theming.SplashItTheme.Apply(theme);
        SetIcons(theme);
    }

    private void SetIcons(UiTheme theme)
    {
        SetIcons(_button, "splash", theme);
        SetIcons(_groupButton, "group", theme);
    }

    private static void SetIcons(PushButton? button, string name, UiTheme theme)
    {
        if (button is null) return;
        var suffix = theme == UiTheme.Dark ? "_dark" : "";
        // A missing dark icon falls back to the light one; with neither, the button has no icon.
        button.LargeImage = LoadIcon($"{name}_32{suffix}.png") ?? LoadIcon($"{name}_32.png");
        button.Image = LoadIcon($"{name}_16{suffix}.png") ?? LoadIcon($"{name}_16.png");
    }

    /// <summary>Loads an icon shipped next to the DLL; null when the file is missing.</summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var path = Path.Combine(directory, "Resources", "icons", fileName);
        if (!File.Exists(path)) return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
        image.EndInit();
        image.Freeze();
        return image;
    }
}
