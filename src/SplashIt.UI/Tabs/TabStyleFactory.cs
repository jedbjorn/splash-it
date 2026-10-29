using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;
using SplashIt.Core.Theming;

namespace SplashIt.UI.Tabs;

/// <summary>
/// Builds the styles that color a tab. The colorizer uses it on Revit's tabs, and the
/// settings window's preview strip (Lane C) uses the same factory on plain WPF tabs, so
/// the preview is what the tabs get.
/// </summary>
public interface ITabStyleFactory
{
    /// <summary>
    /// A sealed style <c>BasedOn</c> <paramref name="basedOn"/> (the tab's original style;
    /// null for none) that applies <paramref name="treatment"/>. Text contrast is measured
    /// against <paramref name="originalBackground"/> (the tab's background under its
    /// original style) when it is a <see cref="SolidColorBrush"/>, else against
    /// <paramref name="theme"/>'s surface token. The same inputs return the same instance.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="basedOn"/> targets a type a <see cref="TabItem"/> style cannot derive
    /// from (see <see cref="TabStyleFactory.CanDerive"/>).
    /// </exception>
    Style Create(Style? basedOn, TabTreatment treatment, Brush? originalBackground, UiTheme theme);

    /// <summary>Drops every cached style (on a settings change or a theme change).</summary>
    void ClearCache();
}

/// <summary>The colors a project fill shows in each state (spec, Shapes and settings, #22).</summary>
/// <param name="Fill">The project color: an unselected tab draws it at <paramref name="MutedOpacity"/>.</param>
/// <param name="Opacity">The fill opacity (0.1 to 1): what the selected tab draws.</param>
/// <param name="MutedOpacity">The unselected tabs' opacity: the Unselected tab opacity share of <paramref name="Opacity"/> (#23).</param>
/// <param name="Text">Text and close glyph on an unselected tab, over the fill as shown (black or white).</param>
/// <param name="Hover">The hover fill (lighter); drawn at <paramref name="MutedOpacity"/>.</param>
/// <param name="HoverText">Text and close glyph over the hover fill as shown.</param>
/// <param name="Selected">The selected tab's fill color, drawn at <paramref name="Opacity"/>.</param>
/// <param name="SelectedText">Text and close glyph over the selected fill as shown.</param>
/// <param name="Accent">The selected tab's 3 px top line (darker, opaque).</param>
public readonly record struct TabFillColors(
    Rgb Fill, double Opacity, double MutedOpacity, Rgb Text, Rgb Hover, Rgb HoverText, Rgb Selected, Rgb SelectedText, Rgb Accent);

/// <summary>
/// The family top bar's shape (FnB decision #27): not a setting, a constant of the look. The
/// shapes the FnB was shown (option sheet, SC-004) differ only in these numbers, so choosing
/// another is a change to <see cref="Default"/> alone.
/// </summary>
/// <param name="Height">The top bar's height, pixels (ignored when <paramref name="MatchSideBar"/>).</param>
/// <param name="Width">The share of the tab's width it spans from the left edge (0 to 1).</param>
/// <param name="OpacityShare">Its opacity as a share of the family bar opacity (0 to 1).</param>
/// <param name="Fade">The share of its length, at the right end, that fades out (0 = none).</param>
/// <param name="MatchSideBar">True: as high as the side bar is wide.</param>
public readonly record struct FamilyTopBarShape(double Height, double Width, double OpacityShare = 1, double Fade = 0, bool MatchSideBar = false)
{
    /// <summary>
    /// The shape in use, #27 (option A): 2 px high, from the left edge, 90 % of the tab's width,
    /// at the family bar opacity.
    /// </summary>
    public static FamilyTopBarShape Default { get; } = new(Height: 2, Width: 0.9);

    /// <summary>The top bar's height on a tab whose side bar is <paramref name="sideBarWidth"/> px wide.</summary>
    public double HeightFor(int sideBarWidth) => MatchSideBar ? sideBarWidth : Height;
}

