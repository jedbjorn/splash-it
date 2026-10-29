using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Tabs;

/// <summary>
/// AC4: the fill and bar styles are BasedOn the original style, with the right background,
/// opacity, bar width, text color, and hover and selected states.
/// </summary>
public class TabStyleFactoryTests
{
    private static readonly Rgb Blue = Rgb.Parse("#4F8EF7");
    private static readonly Rgb Yellow = Rgb.Parse("#EDC948");
    private static readonly Rgb Magenta = Rgb.Parse("#D447BD");

    private static Style Base() => new(typeof(TabItem));

    private static T SetterValue<T>(SetterBaseCollection setters, DependencyProperty property) =>
        (T)setters.OfType<Setter>().Single(s => s.Property == property).Value;

    private static Color Solid(object brush) => ((SolidColorBrush)brush).Color;

    private static Color Argb(Rgb c, int opacityPercent) =>
        Color.FromArgb((byte)Math.Round(opacityPercent / 100.0 * 255, MidpointRounding.AwayFromZero), c.R, c.G, c.B);

    [Fact]
    public void ProjectFill_IsBasedOnTheOriginal_Sealed_AndTargetsTabItem()
    {
        Screenshots.RunSta(() =>
        {
            var original = Base();
            var style = new TabStyleFactory().Create(original, TabTreatment.ProjectFill(Blue, 100), null, ThemeKind.Light);

            Assert.Same(original, style.BasedOn);
            Assert.True(style.IsSealed);
            Assert.Equal(typeof(TabItem), style.TargetType);
            Assert.True(TabStyleFactory.IsDerived(style));
            Assert.False(TabStyleFactory.IsDerived(original));
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(30)]
    [InlineData(10)]
    public void ProjectFill_Background_IsTheColorAtTheFillOpacity(int opacity)
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Blue, opacity), null, ThemeKind.Light);

