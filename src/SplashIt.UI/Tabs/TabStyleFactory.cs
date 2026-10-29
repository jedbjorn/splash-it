using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Colors;

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
    Style Create(Style? basedOn, TabTreatment treatment, Brush? originalBackground, ThemeKind theme);

    /// <summary>Drops every cached style (on a settings change or a theme change).</summary>
    void ClearCache();
}

/// <summary>The colors a project fill shows in each state, all opaque RGB.</summary>
/// <param name="Fill">The fill color; drawn at <paramref name="Opacity"/>.</param>
/// <param name="Opacity">The fill opacity, 0.1 to 1.</param>
/// <param name="Text">Text and close glyph over the fill as shown (black or white).</param>
/// <param name="Hover">The hover fill (lighter); drawn at <paramref name="Opacity"/>.</param>
/// <param name="HoverText">Text over the hover fill as shown.</param>
/// <param name="Selected">The selected tab's fill, drawn at full opacity.</param>
/// <param name="SelectedText">Text over the selected fill.</param>
/// <param name="Accent">The selected tab's 2 px top accent (darker).</param>
public readonly record struct TabFillColors(
    Rgb Fill, double Opacity, Rgb Text, Rgb Hover, Rgb HoverText, Rgb Selected, Rgb SelectedText, Rgb Accent);

/// <summary>
/// The tab style factory (spec, Shapes and settings).
/// <list type="bullet">
/// <item><b>Project fill</b>: the background is the color at the fill opacity; text and the
/// close glyph (<c>Foreground</c>) are black or white, whichever has the higher WCAG contrast
/// against the fill as shown (blended over the tab's own solid background, else over the
/// theme surface). Hover lightens the fill by <see cref="HoverLighten"/>. The selected tab
/// shows the color at full opacity with a <see cref="AccentHeight"/> px accent, darker by
/// <see cref="AccentDarken"/>, along its top edge.</item>
/// <item><b>Family bar</b>: a bar of the bar width on the left edge at the bar opacity, full
/// height. Nothing else changes, so selection and hover stay native.</item>
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

    /// <summary>The selected tab's top accent height, pixels.</summary>
    public const double AccentHeight = 2;

    /// <summary>Above this many cached styles the cache starts over (a safety bound).</summary>
    public const int MaxCached = 1024;

    private static readonly ConditionalWeakTable<Style, object> Derived = new();

    private readonly Dictionary<Key, Style> _cache = new();

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
    public static Rgb BackdropFor(Brush? originalBackground, ThemeKind theme)
    {
        var surface = theme.Surface();
        if (originalBackground is not SolidColorBrush solid) return surface;
        var c = solid.Color;
        return ColorMath.Blend(new Rgb(c.R, c.G, c.B), c.A / 255.0 * solid.Opacity, surface);
    }

    /// <summary>The colors a project fill of <paramref name="color"/> shows over <paramref name="backdrop"/>.</summary>
    public static TabFillColors FillColors(Rgb color, int opacityPercent, Rgb backdrop)
    {
        var opacity = TabTreatment.ProjectFill(color, opacityPercent).Opacity;
        var hover = ColorMath.Lighten(color, HoverLighten);
        return new TabFillColors(
            Fill: color,
            Opacity: opacity,
            Text: ColorMath.TextColorFor(color, opacity, backdrop),
            Hover: hover,
            HoverText: ColorMath.TextColorFor(hover, opacity, backdrop),
            Selected: color,
            SelectedText: ColorMath.TextColorFor(color),
            Accent: ColorMath.Darken(color, AccentDarken));
    }

    public Style Create(Style? basedOn, TabTreatment treatment, Brush? originalBackground, ThemeKind theme)
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
        var colors = FillColors(treatment.Color, treatment.OpacityPercent, backdrop);
        var fill = Brush(colors.Fill, colors.Opacity);
        var text = Brush(colors.Text);
        var hover = Brush(colors.Hover, colors.Opacity);
        var hoverText = Brush(colors.HoverText);
        var selected = Brush(colors.Selected);
        var selectedText = Brush(colors.SelectedText);
        var accent = new TabDecorationSpec(null, 0, Brush(colors.Accent), AccentHeight);

        // Plain setters: the state before any trigger applies.
        style.Setters.Add(new Setter(Control.BackgroundProperty, fill));
        style.Setters.Add(new Setter(Control.ForegroundProperty, text));

        // 1. Not selected: the fill. A trigger, so it outranks the base style's triggers.
        var normal = new Trigger { Property = TabItem.IsSelectedProperty, Value = false };
        normal.Setters.Add(new Setter(Control.BackgroundProperty, fill));
        normal.Setters.Add(new Setter(Control.ForegroundProperty, text));
        style.Triggers.Add(normal);

        // 2. Hover, not selected: lighter.
        var hoverTrigger = new MultiTrigger();
        hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        hoverTrigger.Conditions.Add(new Condition(TabItem.IsSelectedProperty, false));
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hover));
        hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, hoverText));
        style.Triggers.Add(hoverTrigger);

        // 3. Selected: full opacity and the darker top accent.
        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selected));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, selectedText));
        selectedTrigger.Setters.Add(new Setter(TabDecoration.DecorationProperty, accent));
        style.Triggers.Add(selectedTrigger);
    }

    private static void AddFamilyBar(Style style, TabTreatment treatment)
    {
        var bar = new TabDecorationSpec(Brush(treatment.Color, treatment.Opacity), treatment.BarWidth, null, 0);
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