/// <summary>
/// The tab style factory (spec, Shapes and settings).
/// <list type="bullet">
/// <item><b>Project fill</b> (FnB decisions #22 and #23): the selected tab shows the color at the
/// fill opacity with a <see cref="AccentHeight"/> px line, darker by <see cref="AccentDarken"/>,
/// along its top edge; the project's other tabs show the same color at the Unselected tab
/// opacity (<see cref="TabTreatment.UnselectedPercent"/> of the fill opacity, default 50 %), so
/// the active view stands out. At 100 % only the top line marks the selected tab. Text and the close glyph are black or
/// white, whichever has the higher WCAG contrast against the fill as shown in that state
/// (blended over the tab's own solid background, else over the theme surface). Hover lightens
/// an unselected tab's fill by <see cref="HoverLighten"/>.</item>
/// <item><b>Family marks</b> (#27): a bar of the bar width on the left edge at the bar opacity,
/// full height, plus a fixed top bar (<see cref="FamilyTopBarShape.Default"/>: 2 px high, from
/// the left edge, 90 % of the tab's width) in the same color and opacity, so the two read as
/// an L. Only the side bar's width is a setting. Nothing else changes, so selection and hover
/// stay native.</item>
/// </list>
/// <para>
/// <b>Mechanism (AC4; Revit's template is confirmed only by AC11).</b> Every style is
/// <c>BasedOn</c> the tab's original style, so the original template, close button and
/// theme still apply, and the style is the only thing assigned to a tab, so restoring the
/// original style undoes everything.
/// </para>
/// <list type="number">
/// <item>The fill is a <c>Background</c> setter and the text a <c>Foreground</c> setter,
/// because the fill has to sit under the text and only the template can put it there;
/// a typical TabItem template binds its border's background to <c>Background</c> and its
/// text and glyph to <c>Foreground</c>.</item>
/// <item>The three fill states are also style triggers (not selected; hover and not
/// selected; selected). A style trigger outranks a plain setter, and a derived style's
/// triggers come after its base style's, so the fill still wins where the original style
/// sets its own background in a trigger (as AvalonDock-style tab styles do for the
/// selected and hover states). It cannot win against a template trigger that paints a
/// template part directly; WPF's own Aero2 template does that for selection, and the
/// <c>tabs-aero2-*</c> screenshots show the result.</item>
/// <item>The family bar and the selected tab's top accent are drawn by
/// <see cref="TabDecoration"/>, an adorner that does not depend on the template at all.</item>
/// <item>The close glyph: Revit's close button (<c>mDocumentCloseButton</c>, inside
/// AvalonDock's <c>LayoutDocumentTabItem</c>) has its own fixed grey and does not follow the
/// tab's <c>Foreground</c>, so the same black or white is also set through
/// <see cref="TabCloseGlyph.ForegroundProperty"/>, per state, which recolors that button and
/// puts its own color back when the property clears (the original style is restored).</item>
/// </list>
/// The mechanism sits behind <see cref="ITabStyleFactory"/>, and the decoration behind one
/// attached property, so either can be swapped after AC11 without touching the colorizer.
/// <para>
/// Styles are cached per (original style, shape, color, opacity or width and bar opacity,
/// contrast backdrop). The spec's key also lists "selected": here the selected look is an
/// <c>IsSelected</c> trigger inside the one style, so a click restyles the tab at once,
/// with no colorizer pass and no second style. UI thread only.
/// </para>
/// </summary>
public sealed class TabStyleFactory : ITabStyleFactory
{
    /// <summary>How far hover moves the fill toward white.</summary>
    public const double HoverLighten = 0.25;

    /// <summary>How far the selected tab's top accent moves the fill toward black.</summary>
    public const double AccentDarken = 0.35;

    /// <summary>The selected tab's top line height, pixels (#22).</summary>
    public const double AccentHeight = 3;

