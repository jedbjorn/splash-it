using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;

namespace SplashIt.RuntimeTests;

/// <summary>
/// Smoke test for the screenshot pipeline the colorizer and settings-window lanes
/// use: build a small WPF tree off-screen on an STA thread, render it to PNG, and
/// check the image is not blank. It draws the two default color lists as project
/// fills and 3 px family bars on the light and dark theme surfaces.
/// </summary>
public class ScreenshotPipelineTests
{
    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    private static SolidColorBrush Brush(Rgb c)
    {
        var brush = new SolidColorBrush(ToColor(c));
        brush.Freeze();
        return brush;
    }

    private static FrameworkElement ThemePanel(string title, Rgb surface)
    {
        var text = Brush(ColorMath.TextColorFor(surface));
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Project colors: solid fills, label in the higher-contrast text color.
        var projects = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in SplashItDefaults.ProjectColors)
        {
            projects.Children.Add(new Border
            {
                Width = 72,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 4),
                Background = Brush(c),
                Child = new TextBlock
                {
                    Text = c.ToHex(),
                    Foreground = Brush(ColorMath.TextColorFor(c)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        stack.Children.Add(projects);

        // Family colors: a 3 px bar on the left of a native-looking tab.
        var families = new WrapPanel();
        foreach (var c in SplashItDefaults.FamilyColors)
        {
            var tab = new DockPanel { Width = 72, Height = 26, Margin = new Thickness(0, 0, 4, 4) };
            var bar = new Rectangle { Width = SplashItDefaults.FamilyBarWidth, Fill = Brush(c) };
            DockPanel.SetDock(bar, Dock.Left);
            tab.Children.Add(bar);
            tab.Children.Add(new TextBlock
            {
                Text = c.ToHex(),
                Foreground = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            families.Children.Add(tab);
        }
        stack.Children.Add(families);

        return new Border { Background = Brush(surface), Child = stack, Width = 6 * 76 + 24 };
    }

    [Fact]
    public void RendersAWpfTree_OffScreen_ToANonBlankPng()
    {
        Screenshots.RunSta(() =>
        {
            var root = new StackPanel();
            root.Children.Add(ThemePanel("Splash-It default colors — light surface #FFFFFF", SplashItDefaults.LightSurface));
            root.Children.Add(ThemePanel("Splash-It default colors — dark surface #2A2F37", SplashItDefaults.DarkSurface));

            var bitmap = Screenshots.Render(root);
            var path = Screenshots.Save(bitmap, "smoke-default-colors");

            Assert.True(System.IO.File.Exists(path), path);
            Assert.True(new System.IO.FileInfo(path).Length > 1000, "the PNG is suspiciously small");
            Assert.True(bitmap.PixelWidth >= 400 && bitmap.PixelHeight >= 150, $"{bitmap.PixelWidth}x{bitmap.PixelHeight}");
            // Two surfaces, 24 swatches and anti-aliased text: far more than a blank image's one value.
            Assert.True(Screenshots.DistinctPixels(bitmap) > 50, "the render looks blank");

            // The top-left corner is the light surface; the bottom-left the dark one.
            Assert.Equal(Colors.White, Screenshots.PixelAt(bitmap, 1, 1));
            Assert.Equal(Color.FromRgb(0x2A, 0x2F, 0x37), Screenshots.PixelAt(bitmap, 1, bitmap.PixelHeight - 2));
        });
    }
}
