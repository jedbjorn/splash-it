using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.UI.Tabs;
using SplashIt.Core.Theming;

namespace SplashIt.RuntimeTests.Support;

/// <summary>
/// Tab strips for the runtime tests: a Revit-like host TabItem style, WPF's stock style,
/// and helpers to lay a strip out off-screen (inside an <see cref="AdornerDecorator"/>, so
/// <see cref="TabDecoration"/>'s adorners render).
/// </summary>
public static class HostTabs
{
    /// <summary>
    /// A stand-in for Revit's document TabItem style, modeled on the template the tab
    /// surface's diagnostics read in Revit 2026 and 2027 on the VM
    /// (shared/evidence/lane-b/vm/&lt;year&gt;/diag-&lt;year&gt;.txt): a Grid holding a Border
    /// whose Background is the TabItem's Background, the header (icon placeholder and
    /// title in the TabItem's Foreground) and a close glyph with its own fixed grey
    /// (#A2A6B0, set by the button's own style as in Revit, so the tab's Foreground does
    /// not reach it; the button is named mDocumentCloseButton, as in Revit). Like Revit's, the style is a local style on the tab, and
    /// sets its own selected and unselected backgrounds (selected: the theme's surface;
    /// unselected: transparent over the strip). As in Revit (vm/2026-review/diag-2026-close-button.txt
    /// and the AC11 captures), the close button is 16 x 16, its right edge 2 px inside the tab
    /// (here the grid's 1 px gap plus 1 px), 12 px after the title as Revit's, and Hidden,
    /// keeping its slot, unless the tab is
    /// selected or under the mouse.
    /// <para>
    /// The optional numbers make a variant whose close button is NOT where Revit's is (review M2:
    /// so a test can tell an anchor measured from the button from one at the right edge), or one
    /// with no named close button (<paramref name="closeName"/> another name: the fallback slot).
    /// </para>
    /// </summary>
    public static Style RevitLikeStyle(UiTheme theme, double closeWidth = 16, double closeRightInset = 1,
        string closeName = "mDocumentCloseButton")
    {
        var dark = theme == UiTheme.Dark;
        var text = dark ? "#FFF5F5F5" : "#FF3C3C3C";
        var selected = dark ? "#FF2A2F37" : "#FFFFFFFF";
        var hover = dark ? "#FF4A5261" : "#FFE6E6E6";
        var xaml = $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="TabItem">
              <Setter Property="Background" Value="#00FFFFFF" />
              <Setter Property="Foreground" Value="{{text}}" />
              <Setter Property="BorderBrush" Value="#00FFFFFF" />
              <Setter Property="FontFamily" Value="Segoe UI" />
              <Setter Property="FontSize" Value="12" />
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="TabItem">
                    <Grid x:Name="mGrid" Height="20" Margin="0,0,1,0">
                      <Border x:Name="mBorder" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" />
                      <DockPanel Margin="4,2,{{closeRightInset}},2">
                        <Button x:Name="{{closeName}}" DockPanel.Dock="Right" Content="×" Width="{{closeWidth}}" Height="16" Margin="12,0,0,0"
                                VerticalAlignment="Center" Focusable="False" Visibility="Hidden">
                          <Button.Style>
                            <Style TargetType="Button">
                              <Setter Property="Foreground" Value="#FFA2A6B0" />
                              <Setter Property="Template">
                                <Setter.Value>
                                  <ControlTemplate TargetType="Button">
                                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}" />
                                  </ControlTemplate>
                                </Setter.Value>
                              </Setter>
                            </Style>
                          </Button.Style>
                        </Button>
                        <Border Width="12" Height="12" Margin="0,0,5,0" BorderThickness="1" BorderBrush="{TemplateBinding Foreground}" Opacity="0.5" />
                        <ContentPresenter ContentSource="Header" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}" />
                      </DockPanel>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsSelected" Value="True"><Setter TargetName="{{closeName}}" Property="Visibility" Value="Visible" /></Trigger>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="{{closeName}}" Property="Visibility" Value="Visible" /></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
              <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{{hover}}" /></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{{selected}}" /></Trigger>
              </Style.Triggers>
            </Style>
            """;
        return (Style)XamlReader.Parse(xaml);
    }

    /// <summary>The close button's own grey, as Revit's light theme sets it (a style setter on the button).</summary>
    public static readonly Color CloseGrey = Color.FromRgb(0xA2, 0xA6, 0xB0);

    /// <summary>The strip behind the tabs (Revit's tab well), per theme.</summary>
    public static Rgb Well(UiTheme theme) => theme == UiTheme.Dark ? Rgb.Parse("#3B4453") : Rgb.Parse("#D5D9DE");

    public static SolidColorBrush Brush(Rgb c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static Color ColorOf(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>
    /// A tab background's color: a solid brush's own, or the solid part of a project fill's fade
    /// (#30: its first stop, before the fade starts).
    /// </summary>
    public static Color FillOf(Brush brush) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        GradientBrush gradient => gradient.GradientStops[0].Color,
        _ => throw new ArgumentException($"Not a fill: {brush.GetType().Name}", nameof(brush)),
    };

    /// <summary>A TabControl whose strip shows only the headers (no content area), with one tab per title.</summary>
    public static TabControl Strip(Style? tabStyle, UiTheme theme, params string[] titles)
    {
        var strip = new TabControl
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brush(Well(theme)),
            Template = StripTemplate(),
        };
        foreach (var title in titles)
            strip.Items.Add(new TabItem { Header = title, Style = tabStyle, Content = null });
        return strip;
    }

    public static TabItem Tab(TabControl strip, int index) => (TabItem)strip.Items[index]!;

    /// <summary>Wraps <paramref name="content"/> on a surface with an adorner layer, ready for <see cref="Layout"/>.</summary>
    public static AdornerDecorator Host(UIElement content, Rgb surface, double padding = 8) =>
        new()
        {
            UseLayoutRounding = true, // whole-pixel tab positions, as in Revit's tab strip
            Child = new Border { Background = Brush(surface), Padding = new Thickness(padding), Child = content },
        };

    /// <summary>Lays out off-screen, twice, so adorners are placed after the first pass.</summary>
    public static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 3; i++)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
        }
    }

    /// <summary>A TabControl template with only the tab panel (the header strip).</summary>
    private static ControlTemplate StripTemplate()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabControl">
              <Border Background="{TemplateBinding Background}" Padding="2,2,2,0">
                <TabPanel IsItemsHost="True" />
              </Border>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(xaml);
    }
}