    /// <summary>Above this many cached styles the cache starts over (a safety bound).</summary>
    public const int MaxCached = 1024;

    private static readonly ConditionalWeakTable<Style, object> Derived = new();

    private readonly Dictionary<Key, Style> _cache = new();

    /// <param name="familyTopBar">The family top bar's shape; <see cref="FamilyTopBarShape.Default"/> when null
    /// (another is given only to render the options the FnB chose from).</param>
    public TabStyleFactory(FamilyTopBarShape? familyTopBar = null) =>
        FamilyTopBar = familyTopBar ?? FamilyTopBarShape.Default;

    /// <summary>The family top bar's shape this factory draws (#27).</summary>
    public FamilyTopBarShape FamilyTopBar { get; }

    /// <summary>The number of cached styles.</summary>
    public int CachedCount => _cache.Count;

    /// <summary>True for a style this factory (any instance) created.</summary>
    public static bool IsDerived(Style? style) => style is not null && Derived.TryGetValue(style, out _);

    /// <summary>
    /// Whether a TabItem style can be <c>BasedOn</c> <paramref name="basedOn"/>: null, or a
    /// style whose target type is a <see cref="TabItem"/> type or a base of one.
    /// </summary>
    public static bool CanDerive(Style? basedOn) => TargetTypeFor(basedOn) is not null;

    /// <summary>
    /// The opaque color tab text is measured against: <paramref name="originalBackground"/>
    /// composited over <paramref name="theme"/>'s surface when it is a solid brush (its alpha
    /// and opacity count), else the surface itself.
    /// </summary>
    public static Rgb BackdropFor(Brush? originalBackground, UiTheme theme)
    {
        var surface = theme.Surface();
        if (originalBackground is not SolidColorBrush solid) return surface;
        var c = solid.Color;
        return ColorMath.Blend(new Rgb(c.R, c.G, c.B), c.A / 255.0 * solid.Opacity, surface);
    }

    /// <summary>The colors a project fill of <paramref name="color"/> shows over <paramref name="backdrop"/>.</summary>
    public static TabFillColors FillColors(Rgb color, int opacityPercent, Rgb backdrop,
        int unselectedPercent = TabTreatment.DefaultUnselectedPercent) =>
        FillColors(TabTreatment.ProjectFill(color, opacityPercent, unselectedPercent), backdrop);

    /// <summary>The colors a project <paramref name="treatment"/> shows over <paramref name="backdrop"/>.</summary>
    public static TabFillColors FillColors(TabTreatment treatment, Rgb backdrop)
    {
        var color = treatment.Color;
        var opacity = treatment.Opacity;
        var muted = treatment.UnselectedOpacity;
        var hover = ColorMath.Lighten(color, HoverLighten);
        return new TabFillColors(
            Fill: color,
            Opacity: opacity,
            MutedOpacity: muted,
            Text: ColorMath.TextColorFor(color, muted, backdrop),
            Hover: hover,
            HoverText: ColorMath.TextColorFor(hover, muted, backdrop),
            Selected: color,
            SelectedText: ColorMath.TextColorFor(color, opacity, backdrop),
            Accent: ColorMath.Darken(color, AccentDarken));
    }

    public Style Create(Style? basedOn, TabTreatment treatment, Brush? originalBackground, UiTheme theme)
    {
        var target = TargetTypeFor(basedOn)
            ?? throw new ArgumentException(
                $"A TabItem style cannot be based on a style for {basedOn!.TargetType.Name}.", nameof(basedOn));
        // A family bar has no text over it, so the backdrop does not change its style.
        var backdrop = treatment.Kind == TabTreatmentKind.FamilyBar ? default : BackdropFor(originalBackground, theme);
        var key = new Key(basedOn, treatment, backdrop);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var style = new Style(target) { BasedOn = basedOn };
        if (treatment.Kind == TabTreatmentKind.FamilyBar) AddFamilyBar(style, treatment);
        else AddProjectFill(style, treatment, backdrop);
        style.Seal();

        Derived.AddOrUpdate(style, treatment);
        if (_cache.Count >= MaxCached) _cache.Clear();
        _cache.Add(key, style);
        return style;
    }

