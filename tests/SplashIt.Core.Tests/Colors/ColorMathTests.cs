using SplashIt.Core.Colors;

namespace SplashIt.Core.Tests.Colors;

/// <summary>Hex parsing and formatting, blending, lightening and darkening, WCAG contrast, text choice.</summary>
public class ColorMathTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#4f8ef7", 0x4F, 0x8E, 0xF7)]
    [InlineData("#4F8EF7", 0x4F, 0x8E, 0xF7)]
    public void TryParseHex_AcceptsHashAndSixHexDigits(string text, byte r, byte g, byte b)
    {
        Assert.True(ColorMath.TryParseHex(text, out var c));
        Assert.Equal(new Rgb(r, g, b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4F8EF7")]
    [InlineData("#4F8EF")]
    [InlineData("#4F8EF70")]
    [InlineData("#FF4F8EF7")]
    [InlineData(" #4F8EF7")]
    [InlineData("#4F8EF7 ")]
    [InlineData("#GGGGGG")]
    [InlineData("#+12345")]
    [InlineData("#４F8EF7")] // a full-width digit is not ASCII hex
    public void TryParseHex_RejectsAnythingElse(string? text) =>
        Assert.False(ColorMath.TryParseHex(text, out _));

    [Fact]
    public void ToHex_IsUpperCaseHashRrggbb() =>
        Assert.Equal("#0A0BFF", ColorMath.ToHex(new Rgb(10, 11, 255)));

    [Fact]
    public void Blend_ComposesOverTheBackdrop()
    {
        var red = Rgb.Parse("#FF0000");
        Assert.Equal(red, ColorMath.Blend(red, 1.0, ColorMath.White));
        Assert.Equal(ColorMath.White, ColorMath.Blend(red, 0.0, ColorMath.White));
        Assert.Equal(Rgb.Parse("#FF8080"), ColorMath.Blend(red, 0.5, ColorMath.White));
        Assert.Equal(Rgb.Parse("#800000"), ColorMath.Blend(red, 0.5, ColorMath.Black));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Blend_ClampsOpacityBelowZero(double opacity) =>
        Assert.Equal(ColorMath.White, ColorMath.Blend(ColorMath.Black, opacity, ColorMath.White));

    [Fact]
    public void Blend_ClampsOpacityAboveOne() =>
        Assert.Equal(ColorMath.Black, ColorMath.Blend(ColorMath.Black, 2.0, ColorMath.White));

    [Fact]
    public void LightenAndDarken_MoveTowardWhiteAndBlack()
    {
        var c = Rgb.Parse("#4080C0");
        Assert.Equal(c, ColorMath.Lighten(c, 0));
        Assert.Equal(ColorMath.White, ColorMath.Lighten(c, 1));
        Assert.Equal(Rgb.Parse("#A0C0E0"), ColorMath.Lighten(c, 0.5));
        Assert.Equal(c, ColorMath.Darken(c, 0));
        Assert.Equal(ColorMath.Black, ColorMath.Darken(c, 1));
        Assert.Equal(Rgb.Parse("#204060"), ColorMath.Darken(c, 0.5));
        Assert.True(ColorMath.RelativeLuminance(ColorMath.Lighten(c, 0.2)) > ColorMath.RelativeLuminance(c));
        Assert.True(ColorMath.RelativeLuminance(ColorMath.Darken(c, 0.2)) < ColorMath.RelativeLuminance(c));
    }

    [Fact]
    public void Contrast_MatchesWcagReferenceValues()
    {
        Assert.Equal(21.0, ColorMath.ContrastRatio(ColorMath.Black, ColorMath.White), 6);
        Assert.Equal(21.0, ColorMath.ContrastRatio(ColorMath.White, ColorMath.Black), 6);
        Assert.Equal(1.0, ColorMath.ContrastRatio(ColorMath.White, ColorMath.White), 6);
        // #767676 on white is the classic 4.54:1 AA threshold color.
        Assert.Equal(4.54, ColorMath.ContrastRatio(Rgb.Parse("#767676"), ColorMath.White), 2);
        Assert.Equal(0.0, ColorMath.RelativeLuminance(ColorMath.Black), 6);
        Assert.Equal(1.0, ColorMath.RelativeLuminance(ColorMath.White), 6);
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#EDC948", "#000000")] // yellow
    [InlineData("#2A2F37", "#FFFFFF")] // dark surface
    [InlineData("#4F8EF7", "#000000")] // Designs/OS blue: black 6.5:1 beats white 3.2:1
    [InlineData("#1F4E9C", "#FFFFFF")]
    public void TextColorFor_PicksTheHigherContrast(string background, string expected) =>
        Assert.Equal(expected, ColorMath.TextColorFor(Rgb.Parse(background)).ToHex());

    [Fact]
    public void TextColorFor_UsesTheColorAsShown()
    {
        var navy = Rgb.Parse("#1F2A60");
        // Opaque navy wants white text; at 20 % over white it is a pale tint that wants black.
        Assert.Equal(ColorMath.White, ColorMath.TextColorFor(navy, 1.0, ColorMath.White));
        Assert.Equal(ColorMath.Black, ColorMath.TextColorFor(navy, 0.2, ColorMath.White));
        // The same 20 % tint over the dark surface stays dark and wants white.
        Assert.Equal(ColorMath.White, ColorMath.TextColorFor(navy, 0.2, Rgb.Parse("#2A2F37")));
    }

    [Fact]
    public void TextColorFor_ATie_GoesToBlack()
    {
        // Find the grey where black and white contrast are closest; the rule must be deterministic.
        foreach (var v in Enumerable.Range(0, 256))
        {
            var grey = new Rgb((byte)v, (byte)v, (byte)v);
            var black = ColorMath.ContrastRatio(ColorMath.Black, grey);
            var white = ColorMath.ContrastRatio(ColorMath.White, grey);
            Assert.Equal(black >= white ? ColorMath.Black : ColorMath.White, ColorMath.TextColorFor(grey));
        }
    }

    [Fact]
    public void Rgb_LenientParse_StillWorks()
    {
        Assert.True(Rgb.TryParse(" 4f8ef7 ", out var c));
        Assert.Equal("#4F8EF7", c.ToString());
        Assert.Throws<FormatException>(() => Rgb.Parse("nope"));
    }
}
