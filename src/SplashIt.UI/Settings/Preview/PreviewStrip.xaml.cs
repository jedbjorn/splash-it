using System.Windows;
using System.Windows.Controls;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;
using SplashIt.UI.Resources;
using SplashIt.UI.Theming;

namespace SplashIt.UI.Settings.Preview;

/// <summary>
/// A sample tab strip that shows the settings as tabs: Tower (two views, the first selected)
/// takes project color 1, Annex project color 2, the Door family family color 1, and the
/// template stays native. Restyles whenever <see cref="Settings"/> changes, so it follows the
/// window's unsaved values live; with coloring off, every tab is native.
/// </summary>
public partial class PreviewStrip : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(SplashItSettings), typeof(PreviewStrip),
        new PropertyMetadata(null, (d, _) => ((PreviewStrip)d).Restyle()));

    public PreviewStrip()
    {
        InitializeComponent();
        Tower1Tab.Header = UiStrings.Preview_Tower1;
        Tower2Tab.Header = UiStrings.Preview_Tower2;
        AnnexTab.Header = UiStrings.Preview_Annex;
        FamilyTab.Header = UiStrings.Preview_Family;
        TemplateTab.Header = UiStrings.Preview_Template;
        Strip.SelectedItem = Tower1Tab;
        Restyle();
    }

    /// <summary>The style source: the colorizer's <see cref="SplashIt.UI.Tabs.TabStyleFactory"/>, so the preview is what the tabs get.</summary>
    internal static IPreviewTabStyler Styler { get; set; } = new FactoryTabStyler();

    /// <summary>The values to show; null shows the defaults.</summary>
    public SplashItSettings? Settings
    {
        get => (SplashItSettings?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    private void Restyle()
    {
        var s = Settings ?? SplashItSettings.Default;
        var baseStyle = (Style)FindResource("PreviewTabBase");

        // Off: what the user will see is Revit's own tabs.
        OffNote.Text = UiStrings.Preview_Off;
        OffNote.Visibility = s.Enabled ? Visibility.Collapsed : Visibility.Visible;
        if (!s.Enabled)
        {
            foreach (var tab in new[] { Tower1Tab, Tower2Tab, AnnexTab, FamilyTab, TemplateTab }) tab.Style = baseStyle;
            return;
        }

        // What the tabs sit on: the strip's window color. Text contrast is measured against it.
        var well = UiPalette.For(SplashItTheme.Current)[UiPalette.Window];
        var backdrop = new Rgb(well.R, well.G, well.B);

        // The window may hold out-of-range values until Save rejects them; draw the nearest valid ones.
        var fill = Math.Clamp(s.ProjectFillOpacity, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);
        var share = Math.Clamp(s.ProjectUnselectedOpacity, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);
        var width = Math.Clamp(s.FamilyBarWidth, SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth);
        var bar = Math.Clamp(s.FamilyBarOpacity, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);

        var tower = Styler.ProjectStyle(baseStyle, ColorAt(s.ProjectColors, 0), fill, share, backdrop);
        Tower1Tab.Style = tower;
        Tower2Tab.Style = tower;
        // The second project takes color 2; with a one-color list, colors repeat (spec, Allocation).
        AnnexTab.Style = Styler.ProjectStyle(baseStyle, ColorAt(s.ProjectColors, 1), fill, share, backdrop);
        FamilyTab.Style = Styler.FamilyStyle(baseStyle, ColorAt(s.FamilyColors, 0), width, bar);
        TemplateTab.Style = baseStyle;
    }

    private static Rgb ColorAt(IReadOnlyList<Rgb> colors, int index) =>
        colors.Count == 0 ? ColorMath.Black : colors[index % colors.Count];
}
