// Adapted from gantt-it src/GanttIt.Revit/UI/Settings/SettingsWindow.xaml.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: namespaces; closing (the title bar's X, Esc, Close) asks first when there are unsaved
// changes; the height is capped at the monitor's work area; chips select on focus and right-click.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Theming;

namespace SplashIt.UI.Settings;

/// <summary>The Splash-It settings window (spec #1, Settings window). All behaviour is in <see cref="SettingsViewModel"/>.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        DataContext = model;
        Model = model;
        model.CloseRequested += () =>
        {
            if (IsVisible) Close();
        };
    }

    public SettingsViewModel Model { get; }

    /// <summary>
    /// The work area (DIPs) the window must fit in; null reads the monitor the window opens on
    /// (<see cref="MonitorWorkArea"/>). Tests set it to stand in for a small screen.
    /// </summary>
    public Rect? WorkArea { get; set; }

    /// <summary>
    /// Caps the height at the work area, so on a short screen (1080 p at 150 %, a 768 px laptop)
    /// the settings panel scrolls and the title bar and buttons stay on screen.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var area = WorkArea ?? MonitorWorkArea.Of(this);
        if (area.Height > 0) MaxHeight = area.Height;
        if (WindowStartupLocation == WindowStartupLocation.CenterOwner)
            Loaded += (_, _) =>
            {
                // Centered on a tall owner, the title bar could start above the work area.
                if (Top < area.Top) Top = area.Top;
                if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, area.Bottom - ActualHeight);
            };
    }

    /// <summary>Every way of closing goes through the unsaved-changes question (asked at most once).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Model.ConfirmClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>Tabbing to a chip selects it (for Remove and Move) without opening the picker; Space or Enter edits it.</summary>
    private void OnChipFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColorChipViewModel chip }) chip.Select();
    }

    /// <summary>A right-click selects a chip without opening the picker; a left click edits it.</summary>
    private void OnChipRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ColorChipViewModel chip }) return;
        chip.Select();
        if (sender is UIElement element) element.Focus();
        e.Handled = true;
    }
}
