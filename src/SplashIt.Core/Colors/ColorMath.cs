// The luminance, contrast and compositing formulas are adapted from gantt-it
// src/GanttIt.Core/Theming/UiPalette.cs (UiColor.Over, RelativeLuminance,
// ContrastRatio; gantt-it commit 1e27832, MIT, same owner).

using System.Globalization;

namespace SplashIt.Core.Colors;

/// <summary>
/// Color arithmetic for tab styling: strict <c>#RRGGBB</c> parsing and formatting,
/// alpha blending, lightening and darkening, WCAG 2.x contrast, and the
/// black-or-white text choice. Pure and culture-invariant.
/// </summary>
public static class ColorMath
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>
    /// Parses exactly <c>#RRGGBB</c>: a leading <c>#</c> and six hex digits, either
    /// case, nothing else (no whitespace, no alpha). The settings and export formats use this.
    /// </summary>
    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = default;
        if (text is null || text.Length != 7 || text[0] != '#') return false;
        for (var i = 1; i < 7; i++)
            if (!char.IsAsciiHexDigit(text[i])) return false;
        var v = uint.Parse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Formats as upper-case <c>#RRGGBB</c>.</summary>
    public static string ToHex(Rgb color) => color.ToHex();

    /// <summary>
    /// <paramref name="color"/> at <paramref name="opacity"/> (0 to 1, clamped)
    /// composited over the opaque <paramref name="backdrop"/>: what the eye sees.
    /// </summary>
    public static Rgb Blend(Rgb color, double opacity, Rgb backdrop)
    {
        var a = Clamp01(opacity);
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a), MidpointRounding.AwayFromZero);
        return new Rgb(Mix(color.R, backdrop.R), Mix(color.G, backdrop.G), Mix(color.B, backdrop.B));
    }

    /// <summary>Moves <paramref name="color"/> toward white by <paramref name="amount"/> (0 = unchanged, 1 = white).</summary>
    public static Rgb Lighten(Rgb color, double amount) => Blend(White, amount, color);

    /// <summary>Moves <paramref name="color"/> toward black by <paramref name="amount"/> (0 = unchanged, 1 = black).</summary>
    public static Rgb Darken(Rgb color, double amount) => Blend(Black, amount, color);

    /// <summary>WCAG 2.x relative luminance, 0 (black) to 1 (white).</summary>
    public static double RelativeLuminance(Rgb color)
    {
        static double Channel(byte c)
        {
            var s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG 2.x contrast ratio of two opaque colors, 1 to 21. Symmetric.</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Black or white, whichever has the higher WCAG contrast against the opaque
    /// <paramref name="background"/>. A tie goes to black.
    /// </summary>
    public static Rgb TextColorFor(Rgb background) =>
        ContrastRatio(Black, background) >= ContrastRatio(White, background) ? Black : White;

    /// <summary>
    /// The text color for <paramref name="fill"/> at <paramref name="opacity"/> (0 to 1)
    /// as actually shown over <paramref name="backdrop"/> (the tab's own solid
    /// background, else the theme's surface).
    /// </summary>
    public static Rgb TextColorFor(Rgb fill, double opacity, Rgb backdrop) =>
        TextColorFor(Blend(fill, opacity, backdrop));

    /// <summary>
    /// Black or white for text that crosses every one of <paramref name="backgrounds"/> (opaque):
    /// whichever has the higher <em>minimum</em> WCAG contrast over them. A tie, or no background,
    /// goes to black. With one background this is <see cref="TextColorFor(Rgb)"/>.
    /// </summary>
    public static Rgb TextColorForAll(params Rgb[] backgrounds)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        double black = double.MaxValue, white = double.MaxValue;
        foreach (var background in backgrounds)
        {
            black = Math.Min(black, ContrastRatio(Black, background));
            white = Math.Min(white, ContrastRatio(White, background));
        }
        return black >= white ? Black : White;
    }

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
}