            Assert.Equal(Argb(Blue, opacity), Solid(SetterValue<Brush>(style.Setters, Control.BackgroundProperty)));
            var normal = style.Triggers.OfType<Trigger>().First(t => Equals(t.Value, false));
            Assert.Equal(TabItem.IsSelectedProperty, normal.Property);
            Assert.Equal(Argb(Blue, opacity), Solid(SetterValue<Brush>(normal.Setters, Control.BackgroundProperty)));
        });
    }

    /// <summary>The text is the higher-contrast of black and white against the fill as shown.</summary>
    [Theory]
    // fill, opacity, theme (surface), expected text over the fill as shown
    [InlineData("#4F8EF7", 100, ThemeKind.Light, "#000000")] // mid blue: black 6.4:1 beats white 3.3:1
    [InlineData("#4F8EF7", 100, ThemeKind.Dark, "#000000")]  // opaque: the backdrop does not matter
    [InlineData("#1A3D7C", 100, ThemeKind.Light, "#FFFFFF")] // dark navy: white
    [InlineData("#1A3D7C", 30, ThemeKind.Light, "#000000")]  // 30 % navy over white reads light: black
    [InlineData("#1A3D7C", 30, ThemeKind.Dark, "#FFFFFF")]   // 30 % navy over #2A2F37 reads dark: white
    [InlineData("#EDC948", 30, ThemeKind.Dark, "#FFFFFF")]   // 30 % yellow over the dark surface: white
    [InlineData("#EDC948", 100, ThemeKind.Dark, "#000000")]  // full yellow: black
    public void ProjectFill_Text_IsTheHigherContrastChoice_OverLightAndDark(string fill, int opacity, ThemeKind theme, string expected)
    {
        Screenshots.RunSta(() =>
        {
            var color = Rgb.Parse(fill);
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(color, opacity), originalBackground: null, theme);

            var text = Solid(SetterValue<Brush>(style.Setters, Control.ForegroundProperty));
            Assert.Equal(HostTabs.ColorOf(Rgb.Parse(expected)), text);

            // And it really is the better one, measured on the blend over the surface.
            var shown = ColorMath.Blend(color, opacity / 100.0, theme.Surface());
            var chosen = Rgb.Parse(expected);
            var other = chosen == ColorMath.Black ? ColorMath.White : ColorMath.Black;
            Assert.True(ColorMath.ContrastRatio(chosen, shown) >= ColorMath.ContrastRatio(other, shown));
        });
    }

    [Fact]
    public void ProjectFill_Text_UsesTheTabsOwnSolidBackground_ElseTheThemeSurface()
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var navy = TabTreatment.ProjectFill(Rgb.Parse("#1A3D7C"), 30);
            Brush Text(Brush? original, ThemeKind theme) =>
                SetterValue<Brush>(factory.Create(Base(), navy, original, theme).Setters, Control.ForegroundProperty);

            // A solid dark tab background on the light theme wins over the light surface.
            Assert.Equal(Colors.White, Solid(Text(HostTabs.Brush(Rgb.Parse("#20242B")), ThemeKind.Light)));
            // A solid light tab background on the dark theme wins over the dark surface.
            Assert.Equal(Colors.Black, Solid(Text(HostTabs.Brush(Rgb.Parse("#FFFFFF")), ThemeKind.Dark)));
            // A transparent solid background (Revit's dark unselected tab) falls through to the surface.
            Assert.Equal(Colors.White, Solid(Text(new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)), ThemeKind.Dark)));
            // A gradient is not a solid brush: the theme surface is the backdrop.
            var gradient = new LinearGradientBrush(Colors.Black, Colors.Black, 0);
            Assert.Equal(Colors.Black, Solid(Text(gradient, ThemeKind.Light)));
            Assert.Equal(Colors.White, Solid(Text(gradient, ThemeKind.Dark)));
        });
    }

    [Fact]
    public void ProjectFill_HoverTrigger_LightensTheFill_WhenNotSelected()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Blue, 60), null, ThemeKind.Light);

            var hover = style.Triggers.OfType<MultiTrigger>().Single();
            Assert.Contains(hover.Conditions, c => c.Property == UIElement.IsMouseOverProperty && Equals(c.Value, true));
            Assert.Contains(hover.Conditions, c => c.Property == TabItem.IsSelectedProperty && Equals(c.Value, false));
            var lighter = ColorMath.Lighten(Blue, TabStyleFactory.HoverLighten);
            Assert.Equal(Argb(lighter, 60), Solid(SetterValue<Brush>(hover.Setters, Control.BackgroundProperty)));
            Assert.True(ColorMath.RelativeLuminance(lighter) > ColorMath.RelativeLuminance(Blue));
            Assert.Equal(
                HostTabs.ColorOf(ColorMath.TextColorFor(lighter, 0.6, ColorMath.White)),
                Solid(SetterValue<Brush>(hover.Setters, Control.ForegroundProperty)));
        });
    }

    [Fact]
    public void ProjectFill_SelectedTrigger_FullOpacity_AndA2PxDarkerTopAccent()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Yellow, 30), null, ThemeKind.Dark);

            var selected = style.Triggers.OfType<Trigger>().Single(t => Equals(t.Value, true));
            Assert.Equal(TabItem.IsSelectedProperty, selected.Property);
            Assert.Equal(HostTabs.ColorOf(Yellow), Solid(SetterValue<Brush>(selected.Setters, Control.BackgroundProperty)));
            Assert.Equal(Colors.Black, Solid(SetterValue<Brush>(selected.Setters, Control.ForegroundProperty)));

            var accent = SetterValue<TabDecorationSpec>(selected.Setters, TabDecoration.DecorationProperty);
            Assert.Null(accent.LeftBar);
            Assert.Equal(TabStyleFactory.AccentHeight, accent.TopAccentHeight);
            Assert.Equal(2, accent.TopAccentHeight);
            Assert.Equal(HostTabs.ColorOf(ColorMath.Darken(Yellow, TabStyleFactory.AccentDarken)), Solid(accent.TopAccent!));

            // Selected comes last, so it wins over the hover trigger when both apply.
            Assert.Same(selected, style.Triggers[^1]);
        });
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(3, 100)]
    [InlineData(6, 30)]
    public void FamilyBar_IsOnlyALeftBar_OfTheWidthAndOpacity(int width, int opacity)
    {
        Screenshots.RunSta(() =>
        {
            var original = Base();
            var style = new TabStyleFactory().Create(original, TabTreatment.FamilyBar(Magenta, width, opacity), null, ThemeKind.Light);

            Assert.Same(original, style.BasedOn);
            Assert.Empty(style.Triggers); // selection and hover stay native
            var setter = Assert.Single(style.Setters.OfType<Setter>());
            Assert.Equal(TabDecoration.DecorationProperty, setter.Property);
            var bar = (TabDecorationSpec)setter.Value;
            Assert.Equal(width, bar.LeftBarWidth);
            Assert.Equal(Argb(Magenta, opacity), Solid(bar.LeftBar!));
            Assert.Null(bar.TopAccent);
        });
    }

    [Fact]
    public void Styles_AreCachedPerOriginalAndTreatment()
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var a = Base();
            var b = Base();
            var fill = TabTreatment.ProjectFill(Blue, 100);

            var first = factory.Create(a, fill, null, ThemeKind.Light);
            Assert.Same(first, factory.Create(a, fill, null, ThemeKind.Light));
            Assert.NotSame(first, factory.Create(b, fill, null, ThemeKind.Light));                  // another original
            Assert.NotSame(first, factory.Create(a, TabTreatment.ProjectFill(Blue, 50), null, ThemeKind.Light));
            Assert.NotSame(first, factory.Create(a, TabTreatment.ProjectFill(Yellow, 100), null, ThemeKind.Light));
            Assert.NotSame(first, factory.Create(a, fill, null, ThemeKind.Dark));                   // another backdrop

            // A bar has no text, so the backdrop does not split its cache entry.
            var bar = TabTreatment.FamilyBar(Magenta, 3, 100);
            Assert.Same(factory.Create(a, bar, null, ThemeKind.Light), factory.Create(a, bar, null, ThemeKind.Dark));

            factory.ClearCache();
            Assert.Equal(0, factory.CachedCount);
            Assert.NotSame(first, factory.Create(a, fill, null, ThemeKind.Light));
        });
    }

    [Fact]
    public void Create_RefusesAnOriginalThatATabItemStyleCannotDeriveFrom()
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            Assert.True(TabStyleFactory.CanDerive(null));
            Assert.True(TabStyleFactory.CanDerive(new Style(typeof(Control))));
            Assert.False(TabStyleFactory.CanDerive(new Style(typeof(Button))));
            Assert.Throws<ArgumentException>(() =>
                factory.Create(new Style(typeof(Button)), TabTreatment.ProjectFill(Blue, 100), null, ThemeKind.Light));

            var fromControl = factory.Create(new Style(typeof(Control)), TabTreatment.ProjectFill(Blue, 100), null, ThemeKind.Light);
            Assert.Equal(typeof(TabItem), fromControl.TargetType);
        });
    }

    [Fact]
    public void TreatmentNumbers_AreClampedToTheValidRanges()
    {
        Assert.Equal(SplashItDefaults.MinOpacity, TabTreatment.ProjectFill(Blue, 0).OpacityPercent);
        Assert.Equal(SplashItDefaults.MaxOpacity, TabTreatment.ProjectFill(Blue, 250).OpacityPercent);
        Assert.Equal(SplashItDefaults.MaxBarWidth, TabTreatment.FamilyBar(Blue, 40, 100).BarWidth);
        Assert.Equal(SplashItDefaults.MinBarWidth, TabTreatment.FamilyBar(Blue, 0, 100).BarWidth);
        Assert.Equal(0, TabTreatment.ProjectFill(Blue, 100).BarWidth);
    }

    /// <summary>
    /// On a live tab with the Revit-like template: the fill wins over the original style's
    /// own selected and hover triggers, text follows, the accent and bar are adorners, and
    /// putting the original back removes everything.
    /// </summary>
    [Fact]
    public void Applied_ToARevitLikeTab_TheStylesTakeEffect_AndRestoringRemovesThem()
    {
        Screenshots.RunSta(() =>
        {
            var original = HostTabs.RevitLikeStyle(ThemeKind.Light);
            var strip = HostTabs.Strip(original, ThemeKind.Light, "Project1 - L1", "Project1 - L2", "Family1 - Ref. Level");
            var root = HostTabs.Host(strip, SplashItDefaults.LightSurface);
            var project = HostTabs.Tab(strip, 0);
            var selectedProject = HostTabs.Tab(strip, 1);
            var family = HostTabs.Tab(strip, 2);
            selectedProject.IsSelected = true;
            HostTabs.Layout(root);
            Assert.Equal(Colors.White, Solid(selectedProject.Background)); // the original's own selected trigger

            var factory = new TabStyleFactory();
            var fill = factory.Create(original, TabTreatment.ProjectFill(Blue, 40), project.Background, ThemeKind.Light);
            project.Style = fill;
            selectedProject.Style = fill;
            family.Style = factory.Create(original, TabTreatment.FamilyBar(Magenta, 3, 100), family.Background, ThemeKind.Light);
            HostTabs.Layout(root);

            Assert.Equal(Argb(Blue, 40), Solid(project.Background));
            Assert.Equal(HostTabs.ColorOf(Blue), Solid(selectedProject.Background)); // beats the original's white
            Assert.Equal(Colors.Black, Solid(project.Foreground));
            Assert.Null(TabDecoration.FindAdorner(project));
            var accent = TabDecoration.FindAdorner(selectedProject);
            Assert.NotNull(accent);
            Assert.Equal(2, accent!.Spec.TopAccentHeight);
            var bar = TabDecoration.FindAdorner(family);
            Assert.NotNull(bar);
            Assert.Equal(3, bar!.Spec.LeftBarWidth);
            Assert.Equal(Colors.Transparent.A, Solid(family.Background).A); // the family's own background is untouched

            // Selection moves: the trigger restyles at once, no new style needed.
            project.IsSelected = true;
            HostTabs.Layout(root);
            Assert.NotNull(TabDecoration.FindAdorner(project));
            Assert.Null(TabDecoration.FindAdorner(selectedProject));
            Assert.Equal(Argb(Blue, 40), Solid(selectedProject.Background));

            foreach (var tab in new[] { project, selectedProject, family }) tab.Style = original;
            HostTabs.Layout(root);
            Assert.Null(TabDecoration.FindAdorner(project));
            Assert.Null(TabDecoration.FindAdorner(family));
            Assert.Equal(Colors.White, Solid(project.Background)); // selected again, natively
            Assert.Equal(0, Solid(family.Background).A);
        });
    }
}
