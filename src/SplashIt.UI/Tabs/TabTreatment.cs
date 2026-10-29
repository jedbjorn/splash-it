using SplashIt.Core.Colors;
using SplashIt.Core.Settings;

namespace SplashIt.UI.Tabs;

/// <summary>The two tab shapes (decision #10).</summary>
public enum TabTreatmentKind
{
    /// <summary>A project's tab: a fill, solid then fading out before the close × (#30).</summary>
    ProjectFill = 0,

    /// <summary>A family's tab: a bar on the left edge plus a fixed top bar (#27); everything else native.</summary>
    FamilyBar,
}

/// <summary>
/// How one tab is colored: the shape, the color, and its numbers. Opacities are whole
/// percents (10 to 100) and the bar width whole device-independent pixels (1 to 6), as
/// in <see cref="SplashItSettings"/>. Out-of-range numbers are clamped by the
/// constructors, so a treatment is always drawable.
/// </summary>
public readonly record struct TabTreatment
{
    /// <summary>The default Unselected tab opacity (#23), percent of the fill opacity: <see cref="SplashItDefaults.ProjectUnselectedOpacity"/>.</summary>
    public const int DefaultUnselectedPercent = SplashItDefaults.ProjectUnselectedOpacity;

    private TabTreatment(TabTreatmentKind kind, Rgb color, int opacityPercent, int barWidth, int unselectedPercent)
    {
        Kind = kind;
        Color = color;
        OpacityPercent = Math.Clamp(opacityPercent, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);
        BarWidth = kind == TabTreatmentKind.FamilyBar
            ? Math.Clamp(barWidth, SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth)
            : 0;
        UnselectedPercent = kind == TabTreatmentKind.ProjectFill
            ? Math.Clamp(unselectedPercent, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity)
            : 0;
    }

    public TabTreatmentKind Kind { get; }

    public Rgb Color { get; }

    /// <summary>The project fill opacity, or the family bar opacity, in percent.</summary>
    public int OpacityPercent { get; }

    /// <summary>The family bar width in pixels; 0 for a project fill.</summary>
    public int BarWidth { get; }

    /// <summary>
    /// A project's Unselected tab opacity (#23): the share of the fill opacity its unselected
    /// tabs show, percent (10 to 100); 0 for a family bar.
    /// </summary>
    public int UnselectedPercent { get; }

    /// <summary>The opacity as 0.1 to 1.</summary>
    public double Opacity => OpacityPercent / 100.0;

    /// <summary>A project's unselected tabs' opacity, 0.01 to 1: the fill opacity times <see cref="UnselectedPercent"/>.</summary>
    public double UnselectedOpacity => Opacity * UnselectedPercent / 100.0;

    /// <summary>
    /// A project's fill: the selected tab at <paramref name="fillOpacityPercent"/>, the others at
    /// <paramref name="unselectedPercent"/> of it (#23).
    /// </summary>
    public static TabTreatment ProjectFill(Rgb color, int fillOpacityPercent, int unselectedPercent = DefaultUnselectedPercent) =>
        new(TabTreatmentKind.ProjectFill, color, fillOpacityPercent, 0, unselectedPercent);

    /// <summary>A family's left bar, <paramref name="barWidth"/> pixels wide at <paramref name="barOpacityPercent"/>.</summary>
    public static TabTreatment FamilyBar(Rgb color, int barWidth, int barOpacityPercent) =>
        new(TabTreatmentKind.FamilyBar, color, barOpacityPercent, barWidth, 0);

    /// <summary>The treatment <paramref name="settings"/> give a project in <paramref name="color"/>.</summary>
    public static TabTreatment ProjectFill(Rgb color, SplashItSettings settings) =>
        ProjectFill(color, settings.ProjectFillOpacity, settings.ProjectUnselectedOpacity);

    /// <summary>The treatment <paramref name="settings"/> give a family in <paramref name="color"/>.</summary>
    public static TabTreatment FamilyBar(Rgb color, SplashItSettings settings) =>
        FamilyBar(color, settings.FamilyBarWidth, settings.FamilyBarOpacity);
}
