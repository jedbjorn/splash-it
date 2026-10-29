using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Tabs;
using SplashIt.Core.Theming;

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

    /// <summary>
    /// A project fill setter's color at its opacity (#30: the value is a binding whose converter
    /// draws that color, solid up to the fade).
    /// </summary>
    private static Color Fill(SetterBaseCollection setters)
    {
        var value = setters.OfType<Setter>().Single(s => s.Property == Control.BackgroundProperty).Value;
        var converter = Assert.IsType<ProjectFillConverter>(Assert.IsType<System.Windows.Data.MultiBinding>(value).Converter);
        return Argb(converter.Color, converter.Opacity);
    }

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
            var style = new TabStyleFactory().Create(original, TabTreatment.ProjectFill(Blue, 100), null, UiTheme.Light);

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
            var style = new TabStyleFactory().Create(Base(), treatment, null, UiTheme.Light);

            var unselected = Argb(Blue, opacity / 100.0 * share / 100.0);
            Assert.Equal(unselected, Fill(style.Setters));
            var normal = NormalTrigger(style);
            Assert.Equal(TabItem.IsSelectedProperty, normal.Property);
            Assert.Equal(unselected, Fill(normal.Setters));
            Assert.Equal(Argb(Blue, opacity), Fill(SelectedTrigger(style).Setters));
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
                factory.Create(a, TabTreatment.ProjectFill(Blue, 100, 50), null, UiTheme.Light),
                factory.Create(a, TabTreatment.ProjectFill(Blue, 100, 20), null, UiTheme.Light));
        });
    }

    /// <summary>
    /// The higher-contrast of black and white at its worst over two points (#31): the fill as shown,
    /// and the fade under a title's last column (<see cref="ProjectFillShape.TitleEndShare"/>).
    /// </summary>
    private static Rgb Expected(Rgb color, double opacity, Rgb backdrop) => ColorMath.TextColorForAll(
        ColorMath.Blend(color, opacity, backdrop), ColorMath.Blend(color, opacity * ProjectFillShape.Default.TitleEndShare, backdrop));

    /// <summary>The text is the higher-contrast of black and white at its worst over the solid fill and the title's end (#31).</summary>
    [Theory]
    // fill, opacity, theme (surface), expected text
    [InlineData("#4F8EF7", 100, UiTheme.Light, "#000000")] // mid blue: black 6.4:1 beats white 3.3:1, and the end fades lighter
    [InlineData("#4F8EF7", 100, UiTheme.Dark, "#FFFFFF")]  // #31: black would drop to 2.98:1 where the fade darkens; white keeps 3.2:1
    [InlineData("#1A3D7C", 100, UiTheme.Light, "#FFFFFF")] // dark navy: white
    [InlineData("#1A3D7C", 30, UiTheme.Light, "#000000")]  // 30 % navy over white reads light: black
    [InlineData("#1A3D7C", 30, UiTheme.Dark, "#FFFFFF")]   // 30 % navy over #2A2F37 reads dark: white
    [InlineData("#EDC948", 30, UiTheme.Dark, "#FFFFFF")]   // 30 % yellow over the dark surface: white
    [InlineData("#EDC948", 100, UiTheme.Dark, "#000000")]  // full yellow: black
    public void ProjectFill_Text_IsTheHigherContrastChoice_OverLightAndDark(string fill, int opacity, UiTheme theme, string expected)
    {
        Screenshots.RunSta(() =>
        {
            var color = Rgb.Parse(fill);
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(color, opacity), originalBackground: null, theme);

            // The selected tab: the fill at its opacity.
            var selected = SelectedTrigger(style).Setters;
            var text = Solid(SetterValue<Brush>(selected, Control.ForegroundProperty));
            Assert.Equal(HostTabs.ColorOf(Rgb.Parse(expected)), text);

            // And it really is the better one at its worst over the two points.
            var end = ProjectFillShape.Default.TitleEndShare;
            var points = new[] { ColorMath.Blend(color, opacity / 100.0, theme.Surface()), ColorMath.Blend(color, opacity / 100.0 * end, theme.Surface()) };
            var chosen = Rgb.Parse(expected);
            var other = chosen == ColorMath.Black ? ColorMath.White : ColorMath.Black;
            Assert.True(points.Min(p => ColorMath.ContrastRatio(chosen, p)) >= points.Min(p => ColorMath.ContrastRatio(other, p)));

            // An unselected tab: its own (muted) fill, the same two points.
            var muted = opacity / 100.0 * TabTreatment.DefaultUnselectedPercent / 100.0;
            Assert.Equal(
                HostTabs.ColorOf(Expected(color, muted, theme.Surface())),
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
            Brush Text(Brush? original, UiTheme theme) =>
                SetterValue<Brush>(factory.Create(Base(), navy, original, theme).Setters, Control.ForegroundProperty);

            // A solid dark tab background on the light theme wins over the light surface.
            Assert.Equal(Colors.White, Solid(Text(HostTabs.Brush(Rgb.Parse("#20242B")), UiTheme.Light)));
            // A solid light tab background on the dark theme wins over the dark surface.
            Assert.Equal(Colors.Black, Solid(Text(HostTabs.Brush(Rgb.Parse("#FFFFFF")), UiTheme.Dark)));
            // A transparent solid background (Revit's dark unselected tab) falls through to the surface.
            Assert.Equal(Colors.White, Solid(Text(new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)), UiTheme.Dark)));
            // A gradient is not a solid brush: the theme surface is the backdrop.
            var gradient = new LinearGradientBrush(Colors.Black, Colors.Black, 0);
            Assert.Equal(Colors.Black, Solid(Text(gradient, UiTheme.Light)));
            Assert.Equal(Colors.White, Solid(Text(gradient, UiTheme.Dark)));
        });
    }

    /// <summary>
    /// Review M1 and #30: the close glyph is never Revit's grey; the fill ends before it, so in every
    /// state it is black or white against the strip it sits on (the tab's own background, else the
    /// theme surface), not the fill: on the dark theme a full orange tab has black text and a white ×.
    /// </summary>
    [Theory]
    [InlineData(UiTheme.Light, "#000000", "#000000")]
    [InlineData(UiTheme.Dark, "#000000", "#FFFFFF")]
    public void ProjectFill_CloseGlyph_IsChosenAgainstTheStrip_InEveryState(UiTheme theme, string text, string glyph)
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Rgb.Parse("#F28E2B"), 100), null, theme);
            var states = new[] { style.Setters, NormalTrigger(style).Setters, style.Triggers.OfType<MultiTrigger>().Single().Setters, SelectedTrigger(style).Setters };
            var first = SetterValue<Brush>(style.Setters, TabCloseGlyph.ForegroundProperty);
            foreach (var setters in states)
                Assert.Same(first, SetterValue<Brush>(setters, TabCloseGlyph.ForegroundProperty));
            Assert.Equal(HostTabs.ColorOf(ColorMath.TextColorFor(theme.Surface())), Solid(first));
            Assert.Equal(HostTabs.ColorOf(Rgb.Parse(glyph)), Solid(first));
            Assert.Equal(HostTabs.ColorOf(Rgb.Parse(text)), Solid(SetterValue<Brush>(SelectedTrigger(style).Setters, Control.ForegroundProperty)));

            var bar = new TabStyleFactory().Create(Base(), TabTreatment.FamilyBar(Magenta, 3, 100), null, UiTheme.Light);
            Assert.DoesNotContain(bar.Setters.OfType<Setter>(), s => s.Property == TabCloseGlyph.ForegroundProperty); // families stay native
        });
    }

    [Fact]
    public void ProjectFill_HoverTrigger_LightensTheFill_WhenNotSelected()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Blue, 60), null, UiTheme.Light);

            var hover = style.Triggers.OfType<MultiTrigger>().Single();
            Assert.Contains(hover.Conditions, c => c.Property == UIElement.IsMouseOverProperty && Equals(c.Value, true));
            Assert.Contains(hover.Conditions, c => c.Property == TabItem.IsSelectedProperty && Equals(c.Value, false));
            var lighter = ColorMath.Lighten(Blue, TabStyleFactory.HoverLighten);
            Assert.Equal(Argb(lighter, 0.3), Fill(hover.Setters)); // 50 % of 60 %
            Assert.True(ColorMath.RelativeLuminance(lighter) > ColorMath.RelativeLuminance(Blue));
            Assert.Equal(
                HostTabs.ColorOf(Expected(lighter, 0.3, ColorMath.White)),
                Solid(SetterValue<Brush>(hover.Setters, Control.ForegroundProperty)));
        });
    }

    [Fact]
    public void ProjectFill_SelectedTrigger_TheFillOpacity_AndA3PxDarkerTopLine()
    {
        Screenshots.RunSta(() =>
        {
            var style = new TabStyleFactory().Create(Base(), TabTreatment.ProjectFill(Yellow, 30), null, UiTheme.Dark);

            var selected = SelectedTrigger(style);
            Assert.Equal(TabItem.IsSelectedProperty, selected.Property);
            Assert.Equal(Argb(Yellow, 30), Fill(selected.Setters));
            Assert.Equal(Colors.White, Solid(SetterValue<Brush>(selected.Setters, Control.ForegroundProperty))); // 30 % yellow over #2A2F37

            var accent = SetterValue<TabDecorationSpec>(selected.Setters, TabDecoration.DecorationProperty);
            Assert.Null(accent.LeftBar);
            Assert.Equal(TabStyleFactory.AccentHeight, accent.TopAccentHeight);
            Assert.Equal(3, accent.TopAccentHeight);
            Assert.Equal(HostTabs.ColorOf(ColorMath.Darken(Yellow, TabStyleFactory.AccentDarken)), Solid(accent.TopAccent!));
            Assert.Equal(ProjectFillShape.Default, accent.TopAccentAnchor); // #30: the line takes the fill's shape
            Assert.IsType<LinearGradientBrush>(accent.TopAccentFadeBrush);

            // Selected comes last, so it wins over the hover trigger when both apply.
            Assert.Same(selected, style.Triggers[^1]);
        });
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(3, 100)]
    [InlineData(6, 30)]
    public void FamilyMarks_AreTheSideBarOfTheWidth_AndTheFixedTopBar_AtTheBarOpacity(int width, int opacity)
    {
        Screenshots.RunSta(() =>
        {
            var original = Base();
            var style = new TabStyleFactory().Create(original, TabTreatment.FamilyBar(Magenta, width, opacity), null, UiTheme.Light);

            Assert.Same(original, style.BasedOn);
            Assert.Empty(style.Triggers); // selection and hover stay native
            var setter = Assert.Single(style.Setters.OfType<Setter>());
            Assert.Equal(TabDecoration.DecorationProperty, setter.Property);
            var bar = (TabDecorationSpec)setter.Value;
            Assert.Equal(width, bar.LeftBarWidth);
            Assert.Equal(Argb(Magenta, opacity), Solid(bar.LeftBar!));
            // #27/#29: the top bar, 2 px whatever the side bar's width, 95 % of the tab, same color and
            // opacity, its last 15 % fading from that color to transparent.
            Assert.Equal(Argb(Magenta, opacity), Solid(bar.TopAccent!));
            Assert.Equal(2, bar.TopAccentHeight);
            Assert.Equal(0.95, bar.TopAccentWidth);
            Assert.Equal(0.15, bar.TopAccentFade);
            var fade = Assert.IsType<LinearGradientBrush>(bar.TopAccentFadeBrush);
            Assert.Equal(Argb(Magenta, opacity), fade.GradientStops[0].Color);
            Assert.Equal(0, fade.GradientStops[1].Color.A);
        });
    }

    /// <summary>#29: the default is option D at 95 %; every option the FnB saw differs only in the shape's numbers.</summary>
    [Fact]
    public void FamilyTopBar_DefaultIsOptionDAt95_AndTheShapeIsAParameter()
    {
        Screenshots.RunSta(() =>
        {
            Assert.Equal(new FamilyTopBarShape(2, 0.95, 1, 0.15, false), FamilyTopBarShape.Default);
            Assert.Equal(FamilyTopBarShape.Default, new TabStyleFactory().FamilyTopBar);

            var matched = new TabStyleFactory(new FamilyTopBarShape(2, 0.9, MatchSideBar: true));
            var spec = SetterValue<TabDecorationSpec>(matched.Create(Base(), TabTreatment.FamilyBar(Magenta, 5, 100), null, UiTheme.Light).Setters, TabDecoration.DecorationProperty);
            Assert.Equal(5, spec.TopAccentHeight);

            var light = new TabStyleFactory(new FamilyTopBarShape(2, 0.9, OpacityShare: 0.6));
            spec = SetterValue<TabDecorationSpec>(light.Create(Base(), TabTreatment.FamilyBar(Magenta, 3, 50), null, UiTheme.Light).Setters, TabDecoration.DecorationProperty);
            Assert.Equal(Argb(Magenta, 50), Solid(spec.LeftBar!));
            Assert.Equal(Argb(Magenta, 0.3), Solid(spec.TopAccent!));

            var faded = new TabStyleFactory(new FamilyTopBarShape(2, 0.9, Fade: 0.15));
            spec = SetterValue<TabDecorationSpec>(faded.Create(Base(), TabTreatment.FamilyBar(Magenta, 3, 100), null, UiTheme.Light).Setters, TabDecoration.DecorationProperty);
            var fade = Assert.IsType<LinearGradientBrush>(spec.TopAccentFadeBrush);
            Assert.True(fade.IsFrozen);
            Assert.Equal(Argb(Magenta, 100), fade.GradientStops[0].Color);
            Assert.Equal(0, fade.GradientStops[1].Color.A);
        });
    }

    /// <summary>#27: the top bar's length is the share of the width, rounded half away from zero.</summary>
    [Theory]
    [InlineData(100, 0.9, 90)]
    [InlineData(155, 0.9, 140)]   // 139.5 → 140
    [InlineData(151, 0.9, 136)]   // 135.9 → 136
    [InlineData(57, 0.9, 51)]     // 51.3 → 51
    [InlineData(80, 1.0, 80)]
    [InlineData(80.5, 1.0, 80.5)] // full width is the width itself
    [InlineData(0, 0.9, 0)]
    [InlineData(100, 0.95, 95)]   // #29
    [InlineData(150, 0.95, 143)]  // 142.5 → 143
    [InlineData(157, 0.95, 149)]  // 149.15 → 149
    public void TopBarWidth_IsTheShareOfTheTab_Rounded(double tabWidth, double share, double expected) =>
        Assert.Equal(expected, TabDecorationAdorner.TopAccentWidthFor(tabWidth, share));

    /// <summary>#29: the fade covers the bar's last 15 %, from a whole pixel.</summary>
    [Theory]
    [InlineData(95, 0.15, 81)]    // 80.75 → 81
    [InlineData(143, 0.15, 122)]  // 121.55 → 122
    [InlineData(100, 0.15, 85)]
    [InlineData(100, 0, 100)]
    [InlineData(0, 0.15, 0)]
    public void TopBarFade_StartsAtTheLast15Percent_Rounded(double length, double fade, double expected) =>
        Assert.Equal(expected, TabDecorationAdorner.TopAccentFadeStartFor(length, fade));

    [Fact]
    public void Styles_AreCachedPerOriginalAndTreatment()
    {
        Screenshots.RunSta(() =>
        {
            var factory = new TabStyleFactory();
            var a = Base();
            var b = Base();
            var fill = TabTreatment.ProjectFill(Blue, 100);

            var first = factory.Create(a, fill, null, UiTheme.Light);
            Assert.Same(first, factory.Create(a, fill, null, UiTheme.Light));
            Assert.NotSame(first, factory.Create(b, fill, null, UiTheme.Light));                  // another original
            Assert.NotSame(first, factory.Create(a, TabTreatment.ProjectFill(Blue, 50), null, UiTheme.Light));
            Assert.NotSame(first, factory.Create(a, TabTreatment.ProjectFill(Yellow, 100), null, UiTheme.Light));
            Assert.NotSame(first, factory.Create(a, fill, null, UiTheme.Dark));                   // another backdrop

            // A bar has no text, so the backdrop does not split its cache entry.
            var bar = TabTreatment.FamilyBar(Magenta, 3, 100);
            Assert.Same(factory.Create(a, bar, null, UiTheme.Light), factory.Create(a, bar, null, UiTheme.Dark));

            factory.ClearCache();
            Assert.Equal(0, factory.CachedCount);
            Assert.NotSame(first, factory.Create(a, fill, null, UiTheme.Light));
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
                factory.Create(new Style(typeof(Button)), TabTreatment.ProjectFill(Blue, 100), null, UiTheme.Light));

            var fromControl = factory.Create(new Style(typeof(Control)), TabTreatment.ProjectFill(Blue, 100), null, UiTheme.Light);
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
            var original = HostTabs.RevitLikeStyle(UiTheme.Light);
            var strip = HostTabs.Strip(original, UiTheme.Light, "Project1 - L1", "Project1 - L2", "Family1 - Ref. Level");
            var root = HostTabs.Host(strip, SplashItDefaults.LightSurface);
            var project = HostTabs.Tab(strip, 0);
            var selectedProject = HostTabs.Tab(strip, 1);
            var family = HostTabs.Tab(strip, 2);
            selectedProject.IsSelected = true;
            HostTabs.Layout(root);
            Assert.Equal(Colors.White, Solid(selectedProject.Background)); // the original's own selected trigger

            var factory = new TabStyleFactory();
            var fill = factory.Create(original, TabTreatment.ProjectFill(Blue, 40), project.Background, UiTheme.Light);
            project.Style = fill;
            selectedProject.Style = fill;
            family.Style = factory.Create(original, TabTreatment.FamilyBar(Magenta, 3, 100), family.Background, UiTheme.Light);
            HostTabs.Layout(root);

            Assert.Equal(Argb(Blue, 20), HostTabs.FillOf(project.Background));          // unselected: 50 % of the 40 % fill
            Assert.Equal(Argb(Blue, 40), HostTabs.FillOf(selectedProject.Background));  // beats the original's white
            Assert.IsType<LinearGradientBrush>(selectedProject.Background);              // #30: it fades before the ×
            Assert.Equal(Colors.Black, Solid(project.Foreground));
            Assert.Null(TabDecoration.FindAdorner(project));
            var accent = TabDecoration.FindAdorner(selectedProject);
            Assert.NotNull(accent);
            Assert.Equal(3, accent!.Spec.TopAccentHeight);

            // The close button is recolored (black, against the light strip); the family's keeps its grey.
            var close = TabCloseGlyph.FindButton(selectedProject)!;
            Assert.Equal(Colors.Black, Solid(close.Foreground));
            Assert.Same(close, TabCloseGlyph.FindRecolored(selectedProject));
            Assert.Equal(HostTabs.CloseGrey, Solid(TabCloseGlyph.FindButton(family)!.Foreground));
            var bar = TabDecoration.FindAdorner(family);
            Assert.NotNull(bar);
            Assert.Equal(3, bar!.Spec.LeftBarWidth);
            Assert.Equal(2, bar.Spec.TopAccentHeight); // #27: the L's top bar is in the same adorner
            Assert.Equal(Colors.Transparent.A, Solid(family.Background).A); // the family's own background is untouched

            // Selection moves: the trigger restyles at once, no new style needed.
            project.IsSelected = true;
            HostTabs.Layout(root);
            Assert.NotNull(TabDecoration.FindAdorner(project));
            Assert.Null(TabDecoration.FindAdorner(selectedProject));
            Assert.Equal(Argb(Blue, 20), HostTabs.FillOf(selectedProject.Background));

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
