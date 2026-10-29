using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using Xunit.Abstractions;

namespace SplashIt.Core.Tests.Settings;

/// <summary>The default lists and numbers (spec, Shapes and settings).</summary>
public class SplashItDefaultsTests(ITestOutputHelper output)
{
    [Fact]
    public void Defaults_AreEnabled_100Percent_3Px_100Percent()
    {
        var d = SplashItSettings.Default;
        Assert.True(d.Enabled);
        Assert.Equal(1, d.SchemaVersion);
        Assert.Equal(100, d.ProjectFillOpacity);
        Assert.Equal(50, d.ProjectUnselectedOpacity);
        Assert.Equal(50, SplashItDefaults.ProjectUnselectedOpacity);
        Assert.Equal(3, d.FamilyBarWidth);
        Assert.Equal(100, d.FamilyBarOpacity);
        Assert.Empty(d.Validate());
    }

    [Fact]
    public void ProjectColors_AreGanttItsPalette_InOrder()
    {
        // gantt-it src/GanttIt.Core/Model/Palette.cs Palette.Default, gantt-it commit 1e27832.
        Assert.Equal(
            ["#4F8EF7", "#F28E2B", "#59A14F", "#E15759", "#B07AA1", "#76B7B2",
             "#EDC948", "#FF9DA7", "#9C755F", "#BAB0AC", "#8CD17D", "#499894"],
            SplashItDefaults.ProjectColors.Select(c => c.ToHex()));
    }

    [Fact]
    public void FamilyColors_AreTwelve_Unique_AndNoneIsAProjectColor()
    {
        var family = SplashItDefaults.FamilyColors;
        Assert.Equal(12, family.Count);
        Assert.Equal(12, family.Distinct().Count());
        Assert.Empty(family.Intersect(SplashItDefaults.ProjectColors));
    }

    [Fact]
    public void FamilyColors_ReachThreeToOne_AgainstBothThemeSurfaces()
    {
        // WCAG 1.4.11 non-text contrast, so a thin bar reads on light and dark tabs.
        Assert.Equal("#FFFFFF", SplashItDefaults.LightSurface.ToHex());
        Assert.Equal("#2A2F37", SplashItDefaults.DarkSurface.ToHex());

        var failures = new List<string>();
        foreach (var color in SplashItDefaults.FamilyColors)
        {
            var light = ColorMath.ContrastRatio(color, SplashItDefaults.LightSurface);
            var dark = ColorMath.ContrastRatio(color, SplashItDefaults.DarkSurface);
            output.WriteLine($"{color.ToHex()}  on #FFFFFF {light:0.00}:1  on #2A2F37 {dark:0.00}:1");
            if (light < 3.0 || dark < 3.0) failures.Add($"{color.ToHex()}: {light:0.00} / {dark:0.00}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void DefaultLists_AreReadOnly()
    {
        Assert.False(SplashItDefaults.ProjectColors is Rgb[]);
        Assert.False(SplashItDefaults.FamilyColors is Rgb[]);
        Assert.Throws<NotSupportedException>(() => ((IList<Rgb>)SplashItDefaults.FamilyColors)[0] = default);
    }
}
