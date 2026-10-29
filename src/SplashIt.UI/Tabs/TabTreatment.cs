using SplashIt.Core.Colors;
using SplashIt.Core.Settings;

namespace SplashIt.UI.Tabs;

/// <summary>The two tab shapes (decision #10).</summary>
public enum TabTreatmentKind
{
    /// <summary>A project's tab: a solid fill.</summary>
    ProjectFill = 0,

    /// <summary>A family's tab: a bar on the left edge; everything else native.</summary>
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
    private TabTreatment(TabTreatmentKind kind, Rgb color, int opacityPercent, int barWidth)
    {
        Kind = kind;
        Color = color;
        OpacityPercent = Math.Clamp(opacityPercent, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity);
        BarWidth = kind == TabTreatmentKind.FamilyBar
            ? Math.Clamp(barWidth, SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth)
            : 0;
    }

    public TabTreatmentKind Kind { get; }

    public Rgb Color { get; }

    /// <summary>The project fill opacity, or the family bar opacity, in percent.</summary>
    public int OpacityPercent { get; }

    /// <summary>The family bar width in pixels; 0 for a project fill.</summary>
    public int BarWidth { get; }

    /// <summary>The opacity as 0.1 to 1.</summary>
    public double Opacity => OpacityPercent / 100.0;

    /// <summary>A project's solid fill at <paramref name="fillOpacityPercent"/>.</summary>
    public static TabTreatment ProjectFill(Rgb color, int fillOpacityPercent) =>
        new(TabTreatmentKind.ProjectFill, color, fillOpacityPercent, 0);

    /// <summary>A family's left bar, <paramref name="barWidth"/> pixels wide at <paramref name="barOpacityPercent"/>.</summary>
    public static TabTreatment FamilyBar(Rgb color, int barWidth, int barOpacityPercent) =>
        new(TabTreatmentKind.FamilyBar, color, barOpacityPercent, barWidth);

    /// <summary>The treatment <paramref name="settings"/> give a project in <paramref name="color"/>.</summary>
    public static TabTreatment ProjectFill(Rgb color, SplashItSettings settings) =>
        ProjectFill(color, settings.ProjectFillOpacity);

    /// <summary>The treatment <paramref name="settings"/> give a family in <paramref name="color"/>.</summary>
    public static TabTreatment FamilyBar(Rgb color, SplashItSettings settings) =>
        FamilyBar(color, settings.FamilyBarWidth, settings.FamilyBarOpacity);
}
