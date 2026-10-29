using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Resources;
using SplashIt.UI.Settings;
using SplashIt.UI.Settings.Preview;
using SplashIt.UI.Swatches;
using SplashIt.UI.Theming;

namespace SplashIt.RuntimeTests.Settings;

/// <summary>
/// AC7: the real <see cref="SettingsWindow"/> and the color picker, rendered off-screen to PNG
/// in light and dark: every status state, a 12-color project list, the picker open on a chip,
/// an import with errors, and the preview strip at bar widths 1, 3 and 6 and opacities 30 % and
/// 100 %. Each test also checks what it rendered (the state shown, the theme's colors, the
/// controls present), so a broken render fails here as well as in review. The PNGs land in
/// TestResults/screenshots and CI uploads them as SplashIt-screenshots.
/// </summary>
[Collection(WpfThemeCollection.Name)]
public class SettingsWindowScreenshotTests
{
    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    public static TheoryData<SettingsStatusKind, UiTheme> StatusCases
    {
        get
        {
            var data = new TheoryData<SettingsStatusKind, UiTheme>();
            foreach (var kind in Enum.GetValues<SettingsStatusKind>())
            {
                data.Add(kind, UiTheme.Light);
                data.Add(kind, UiTheme.Dark);
            }
            return data;
        }
    }

    private static string Name(string what, UiTheme theme) => $"{what}-{theme.ToString().ToLowerInvariant()}";

