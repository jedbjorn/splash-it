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

    private static Color Argb(Rgb c, int opacityPercent) => Argb(c, opacityPercent / 100.0);

    private static Color Argb(Rgb c, double opacity) =>
        Color.FromArgb((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero), c.R, c.G, c.B);

    private static Trigger SelectedTrigger(Style style) => style.Triggers.OfType<Trigger>().Single(t => Equals(t.Value, true));

    private static Trigger NormalTrigger(Style style) => style.Triggers.OfType<Trigger>().Single(t => Equals(t.Value, false));

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

    /// <summary>
    /// #22/#23: the selected tab is the color at the fill opacity; the project's other tabs are
    /// the same color at the Unselected tab opacity, a share of the fill opacity (default 50 %).
    /// </summary>
    [Theory]
    [InlineData(100, 50)]   // the defaults
    [InlineData(100, 100)]  // at 100 % only the top line marks the selected tab
    [InlineData(100, 20)]
    [InlineData(30, 50)]
    [InlineData(10, 10)]
    public void ProjectFill_SelectedIsTheFill_AndUnselectedIsTheShareOfIt(int opacity, int share)
    {
        Screenshots.RunSta(() =>
        {
            var treatment = TabTreatment.ProjectFill(Blue, opacity, share);
            var style = new TabStyleFactory().Create(Base(), treatment, null, ThemeKind.Light);

            var unselected = Argb(Blue, opacity / 100.0 * share / 100.0);
            Assert.Equal(unselected, Solid(SetterValue<Brush>(style.Setters, Control.BackgroundProperty)));
            var normal = NormalTrigger(style);
            Assert.Equal(TabItem.IsSelectedProperty, normal.Property);
            Assert.Equal(unselected, Solid(SetterValue<Brush>(normal.Setters, Control.BackgroundProperty)));
            Assert.Equal(Argb(Blue, opacity), Solid(SetterValue<Brush>(SelectedTrigger(style).Setters, Control.BackgroundProperty)));
            Assert.Equal(share, treatment.UnselectedPercent);
        });
    }

    [Fact]
    public void UnselectedShare_DefaultsTo50_IsClamped_AndSplitsTheCache()
    {
        Screenshots.RunSta(() =>
        {
            Assert.Equal(50, TabTreatment.DefaultUnselectedPercent);
            Assert.Equal(50, TabTreatment.ProjectFill(Blue, 100).UnselectedPercent);
            Assert.Equal(10, TabTreatment.ProjectFill(Blue, 100, 0).UnselectedPercent);
            Assert.Equal(100, TabTreatment.ProjectFill(Blue, 100, 400).UnselectedPercent);
            Assert.Equal(0, TabTreatment.FamilyBar(Blue, 3, 100).UnselectedPercent);

            var factory = new TabStyleFactory();
            var a = Base();
            Assert.NotSame(
                factory.Create(a, TabTreatment.ProjectFill(Blue, 100, 50), null, ThemeKind.Light),
                factory.Create(a, TabTreatment.ProjectFill(Blue, 100, 20), null, ThemeKind.Light));
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

            // The selected tab: the fill at its opacity.
            var selected = SelectedTrigger(style).Setters;
            var text = Solid(SetterValue<Brush>(selected, Control.ForegroundProperty));
            Assert.Equal(HostTabs.ColorOf(Rgb.Parse(expected)), text);

            // And it really is the better one, measured on the blend over the surface.
            var shown = ColorMath.Blend(color, opacity / 100.0, theme.Surface());
            var chosen = Rgb.Parse(expected);
            var other = chosen == ColorMath.Black ? ColorMath.White : ColorMath.Black;
            Assert.True(ColorMath.ContrastRatio(chosen, shown) >= ColorMath.ContrastRatio(other, shown));

            // An unselected tab: measured on its own (muted) fill as shown.
            var muted = opacity / 100.0 * TabTreatment.DefaultUnselectedPercent / 100.0;
            Assert.Equal(
                HostTabs.ColorOf(ColorMath.TextColorFor(color, muted, theme.Surface())),
                Solid(SetterValue<Brush>(NormalTrigger(style).Setters, Control.ForegroundProperty)));
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

    /// <summary>Review M1: the close glyph follows the text color in every state, never Revit's grey.</summary>
    [Fact]
    public void ProjectFill_CloseGlyph_FollowsTheTextInEveryState()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Rgb.Parse("#F28E2B"), 100), null, ThemeKind.Light);
            foreach (var setters in new[] { style.Setters, NormalTrigger(style).Setters, style.Triggers.OfType<MultiTrigger>().Single().Setters, SelectedTrigger(style).Setters })
                Assert.Same(SetterValue<Brush>(setters, Control.ForegroundProperty), SetterValue<Brush>(setters, TabCloseGlyph.ForegroundProperty));
            Assert.Equal(Colors.Black, Solid(SetterValue<Brush>(SelectedTrigger(style).Setters, TabCloseGlyph.ForegroundProperty)));

            var bar = new TabStyleFactory().Create(Base(), TabTreatment.FamilyBar(Magenta, 3, 100), null, ThemeKind.Light);
            Assert.DoesNotContain(bar.Setters.OfType<Setter>(), s => s.Property == TabCloseGlyph.ForegroundProperty); // families stay native
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
            Assert.Equal(Argb(lighter, 0.3), Solid(SetterValue<Brush>(hover.Setters, Control.BackgroundProperty))); // 50 % of 60 %
            Assert.True(ColorMath.RelativeLuminance(lighter) > ColorMath.RelativeLuminance(Blue));
            Assert.Equal(
                HostTabs.ColorOf(ColorMath.TextColorFor(lighter, 0.3, ColorMath.White)),
                Solid(SetterValue<Brush>(hover.Setters, Control.ForegroundProperty)));
        });
    }

    [Fact]
    public void ProjectFill_SelectedTrigger_TheFillOpacity_AndA3PxDarkerTopLine()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Yellow, 30), null, ThemeKind.Dark);

            var selected = SelectedTrigger(style);
            Assert.Equal(TabItem.IsSelectedProperty, selected.Property);
            Assert.Equal(Argb(Yellow, 30), Solid(SetterValue<Brush>(selected.Setters, Control.BackgroundProperty)));
            Assert.Equal(Colors.White, Solid(SetterValue<Brush>(selected.Setters, Control.ForegroundProperty))); // 30 % yellow over #2A2F37

            var accent = SetterValue<TabDecorationSpec>(selected.Setters, TabDecoration.DecorationProperty);
            Assert.Null(accent.LeftBar);
            Assert.Equal(TabStyleFactory.AccentHeight, accent.TopAccentHeight);
            Assert.Equal(3, accent.TopAccentHeight);
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

            Assert.Equal(Argb(Blue, 20), Solid(project.Background));          // unselected: 50 % of the 40 % fill
            Assert.Equal(Argb(Blue, 40), Solid(selectedProject.Background));  // beats the original's white
            Assert.Equal(Colors.Black, Solid(project.Foreground));
            Assert.Null(TabDecoration.FindAdorner(project));
            var accent = TabDecoration.FindAdorner(selectedProject);
            Assert.NotNull(accent);
            Assert.Equal(3, accent!.Spec.TopAccentHeight);

            // The close button is recolored to the text color; the family's keeps its grey.
            var close = TabCloseGlyph.FindButton(selectedProject)!;
            Assert.Equal(Colors.Black, Solid(close.Foreground));
            Assert.Same(close, TabCloseGlyph.FindRecolored(selectedProject));
            Assert.Equal(HostTabs.CloseGrey, Solid(TabCloseGlyph.FindButton(family)!.Foreground));
            var bar = TabDecoration.FindAdorner(family);
            Assert.NotNull(bar);
            Assert.Equal(3, bar!.Spec.LeftBarWidth);
            Assert.Equal(Colors.Transparent.A, Solid(family.Background).A); // the family's own background is untouched

            // Selection moves: the trigger restyles at once, no new style needed.
            project.IsSelected = true;
            HostTabs.Layout(root);
            Assert.NotNull(TabDecoration.FindAdorner(project));
            Assert.Null(TabDecoration.FindAdorner(selectedProject));
            Assert.Equal(Argb(Blue, 20), Solid(selectedProject.Background));

            foreach (var tab in new[] { project, selectedProject, family }) tab.Style = original;
            HostTabs.Layout(root);
            Assert.Null(TabDecoration.FindAdorner(project));
            Assert.Null(TabDecoration.FindAdorner(family));
            // The close button's own color is back exactly: no local value left.
            Assert.Equal(DependencyProperty.UnsetValue, close.ReadLocalValue(Control.ForegroundProperty));
            Assert.Equal(HostTabs.CloseGrey, Solid(close.Foreground));
            Assert.Null(TabCloseGlyph.FindRecolored(selectedProject));
            Assert.Equal(Colors.White, Solid(project.Background)); // selected again, natively
            Assert.Equal(0, Solid(family.Background).A);
        });
    }
}
