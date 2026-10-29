using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        /// <param name="workArea">Stands in for a small screen; null uses the monitor's.</param>
        /// <param name="realDialogs">Use the real <see cref="DialogService"/> (owned by the window) instead of the fake.</param>
        public Shown(UiTheme theme, SettingsStatus status, SplashItSettings? stored = null, Rect? workArea = null, bool realDialogs = false)
        {
            SplashItTheme.Apply(theme);
            Theme = theme;
            Folder = new TempFolder();
            SettingsPath = Folder.File("settings.json");
            (stored ?? SplashItSettings.Default).Write(SettingsPath);
            Host = new FakeHost { Status = status, SettingsPath = SettingsPath };
            var real = realDialogs ? new DialogService() : null;
            Model = new SettingsViewModel(SplashItSettings.Read(SettingsPath), SettingsPath, Host, (IDialogService?)real ?? Dialogs);
            Window = new SettingsWindow(Model) { WorkArea = workArea };
            if (real is not null) real.Owner = Window;
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
        // Fits a 1366 x 768 laptop (and the 1024 x 768 CI desktop) with its title bar, errors included.
        Assert.InRange(bitmap.PixelHeight, 450, 690);
        Assert.True(Screenshots.DistinctPixels(bitmap) > 300, "the render looks blank");
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, 2, 2));
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, bitmap.PixelWidth - 3, bitmap.PixelHeight - 3));
    }

    private static Color At(BitmapSource bitmap, FrameworkElement element, FrameworkElement root, double x, double y)
    {
        var p = element.TranslatePoint(new Point(x, y), root);
        return Screenshots.PixelAt(bitmap, (int)Math.Floor(p.X), (int)Math.Floor(p.Y));
    }

    private static int Distance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>
    /// Every chip of both lists shows its number: the label's text is "n", and the rendered chip
    /// has label-colored pixels in its middle (where the number is drawn) on an otherwise solid fill.
    /// </summary>
    private static void AssertChipNumbers(Shown shown, BitmapSource bitmap)
    {
        foreach (var list in new[] { shown.Model.ProjectColors, shown.Model.FamilyColors })
        {
            foreach (var chip in list.Chips)
            {
                var button = shown.ById<Button>(chip.AutomationId);
                var label = (TextBlock)button.Template.FindName("Label", button);
                Assert.Equal(chip.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), label.Text);
                Assert.Equal(Visibility.Visible, label.Visibility);

                var fill = ToColor(chip.Color);
                // Below the number, the chip's face is plain fill.
                Assert.True(Distance(fill, At(bitmap, button, shown.Root, 8, 24)) <= 3, $"{chip.Name}: the chip is not its color");
                // The middle carries the number: count pixels far from the fill.
                var inked = 0;
                for (var y = 9; y <= 21; y++)
                    for (var x = 9; x <= 21; x++)
                        if (Distance(fill, At(bitmap, button, shown.Root, x, y)) > 60) inked++;
                Assert.True(inked >= 4, $"{chip.Name}: no number drawn ({inked} label pixels)");
            }
        }
    }

    /// <summary>The ring around the selected chip is the text color; other chips have none.</summary>
    private static void AssertSelectionRing(Shown shown, BitmapSource bitmap, ColorListViewModel list)
    {
        var ring = Palette(shown.Theme, UiPalette.Text);
        var surface = Palette(shown.Theme, UiPalette.Surface);
        foreach (var chip in list.Chips)
        {
            var button = shown.ById<Button>(chip.AutomationId);
            var edge = At(bitmap, button, shown.Root, 1, button.ActualHeight / 2);
            if (chip.IsSelected) Assert.True(Distance(ring, edge) <= 3, $"{chip.Name}: selection ring is {edge}, expected {ring}");
            else Assert.True(Distance(surface, edge) <= 3, $"{chip.Name}: unselected chip has a ring ({edge})");
        }
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
        Assert.Equal("50%", shown.ById<TextBlock>("Settings.ProjectUnselectedOpacityValue").Text);
        Assert.Equal("3 px", shown.ById<TextBlock>("Settings.FamilyBarWidthValue").Text);
        AssertChipNumbers(shown, bitmap);
        // Chip 1 (blue in the project list) is selected: its ring must still read.
        AssertSelectionRing(shown, bitmap, shown.Model.ProjectColors);
        AssertSelectionRing(shown, bitmap, shown.Model.FamilyColors);

        // The preview: colored tabs when on; native tabs and a note when "Color document tabs" is off.
        var note = shown.ById<TextBlock>("Settings.Preview.OffNote");
        var tower2 = shown.ById<TabItem>("Settings.Preview.Tower2");
        var tower2Pixel = At(bitmap, tower2, shown.Root, 4, tower2.ActualHeight - 3);
        if (kind == SettingsStatusKind.Off)
        {
            Assert.Equal(Visibility.Visible, note.Visibility);
            Assert.Equal(UiStrings.Preview_Off, note.Text);
            Assert.True(Distance(Palette(theme, UiPalette.Hover), tower2Pixel) <= 3, $"off preview tab is {tower2Pixel}");
        }
        else
        {
            Assert.Equal(Visibility.Collapsed, note.Visibility);
            // The project's unselected tab: fill (100 %) × the unselected share (50 %) over what the
            // tabs sit on (the strip's window color), drawn by the colorizer's TabStyleFactory.
            var well = Palette(theme, UiPalette.Window);
            var expected = ToColor(ColorMath.Blend(SplashItDefaults.ProjectColors[0], 0.5, new Rgb(well.R, well.G, well.B)));
            Assert.True(Distance(expected, tower2Pixel) <= 3, $"preview tab is {tower2Pixel}, expected {expected}");
        }
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
            ProjectUnselectedOpacity = 80,
        };
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active), stored);

        // Select project chip 12 with a right-click and family chip 3 with keyboard focus: no picker opens.
        var chip12 = shown.ById<Button>("Settings.ProjectColors.Chip12");
        chip12.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent,
        });
        var family3 = shown.ById<Button>("Settings.FamilyColors.Chip3");
        family3.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, family3)
        {
            RoutedEvent = Keyboard.GotKeyboardFocusEvent,
        });
        Assert.Same(shown.Model.ProjectColors.Chips[11], shown.Model.ProjectColors.Selected);
        Assert.Same(shown.Model.FamilyColors.Chips[2], shown.Model.FamilyColors.Selected);
        Assert.Empty(shown.Dialogs.PickCalls);
        Assert.False(shown.Model.ProjectColors.MoveRightCommand.CanExecute(null));

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-project-12-colors", theme));

        AssertWindowRender(bitmap, theme);
        Assert.False(shown.Model.ProjectColors.AddCommand.CanExecute(null));
        Assert.True(shown.Model.FamilyColors.AddCommand.CanExecute(null));
        Assert.Equal("12 of 12", shown.Model.ProjectColors.CountText);
        Assert.Equal("60%", shown.ById<TextBlock>("Settings.ProjectFillOpacityValue").Text);
        Assert.Equal("5 px", shown.ById<TextBlock>("Settings.FamilyBarWidthValue").Text);
        Assert.Equal("80%", shown.ById<TextBlock>("Settings.FamilyBarOpacityValue").Text);
        Assert.Equal("80%", shown.ById<TextBlock>("Settings.ProjectUnselectedOpacityValue").Text);

        // Each chip is drawn in its color with its number (1 to 12) in the middle.
        AssertChipNumbers(shown, bitmap);
        AssertSelectionRing(shown, bitmap, shown.Model.ProjectColors);
        AssertSelectionRing(shown, bitmap, shown.Model.FamilyColors);
    });

    // ---- the color picker open on a chip ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void ColorPicker_OpenOnAChip(UiTheme theme) => Sta(() =>
    {
        // The real chain: the chip's EditCommand, the real DialogService, the real ColorPickerDialog.
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active), realDialogs: true);
        foreach (var recent in new[] { "#2E86AB", "#C0392B", "#7D3C98" }) PickerSession.RecentColors.Add(Rgb.Parse(recent));

        var project3 = shown.Model.ProjectColors.Chips[2];
        var projectPicker = OpenPickerFrom(shown.Model.ProjectColors, project3);
        Assert.Equal("Project color 3", projectPicker.Title);
        Assert.Equal(SplashItDefaults.ProjectColors, projectPicker.Swatches);
        Assert.Equal(project3.Hex, projectPicker.Hex);
        Assert.True(projectPicker.Recent >= 3, $"{projectPicker.Recent} recent colors");
        Assert.True(Screenshots.DistinctPixels(projectPicker.Image) > 50, "the picker looks blank");
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(projectPicker.Image, 1, 1));
        Assert.Same(project3, shown.Model.ProjectColors.Selected);
        Assert.False(shown.Model.IsDirty); // cancelled
        // The window as it stands with the picker open: chip 3 selected (the click selected it).
        var window = shown.Capture();

        // The family list's picker offers the family palette.
        var family2 = shown.Model.FamilyColors.Chips[1];
        var familyPicker = OpenPickerFrom(shown.Model.FamilyColors, family2);
        Assert.Equal("Family color 2", familyPicker.Title);
        Assert.Equal(SplashItDefaults.FamilyColors, familyPicker.Swatches);

        Screenshots.Save(projectPicker.Image, Name("color-picker", theme));
        // A composite: both are real windows, rendered off-screen, so the picker is drawn under
        // project chip 3 (in Revit it opens at the pointer, beside the chip clicked).
        var chip = shown.ById<Button>("Settings.ProjectColors.Chip3");
        var anchor = chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight + 6), shown.Root);
        var composed = WindowCapture.Compose(window, projectPicker.Image, new Point(Math.Round(anchor.X) - 20, Math.Round(anchor.Y)),
            Palette(theme, UiPalette.StrongBorder));
        Screenshots.Save(composed, Name("settings-color-picker", theme));
    });

    private sealed record PickerSeen(string Title, IReadOnlyList<Rgb> Swatches, string Hex, int Recent, BitmapSource Image);

    /// <summary>Clicks <paramref name="chip"/> (its EditCommand), inspects and renders the modal picker it opens, then cancels it.</summary>
    private static PickerSeen OpenPickerFrom(ColorListViewModel list, ColorChipViewModel chip)
    {
        PickerSeen? seen = null;
        var attempts = 0;
        void InspectAndCancel()
        {
            var picker = PresentationSource.CurrentSources.OfType<HwndSource>()
                .Select(s => s.RootVisual).OfType<ColorPickerDialog>().FirstOrDefault(w => w.IsLoaded);
            if (picker is null)
            {
                if (++attempts > 500) throw new TimeoutException("The color picker never opened.");
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, InspectAndCancel);
                return;
            }
            Button[] Buttons(string prefix) => WindowCapture.Descendants<Button>(picker)
                .Where(b => AutomationProperties.GetAutomationId(b).StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            var swatches = Buttons("ColorPicker.Swatch")
                .Select(b => ((SolidColorBrush)b.Background).Color)
                .Select(c => new Rgb(c.R, c.G, c.B)).ToArray();
            var hex = WindowCapture.Descendants<TextBox>(picker).Single(b => AutomationProperties.GetAutomationId(b) == "ColorPicker.Hex");
            seen = new PickerSeen(picker.Title, swatches, hex.Text, Buttons("ColorPicker.Recent").Length, WindowCapture.Client(picker));
            picker.Close(); // Cancel
        }
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, InspectAndCancel);
        list.EditCommand.Execute(chip); // modal: returns once the picker closes
        return seen ?? throw new InvalidOperationException("The picker was not inspected.");
    }

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
            ProjectUnselectedOpacity = 30,
        }, team);
        shown.Dialogs.OpenResult = team;
        shown.Model.ImportCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-imported", theme));

        AssertWindowRender(bitmap, theme);
        Assert.Equal("Imported from team-colors.json; Save to apply.", shown.ById<TextBlock>("Settings.Notice").Text);
        Assert.Equal("40%", shown.ById<TextBlock>("Settings.ProjectFillOpacityValue").Text);
        Assert.Equal("30%", shown.ById<TextBlock>("Settings.ProjectUnselectedOpacityValue").Text);
        Assert.Equal(6, shown.Model.ProjectColors.Count);
        Assert.True(shown.Model.IsDirty);
    });

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_InvalidSave_ShowsTheErrorsInline(UiTheme theme) => Sta(() =>
    {
        // A forced state: the sliders snap and clamp, so the window can't reach these values.
        // The view-model is set directly to show Save's backstop.
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.Active));
        shown.Model.ProjectFillOpacity = 5;
        shown.Model.FamilyBarWidth = 8;
        var before = File.ReadAllBytes(shown.SettingsPath);
        shown.Model.SaveCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-invalid-save-forced", theme));

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
        var strips = new List<(PreviewStrip Strip, int Width, int Opacity, int Share)>();
        // Bar widths 1, 3 and 6 at 30 % and 100 % (with the default unselected share, 50 %), then the
        // unselected share's ends at full fill: 100 % (only the top line marks the selected tab) and 10 %.
        var cases = new[] { 1, 3, 6 }.SelectMany(w => new[] { (w, 30, 50), (w, 100, 50) })
            .Concat([(3, 100, 100), (3, 100, 10)]);
        foreach (var (width, opacity, share) in cases)
        {
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"Bar width {width} px · bar {opacity}% · fill {opacity}% · unselected tabs {share}% of the fill",
                    Foreground = text,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Margin = new Thickness(12, 10, 12, 4),
                });
                var strip = new PreviewStrip
                {
                    Settings = SplashItSettings.Default with
                    {
                        FamilyBarWidth = width, FamilyBarOpacity = opacity, ProjectFillOpacity = opacity, ProjectUnselectedOpacity = share,
                    },
                    Margin = new Thickness(12, 0, 12, 0),
                };
                root.Children.Add(strip);
                strips.Add((strip, width, opacity, share));
            }
        }
        root.Children.Add(new Border { Height = 12 });

        var bitmap = Screenshots.Render(root);
        Screenshots.Save(bitmap, Name("preview-matrix", theme));
        Assert.True(Screenshots.DistinctPixels(bitmap) > 60, "the render looks blank");

        // The tabs sit on the strip's window color; the project fills (the colorizer's TabStyleFactory
        // since the Lane B integration) are translucent over it.
        var windowColor = UiPalette.For(theme)[UiPalette.Window];
        var surface = new Rgb(windowColor.R, windowColor.G, windowColor.B);
        var tabBackground = UiPalette.For(theme)[UiPalette.Hover];
        var tabRgb = new Rgb(tabBackground.R, tabBackground.G, tabBackground.B);
        var border = UiPalette.For(theme)[UiPalette.Border];
        var tabBorder = new Rgb(border.R, border.G, border.B);
        var project1 = SplashItDefaults.ProjectColors[0];
        var project2 = SplashItDefaults.ProjectColors[1];
        var family1 = SplashItDefaults.FamilyColors[0];

        foreach (var (strip, width, opacity, share) in strips)
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

            var unselected = opacity / 100.0 * (share / 100.0);
            // The selected project tab: its color at the fill opacity (below its top line, left of its
            // text), and a 3 px darker line along its top.
            AssertNear(ToColor(ColorMath.Blend(project1, opacity / 100.0, surface)), At(tower1, 4, tower1.ActualHeight - 3),
                $"Tower selected, {width}/{opacity}/{share}");
            var line = ColorMath.Darken(project1, SplashIt.UI.Tabs.TabStyleFactory.AccentDarken);
            AssertNear(ToColor(line), At(tower1, 6, 0.5), $"Tower top line, row 1, {width}/{opacity}/{share}");
            AssertNear(ToColor(line), At(tower1, 6, 2.5), $"Tower top line, row 3, {width}/{opacity}/{share}");
            // The project's other tab, and the other project: fill × the unselected share (decision #23).
            AssertNear(ToColor(ColorMath.Blend(project1, unselected, surface)), At(tower2, 4, tower2.ActualHeight - 3),
                $"Tower unselected, {width}/{opacity}/{share}");
            AssertNear(ToColor(ColorMath.Blend(project2, unselected, surface)), At(annex, 4, annex.ActualHeight - 3),
                $"Annex, {width}/{opacity}/{share}");
            // The family bar: inside it, the family color at the bar opacity over what is beneath (the
            // tab's 1 px left border in column 0, its background after that); just right of it, the tab.
            var barX = width / 2;
            var beneath = barX == 0 ? tabBorder : tabRgb;
            AssertNear(ToColor(ColorMath.Blend(family1, opacity / 100.0, beneath)), At(family, barX + 0.5, family.ActualHeight / 2),
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

    [Theory]
    [MemberData(nameof(Themes))]
    public void Window_OnASmallScreen_ScrollsThePanel_AndKeepsTheButtonsVisible(UiTheme theme) => Sta(() =>
    {
        // 1920 x 1080 at 150 % gives a work area of about 1280 x 688 DIPs; this one is shorter still.
        var area = new Rect(0, 0, 1280, 560);
        using var shown = new Shown(theme, new SettingsStatus(SettingsStatusKind.BlockedByPyRevit), workArea: area);
        var bad = shown.Folder.File("from-a-colleague.json");
        File.WriteAllText(bad, """
            { "format": "splash-it-settings", "schemaVersion": 1, "projectColors": ["x"], "familyColors": [],
              "projectFillOpacity": 5, "familyBarWidth": 12, "familyBarOpacity": 0 }
            """);
        shown.Dialogs.OpenResult = bad;
        shown.Model.ImportCommand.Execute(null);

        var bitmap = shown.Capture();
        Screenshots.Save(bitmap, Name("settings-small-screen", theme));

        Assert.Equal(area.Height, shown.Window.MaxHeight);
        Assert.True(shown.Window.ActualHeight <= area.Height + 0.5, $"window is {shown.Window.ActualHeight} tall");
        var scroller = shown.ById<ScrollViewer>("Settings.PanelScroller");
        Assert.True(scroller.ScrollableHeight > 0, "the panel should scroll on a short screen");
        // Save, Close and the whole error banner stay inside the client area.
        foreach (var id in new[] { "Settings.Save", "Settings.Close", "Settings.Export" })
        {
            var button = shown.ById<Button>(id);
            var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), shown.Root).Y;
            Assert.True(bottom <= shown.Root.ActualHeight, $"{id} ends at {bottom}, the client is {shown.Root.ActualHeight}");
        }
        Assert.True(shown.ById<TextBlock>("Settings.ErrorHeading").IsVisible);
        Assert.Equal(Palette(theme, UiPalette.Window), Screenshots.PixelAt(bitmap, 2, 2));
    });

    [Fact]
    public void Launcher_WithARealOwnerWindow_IsOwnedByIt_AndModal() => Sta(() =>
    {
        // Stands in for Revit's main window: a real top-level HWND.
        var revit = new Window { Width = 800, Height = 600, Title = "Owner" };
        WindowCapture.ShowOffScreen(revit);
        var owner = new WindowInteropHelper(revit).Handle;
        Assert.NotEqual(IntPtr.Zero, owner);

        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        IntPtr ownerSeen = IntPtr.Zero, nativeOwner = IntPtr.Zero;
        var ownerEnabled = true;
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
            var helper = new WindowInteropHelper(window);
            ownerSeen = helper.Owner;
            nativeOwner = GetWindow(helper.Handle, GwOwner);
            ownerEnabled = IsWindowEnabled(owner);
            window.Close();
        }
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, InspectAndClose);
        try
        {
            SettingsLauncher.Show(owner, new FakeHost(), UiTheme.Light, path);
        }
        finally
        {
            revit.Close();
        }

        Assert.Equal(owner, ownerSeen);
        Assert.Equal(owner, nativeOwner);
        Assert.False(ownerEnabled, "a modal window disables its owner while it is open");
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

    private const uint GwOwner = 4;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
