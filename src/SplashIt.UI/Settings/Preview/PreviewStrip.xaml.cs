using System.Windows;
using System.Windows.Controls;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
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

    /// <summary>
    /// The style source. The placeholder until Lane B's tab style factory lands; then an
    /// adapter over that factory, so the preview and the colorizer share one implementation.
    /// </summary>
    internal static IPreviewTabStyler Styler { get; set; } = new PlaceholderTabStyler();

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

        var backdrop = SplashItTheme.IsDark ? SplashItDefaults.DarkSurface : SplashItDefaults.LightSurface;

        // The window may hold out-of-range values until Save rejects them; draw the nearest valid ones.
        var fill = Math.Clamp(s.ProjectFillOpacity, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);
        var width = Math.Clamp(s.FamilyBarWidth, SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth);
        var bar = Math.Clamp(s.FamilyBarOpacity, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);

        var tower = Styler.ProjectStyle(baseStyle, ColorAt(s.ProjectColors, 0), fill, backdrop);
        Tower1Tab.Style = tower;
        Tower2Tab.Style = tower;
        // The second project takes color 2; with a one-color list, colors repeat (spec, Allocation).
        AnnexTab.Style = Styler.ProjectStyle(baseStyle, ColorAt(s.ProjectColors, 1), fill, backdrop);
        FamilyTab.Style = Styler.FamilyStyle(baseStyle, ColorAt(s.FamilyColors, 0), width, bar);
        TemplateTab.Style = baseStyle;
    }

    private static Rgb ColorAt(IReadOnlyList<Rgb> colors, int index) =>
        colors.Count == 0 ? ColorMath.Black : colors[index % colors.Count];
}
