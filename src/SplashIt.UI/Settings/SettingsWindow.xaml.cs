// Adapted from gantt-it src/GanttIt.Revit/UI/Settings/SettingsWindow.xaml.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: namespaces; closing (the title bar's X, Esc, Close) asks first when there are unsaved changes.

using System.ComponentModel;
using System.Windows;
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

    /// <summary>Every way of closing goes through the unsaved-changes question (asked at most once).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Model.ConfirmClose()) e.Cancel = true;
        base.OnClosing(e);
    }
}
