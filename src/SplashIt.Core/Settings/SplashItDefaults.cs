using SplashIt.Core.Colors;

namespace SplashIt.Core.Settings;

/// <summary>
/// The default settings and the valid ranges (spec, Shapes and settings).
/// </summary>
public static class SplashItDefaults
{
    /// <summary>The settings and export schema this version reads and writes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Coloring is on with no settings file (decision #6).</summary>
    public const bool Enabled = true;

    public const int MinColors = 1;
    public const int MaxColors = 12;

    public const int MinOpacity = 10;
    public const int MaxOpacity = 100;

    public const int MinBarWidth = 1;
    public const int MaxBarWidth = 6;

    /// <summary>Project fill opacity, percent.</summary>
    public const int ProjectFillOpacity = 100;

    /// <summary>
    /// Unselected tab opacity (decision #23), percent of <see cref="ProjectFillOpacity"/>: a
    /// project's unselected tabs show its color at fill × this share; the selected tab at the full
    /// fill. Also the value read when an older settings file has no such field.
    /// </summary>
    public const int ProjectUnselectedOpacity = 50;

    /// <summary>Family bar width, device-independent pixels.</summary>
    public const int FamilyBarWidth = 3;

    /// <summary>Family bar opacity, percent.</summary>
    public const int FamilyBarOpacity = 100;

    /// <summary>
    /// The light theme's surface token, <c>#FFFFFF</c>: the backdrop for text
    /// contrast when a tab's own background is not a solid brush. Matches gantt-it
    /// src/GanttIt.Core/Theming/UiPalette.cs <c>SurfaceBrush</c> (gantt-it commit 1e27832).
    /// </summary>
    public static readonly Rgb LightSurface = Rgb.Parse("#FFFFFF");

    /// <summary>The dark theme's surface token, <c>#2A2F37</c> (same source as <see cref="LightSurface"/>).</summary>
    public static readonly Rgb DarkSurface = Rgb.Parse("#2A2F37");

    /// <summary>
    /// Project colors: gantt-it's 12-swatch palette in its order, Designs/OS blue
    /// first. Copied from gantt-it src/GanttIt.Core/Model/Palette.cs <c>Palette.Default</c>
    /// (gantt-it commit 1e27832).
    /// </summary>
    public static readonly IReadOnlyList<Rgb> ProjectColors = Array.AsReadOnly(new[]
    {
        Rgb.Parse("#4F8EF7"), // blue
        Rgb.Parse("#F28E2B"), // orange
        Rgb.Parse("#59A14F"), // green
        Rgb.Parse("#E15759"), // red
        Rgb.Parse("#B07AA1"), // purple
        Rgb.Parse("#76B7B2"), // teal
        Rgb.Parse("#EDC948"), // yellow
        Rgb.Parse("#FF9DA7"), // pink
        Rgb.Parse("#9C755F"), // brown
        Rgb.Parse("#BAB0AC"), // warm grey
        Rgb.Parse("#8CD17D"), // light green
        Rgb.Parse("#499894"), // dark teal
    });

    /// <summary>
    /// Family colors: twelve saturated mid-tones, none of them in the project set.
    /// A family shows only a thin bar, so each reaches at least 3:1 (WCAG non-text)
    /// against both theme surfaces, <see cref="LightSurface"/> and <see cref="DarkSurface"/>;
    /// that window is narrow (relative luminance about 0.18 to 0.30), so the colors
    /// differ by hue. Neighbors alternate warm and cool so the first few read apart.
    /// </summary>
    public static readonly IReadOnlyList<Rgb> FamilyColors = Array.AsReadOnly(new[]
    {
        Rgb.Parse("#D447BD"), // magenta      3.86:1 light, 3.48:1 dark
        Rgb.Parse("#1A8BBF"), // cerulean     3.83:1, 3.52:1
        Rgb.Parse("#B8770B"), // ochre        3.70:1, 3.64:1
        Rgb.Parse("#13937A"), // sea green    3.83:1, 3.51:1
        Rgb.Parse("#9466F2"), // violet       3.86:1, 3.49:1
        Rgb.Parse("#E0481D"), // vermilion    4.11:1, 3.28:1
        Rgb.Parse("#8A8A12"), // olive        3.67:1, 3.67:1
        Rgb.Parse("#E8408A"), // raspberry    3.80:1, 3.55:1
        Rgb.Parse("#7072F5"), // indigo       3.88:1, 3.47:1
        Rgb.Parse("#2E9632"), // green        3.80:1, 3.54:1
        Rgb.Parse("#B458DC"), // orchid       3.91:1, 3.44:1
        Rgb.Parse("#E8365E"), // crimson      4.10:1, 3.28:1
    });
}