    private static Color Palette(UiTheme theme, string key)
    {
        var c = UiPalette.For(theme)[key];
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>A settings window shown off-screen over a temp settings file, with a fake host and dialogs.</summary>
    private sealed class Shown : IDisposable
    {
        public Shown(UiTheme theme, SettingsStatus status, SplashItSettings? stored = null)
        {
            SplashItTheme.Apply(theme);
            Theme = theme;
            Folder = new TempFolder();
            SettingsPath = Folder.File("settings.json");
            (stored ?? SplashItSettings.Default).Write(SettingsPath);
            Host = new FakeHost { Status = status, SettingsPath = SettingsPath };
            Model = new SettingsViewModel(SplashItSettings.Read(SettingsPath), SettingsPath, Host, Dialogs);
            Window = new SettingsWindow(Model);
            WindowCapture.ShowOffScreen(Window);
        }

        public UiTheme Theme { get; }

        public TempFolder Folder { get; }

        public string SettingsPath { get; }

        public FakeHost Host { get; }

        public FakeDialogs Dialogs { get; } = new() { ConfirmResult = true };

        public SettingsViewModel Model { get; }

        public SettingsWindow Window { get; }

        public FrameworkElement Root => (FrameworkElement)VisualTreeHelper.GetChild(Window, 0);

        public BitmapSource Capture()
        {
            WindowCapture.DoEvents();
            Window.UpdateLayout();
            WindowCapture.DoEvents();
            return WindowCapture.Client(Window);
        }

        public T ById<T>(string automationId) where T : FrameworkElement =>
            WindowCapture.Descendants<T>(Window).Single(e => AutomationProperties.GetAutomationId(e) == automationId);

        public void Dispose()
        {
            Window.Close();
            Folder.Dispose();
        }
    }

    private static void Sta(Action action) => Screenshots.RunSta(() =>
    {
        try
        {
            action();
        }
        finally
        {
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });

    /// <summary>The checks every window render must pass: a real size, real content, and the theme's window color.</summary>
    private static void AssertWindowRender(BitmapSource bitmap, UiTheme theme)
    {
        Assert.InRange(bitmap.PixelWidth, 600, 700);
        Assert.InRange(bitmap.PixelHeight, 450, 1000);
        Assert.True(Screenshots.DistinctPixels(bitmap) > 300, "the render looks blank");
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, 2, 2));
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, bitmap.PixelWidth - 3, bitmap.PixelHeight - 3));
    }

    // ---- status states --------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void Window_RendersEachStatusState(SettingsStatusKind kind, UiTheme theme) => Sta(() =>
    {
        var reason = kind == SettingsStatusKind.Unavailable ? "the document tab bar was not found in Revit's window." : null;
        var stored = kind == SettingsStatusKind.Off ? SplashItSettings.Default with { Enabled = false } : SplashItSettings.Default;
        using var shown = new Shown(theme, new SettingsStatus(kind, reason), stored);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name($"settings-status-{kind.ToString().ToLowerInvariant()}", theme));

        AssertWindowRender(bitmap, theme);
        Assert.Equal(shown.Model.StatusText, shown.ById<TextBlock>("Settings.StatusText").Text);
        Assert.Equal(kind != SettingsStatusKind.Off, shown.ById<CheckBox>("Settings.Enabled").IsChecked);
        Assert.Equal(12, WindowCapture.Descendants<Button>(shown.Window)
            .Count(b => AutomationProperties.GetAutomationId(b).StartsWith("Settings.ProjectColors.Chip", StringComparison.Ordinal)));
        Assert.Equal(12, WindowCapture.Descendants<Button>(shown.Window)
            .Count(b => AutomationProperties.GetAutomationId(b).StartsWith("Settings.FamilyColors.Chip", StringComparison.Ordinal)));
        Assert.Equal(kind == SettingsStatusKind.Active, shown.ById<Button>("Settings.ResetAssignments").IsEnabled);
        Assert.Equal("100%", shown.ById<TextBlock>("Settings.ProjectFillOpacityValue").Text);
        Assert.Equal("3 px", shown.ById<TextBlock>("Settings.FamilyBarWidthValue").Text);
    });

    // ---- a 12-color project list --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_TwelveProjectColors_DisablesAdd(UiTheme theme) => Sta(() =>
    {
        // Twelve project colors in a custom order, four family colors, and non-default sliders.
        var project = SplashItDefaults.ProjectColors.Reverse().ToArray();
        var stored = SplashItSettings.Default with
        {
            ProjectColors = project,
            FamilyColors = SplashItDefaults.FamilyColors.Take(4).ToArray(),
            ProjectFillOpacity = 60,
            FamilyBarWidth = 5,
            FamilyBarOpacity = 80,
        };
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active), stored);
        shown.Model.ProjectColors.Selected = shown.Model.ProjectColors.Chips[11];

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-project-12-colors", theme));

        AssertWindowRender(bitmap, theme);
        Assert.False(shown.Model.ProjectColors.AddCommand.CanExecute(null));
        Assert.True(shown.Model.FamilyColors.AddCommand.CanExecute(null));
        Assert.Equal("12 of 12", shown.Model.ProjectColors.CountText);
        Assert.Equal("60%", shown.ById<TextBlock>("Settings.ProjectFillOpacityValue").Text);
        Assert.Equal("5 px", shown.ById<TextBlock>("Settings.FamilyBarWidthValue").Text);
        Assert.Equal("80%", shown.ById<TextBlock>("Settings.FamilyBarOpacityValue").Text);

        // Each project chip is drawn in its color: sample the middle of chip 1's left edge area.
        var chip = shown.ById<Button>("Settings.ProjectColors.Chip1");
        var at = chip.TranslatePoint(new Point(8, chip.ActualHeight / 2), shown.Root);
        Assert.Equal(ToColor(project[0]), Screenshots.PixelAt(bitmap, (int)at.X, (int)at.Y));
    });

    // ---- the color picker open on a chip ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void ColorPicker_OpenOnAChip(UiTheme theme) => Sta(() =>
    {
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        var chipModel = shown.Model.ProjectColors.Chips[2];
        shown.Model.ProjectColors.Selected = chipModel;
        foreach (var recent in new[] { "#2E86AB", "#C0392B", "#7D3C98" }) PickerSession.RecentColors.Add(Rgb.Parse(recent));

        var window = shown.Capture();
        var picker = new ColorPickerDialog(chipModel.Color, SplashItDefaults.ProjectColors, chipModel.Name, null, SystemParameters.WorkArea)
        {
            Owner = shown.Window,
        };
        WindowCapture.ShowOffScreen(picker);
        try
        {
            var pickerImage = WindowCapture.Client(picker);
            Screenshots.Save(pickerImage, Name("color-picker", theme));

            var chip = shown.ById<Button>("Settings.ProjectColors.Chip3");
            var anchor = chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight + 6), shown.Root);
            var composed = WindowCapture.Compose(window, pickerImage, new Point(Math.Round(anchor.X) - 20, Math.Round(anchor.Y)),
                Palette(theme, UiPalette.StrongBorder));
            Screenshots.Save(composed, Name("settings-color-picker", theme));

            Assert.Equal("Project color 3", picker.Title);
            Assert.True(Screenshots.DistinctPixels(pickerImage) > 50, "the picker looks blank");
            Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(pickerImage, 1, 1));
            var swatches = WindowCapture.Descendants<Button>(picker)
                .Count(b => AutomationProperties.GetAutomationId(b).StartsWith("ColorPicker.Swatch", StringComparison.Ordinal));
            var recents = WindowCapture.Descendants<Button>(picker)
                .Count(b => AutomationProperties.GetAutomationId(b).StartsWith("ColorPicker.Recent", StringComparison.Ordinal));
            Assert.Equal(12, swatches);
            Assert.True(recents >= 3, $"{recents} recent colors");
            var hex = WindowCapture.Descendants<TextBox>(picker).Single(b => AutomationProperties.GetAutomationId(b) == "ColorPicker.Hex");
            Assert.Equal(chipModel.Hex, hex.Text);
        }
        finally
        {
            picker.Close();
        }
    });

    // ---- import errors, import notice, invalid save -------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_ImportWithErrors_ListsThemAll(UiTheme theme) => Sta(() =>
    {
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        var bad = shown.Folder.File("from-a-colleague.json");
        File.WriteAllText(bad, """
            {
              "format": "splash-it-settings",
              "schemaVersion": 1,
              "projectColors": ["#4F8EF7", "orange"],
              "familyColors": [],
              "projectFillOpacity": 5,
              "familyBarWidth": 12,
              "familyBarOpacity": 100
            }
            """);
        shown.Dialogs.OpenResult = bad;
        shown.Model.ImportCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-import-errors", theme));

        AssertWindowRender(bitmap, theme);
        var expected = SettingsExchange.ImportFile(bad, SplashItSettings.Default).Errors;
        Assert.True(expected.Count >= 4);
        Assert.Equal(expected, shown.Model.Errors);
        Assert.Equal(UiStrings.Settings_ImportFailed("from-a-colleague.json"), shown.ById<TextBlock>("Settings.ErrorHeading").Text);
        var listed = shown.ById<ItemsControl>("Settings.Errors");
        Assert.Equal(expected.Count, listed.Items.Count);
        Assert.False(shown.Model.IsDirty);
    });

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_Imported_ShowsSaveToApply(UiTheme theme) => Sta(() =>
    {
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        var team = shown.Folder.File("team-colors.json");
        SettingsExchange.ExportFile(SplashItSettings.Default with
        {
            ProjectColors = SplashItDefaults.ProjectColors.Skip(6).ToArray(),
            FamilyColors = SplashItDefaults.FamilyColors.Skip(3).Take(5).ToArray(),
            ProjectFillOpacity = 40,
            FamilyBarWidth = 6,
            FamilyBarOpacity = 70,
        }, team);
        shown.Dialogs.OpenResult = team;
        shown.Model.ImportCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-imported", theme));

        AssertWindowRender(bitmap, theme);
        Assert.Equal("Imported from team-colors.json; Save to apply.", shown.ById<TextBlock>("Settings.Notice").Text);
        Assert.Equal("40%", shown.ById<TextBlock>("Settings.ProjectFillOpacityValue").Text);
        Assert.Equal(6, shown.Model.ProjectColors.Count);
        Assert.True(shown.Model.IsDirty);
    });

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_InvalidSave_ShowsTheErrorsInline(UiTheme theme) => Sta(() =>
    {
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        shown.Model.ProjectFillOpacity = 5;
        shown.Model.FamilyBarWidth = 8;
        var before = File.ReadAllBytes(shown.SettingsPath);
        shown.Model.SaveCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-invalid-save", theme));

        AssertWindowRender(bitmap, theme);
        Assert.Equal(before, File.ReadAllBytes(shown.SettingsPath));
        Assert.Empty(shown.Host.Applied);
        Assert.Equal(2, shown.ById<ItemsControl>("Settings.Errors").Items.Count);
    });

    // ---- the discard prompt -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void DiscardPrompt_Renders(UiTheme theme) => Sta(() =>
    {
        SplashItTheme.Apply(theme);
        var dialog = new MessageDialog(new MessageDialogModel(UiStrings.Settings_DiscardTitle, UiStrings.Settings_DiscardTitle,
            UiStrings.Settings_DiscardMessage, [], UiStrings.Settings_Discard, HasCancel: true, Danger: true), MessageDialog.DiscardChangesId);
        WindowCapture.ShowOffScreen(dialog);
        try
        {
            var bitmap = WindowCapture.Client(dialog);
            Screenshots.Save(bitmap, Name("discard-changes", theme));
            Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, 1, 1));
            Assert.True(Screenshots.DistinctPixels(bitmap) > 30, "the dialog looks blank");
        }
        finally
        {
            dialog.Close();
        }
    });

    // ---- the preview strip at bar widths 1, 3, 6 and opacities 30 % and 100 % --------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void PreviewStrip_AtEachWidthAndOpacity(UiTheme theme) => Sta(() =>
    {
        SplashItTheme.Apply(theme);
        var text = new SolidColorBrush(Palette(theme, UiPalette.Text));
        var root = new StackPanel { Background = new SolidColorBrush(Palette(theme, UiPalette.Surface)), UseLayoutRounding = true };
        var strips = new List<(PreviewStrip Strip, int Width, int Opacity)>();
        foreach (var width in new[] { 1, 3, 6 })
        {
            foreach (var opacity in new[] { 30, 100 })
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"Bar width {width} px · bar opacity {opacity}% · fill opacity {opacity}%",
                    Foreground = text,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Margin = new Thickness(12, 10, 12, 4),
                });
                var strip = new PreviewStrip
                {
                    Settings = SplashItSettings.Default with { FamilyBarWidth = width, FamilyBarOpacity = opacity, ProjectFillOpacity = opacity },
                    Margin = new Thickness(12, 0, 12, 0),
                };
                root.Children.Add(strip);
                strips.Add((strip, width, opacity));
            }
        }
        root.Children.Add(new Border { Height = 12 });

        var bitmap = Screenshots.Render(root);
        Screenshots.Save(bitmap, Name("preview-matrix", theme));
        Assert.True(Screenshots.DistinctPixels(bitmap) > 200, "the render looks blank");

        var surface = theme == UiTheme.Dark ? SplashItDefaults.DarkSurface : SplashItDefaults.LightSurface;
        var tabBackground = UiPalette.For(theme)[UiPalette.Hover];
        var tabRgb = new Rgb(tabBackground.R, tabBackground.G, tabBackground.B);
        var project1 = SplashItDefaults.ProjectColors[0];
        var project2 = SplashItDefaults.ProjectColors[1];
        var family1 = SplashItDefaults.FamilyColors[0];

        foreach (var (strip, width, opacity) in strips)
        {
            TabItem Tab(string id) => WindowCapture.Descendants<TabItem>(strip).Single(t => AutomationProperties.GetAutomationId(t) == id);
            Color At(TabItem tab, double x, double y)
            {
                var p = tab.TranslatePoint(new Point(x, y), root);
                return Screenshots.PixelAt(bitmap, (int)Math.Floor(p.X), (int)Math.Floor(p.Y));
            }

            var tower1 = Tab("Settings.Preview.Tower1");
            var tower2 = Tab("Settings.Preview.Tower2");
            var annex = Tab("Settings.Preview.Annex");
            var family = Tab("Settings.Preview.Family");
            var template = Tab("Settings.Preview.Template");
            Assert.True(tower1.IsSelected);

            // The selected project tab shows its color at full opacity (below its top accent, left of its text).
            AssertNear(ToColor(project1), At(tower1, 4, tower1.ActualHeight - 3), $"Tower selected, {width}/{opacity}");
            // Unselected tabs of the same project: the fill opacity over the theme surface.
            AssertNear(ToColor(ColorMath.Blend(project1, opacity / 100.0, surface)), At(tower2, 4, tower2.ActualHeight - 3),
                $"Tower unselected, {width}/{opacity}");
            AssertNear(ToColor(ColorMath.Blend(project2, opacity / 100.0, surface)), At(annex, 4, annex.ActualHeight - 3),
                $"Annex, {width}/{opacity}");
            // The family bar: inside it, the family color at the bar opacity over the tab; just right of it, the tab.
            AssertNear(ToColor(ColorMath.Blend(family1, opacity / 100.0, tabRgb)), At(family, width / 2.0, family.ActualHeight / 2),
                $"Family bar, {width}/{opacity}");
            AssertNear(ToColor(tabRgb), At(family, width + 3, family.ActualHeight - 3), $"Family tab, {width}/{opacity}");
            // The template stays native.
            AssertNear(ToColor(tabRgb), At(template, 4, template.ActualHeight - 3), $"Template, {width}/{opacity}");
        }
    });

    private static void AssertNear(Color expected, Color actual, string what)
    {
        var off = Math.Max(Math.Abs(expected.R - actual.R), Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B)));
        Assert.True(off <= 3, $"{what}: expected {expected}, got {actual}");
    }

    // ---- window behavior ------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void TitleBar_IsDark_OnlyInTheDarkTheme(UiTheme theme) => Sta(() =>
    {
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        var hwnd = new WindowInteropHelper(shown.Window).Handle;
        Assert.NotEqual(IntPtr.Zero, hwnd);
        var value = 0;
        if (DwmGetWindowAttribute(hwnd, 20, out value, sizeof(int)) != 0)
            Assert.Equal(0, DwmGetWindowAttribute(hwnd, 19, out value, sizeof(int)));
        Assert.Equal(theme == UiTheme.Dark ? 1 : 0, value);
    });

    [Fact]
    public void ClosingTheWindow_WithChanges_AsksFirst_AndStaysOpenWhenDeclined() => Sta(() =>
    {
        using var shown = new Shown(UiTheme.Light, new SettingsStatus(SettingsStatusKind.Active));
        shown.Model.ProjectFillOpacity = 30;
        shown.Dialogs.ConfirmResult = false;

        shown.Window.Close(); // the title bar's X
        WindowCapture.DoEvents();

        Assert.True(shown.Window.IsVisible);
        Assert.Single(shown.Dialogs.ConfirmCalls);
        shown.Dialogs.ConfirmResult = true;
    });

    [Fact]
    public void Launcher_ReadsTheSettings_AppliesTheTheme_AndShowsTheWindowModally() => Sta(() =>
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        (SplashItSettings.Default with { ProjectFillOpacity = 42, FamilyBarWidth = 2 }).Write(path);
        var host = new FakeHost { Status = new SettingsStatus(SettingsStatusKind.BlockedByPyRevit) };
        SettingsViewModel? seen = null;
        var themeSeen = UiTheme.Light;
        var wasModal = false;

        // Runs inside ShowDialog's loop: look at the window once it has loaded, then close it.
        var attempts = 0;
        void InspectAndClose()
        {
            var window = PresentationSource.CurrentSources.OfType<HwndSource>()
                .Select(s => s.RootVisual).OfType<SettingsWindow>().FirstOrDefault(w => w.IsLoaded);
            if (window is null)
            {
                if (++attempts > 500) throw new TimeoutException("The settings window never loaded.");
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, InspectAndClose);
                return;
            }
            seen = window.Model;
            themeSeen = SplashItTheme.Current;
            wasModal = ComponentDispatcher.IsThreadModal;
            window.Close();
        }
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, InspectAndClose);
        SettingsLauncher.Show(IntPtr.Zero, host, UiTheme.Dark, path);

        Assert.NotNull(seen);
        Assert.Equal(42, seen!.ProjectFillOpacity);
        Assert.Equal(2, seen.FamilyBarWidth);
        Assert.Equal(SettingsStatusKind.BlockedByPyRevit, seen.StatusKind);
        Assert.Equal(UiTheme.Dark, themeSeen);
        Assert.True(wasModal);
        Assert.Empty(host.Applied);
    });

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