    public void ClearCache() => _cache.Clear();

    private static void AddProjectFill(Style style, TabTreatment treatment, Rgb backdrop)
    {
        var colors = FillColors(treatment, backdrop);
        var fill = Brush(colors.Fill, colors.MutedOpacity);
        var text = Brush(colors.Text);
        var hover = Brush(colors.Hover, colors.MutedOpacity);
        var hoverText = Brush(colors.HoverText);
        var selected = Brush(colors.Selected, colors.Opacity);
        var selectedText = Brush(colors.SelectedText);
        var accent = new TabDecorationSpec(null, 0, Brush(colors.Accent), AccentHeight);

        // Plain setters: the state before any trigger applies.
        AddState(style.Setters, fill, text);

        // 1. Not selected: the muted fill. A trigger, so it outranks the base style's triggers.
        var normal = new Trigger { Property = TabItem.IsSelectedProperty, Value = false };
        AddState(normal.Setters, fill, text);
        style.Triggers.Add(normal);

        // 2. Hover, not selected: lighter.
        var hoverTrigger = new MultiTrigger();
        hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        hoverTrigger.Conditions.Add(new Condition(TabItem.IsSelectedProperty, false));
        AddState(hoverTrigger.Setters, hover, hoverText);
        style.Triggers.Add(hoverTrigger);

        // 3. Selected: the fill opacity and the darker top line.
        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        AddState(selectedTrigger.Setters, selected, selectedText);
        selectedTrigger.Setters.Add(new Setter(TabDecoration.DecorationProperty, accent));
        style.Triggers.Add(selectedTrigger);
    }

    /// <summary>One state's fill, text, and close glyph (the glyph follows the text, #22).</summary>
    private static void AddState(SetterBaseCollection setters, Brush fill, Brush text)
    {
        setters.Add(new Setter(Control.BackgroundProperty, fill));
        setters.Add(new Setter(Control.ForegroundProperty, text));
        setters.Add(new Setter(TabCloseGlyph.ForegroundProperty, text));
    }

    /// <summary>The family's L (#27): the side bar of the bar width, and the top bar of <see cref="FamilyTopBar"/>.</summary>
    private void AddFamilyBar(Style style, TabTreatment treatment)
    {
        var shape = FamilyTopBar;
        var bar = new TabDecorationSpec(
            Brush(treatment.Color, treatment.Opacity), treatment.BarWidth,
            Brush(treatment.Color, treatment.Opacity * Math.Clamp(shape.OpacityShare, 0, 1)), shape.HeightFor(treatment.BarWidth),
            shape.Width, shape.Fade);
        style.Setters.Add(new Setter(TabDecoration.DecorationProperty, bar));
    }

    private static Type? TargetTypeFor(Style? basedOn)
    {
        var baseType = basedOn?.TargetType ?? typeof(TabItem);
        if (typeof(TabItem).IsAssignableFrom(baseType)) return baseType;
        return baseType.IsAssignableFrom(typeof(TabItem)) ? typeof(TabItem) : null;
    }

    private static SolidColorBrush Brush(Rgb color, double opacity = 1)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private readonly struct Key(Style? basedOn, TabTreatment treatment, Rgb backdrop) : IEquatable<Key>
    {
        private readonly Style? _basedOn = basedOn;
        private readonly TabTreatment _treatment = treatment;
        private readonly Rgb _backdrop = backdrop;

        public bool Equals(Key other) =>
            ReferenceEquals(_basedOn, other._basedOn) && _treatment == other._treatment && _backdrop == other._backdrop;

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_basedOn is null ? 0 : RuntimeHelpers.GetHashCode(_basedOn), _treatment, _backdrop);
    }
}
