using System.Globalization;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Tests.Support;

namespace SplashIt.Core.Tests.Culture;

/// <summary>
/// Parsing, formatting, contrast, settings files, export/import and messages behave
/// the same whatever the user's culture: de-DE (decimal comma) and tr-TR (dotted and
/// dotless i) are the classic traps.
/// </summary>
public sealed class CultureTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> Cultures => new() { "de-DE", "tr-TR" };

    /// <summary>Runs <paramref name="body"/> with both current cultures set, then restores them.</summary>
    private static void InCulture(string name, Action body)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ColorMath_ParsesFormatsAndMeasures_TheSame(string culture)
    {
        var invariantContrast = ColorMath.ContrastRatio(Rgb.Parse("#4F8EF7"), ColorMath.White);

        InCulture(culture, () =>
        {
            Assert.Equal(culture, CultureInfo.CurrentCulture.Name);
            Assert.True(ColorMath.TryParseHex("#4f8ef7", out var c));
            Assert.Equal("#4F8EF7", c.ToHex());
            Assert.Equal("#4F8EF7", ColorMath.ToHex(c));
            Assert.True(Rgb.TryParse(" 4f8ef7 ", out var lenient));
            Assert.Equal(c, lenient);
            Assert.Equal(invariantContrast, ColorMath.ContrastRatio(c, ColorMath.White));
            Assert.Equal(ColorMath.Black, ColorMath.TextColorFor(c));
            Assert.Equal(Rgb.Parse("#A7C7FB"), ColorMath.Blend(c, 0.5, ColorMath.White));
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Settings_ReadClampsAndLogs_Invariantly(string culture)
    {
        InCulture(culture, () =>
        {
            var log = new List<string>();
            var read = SplashItSettings.FromJson(
                """{ "schemaVersion": 1, "enabled": false, "projectFillOpacity": 55.4, "familyBarWidth": 2.5, "familyBarOpacity": 1e3 }""",
                log.Add);

            Assert.False(read.Enabled);
            Assert.Equal(55, read.ProjectFillOpacity);
            Assert.Equal(3, read.FamilyBarWidth);
            Assert.Equal(100, read.FamilyBarOpacity);
            Assert.Contains(log, l => l.Contains("projectFillOpacity 55.4 "));
            Assert.Contains(log, l => l.Contains("familyBarWidth 2.5 "));
            Assert.DoesNotContain(log, l => l.Contains("55,4") || l.Contains("2,5"));
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Settings_WriteAndRead_RoundTrip(string culture)
    {
        var path = _temp.File($"settings-{culture}.json");
        var settings = SplashItSettings.Default with
        {
            Enabled = false,
            ProjectColors = [Rgb.Parse("#ABCDEF"), Rgb.Parse("#0A0B0C")],
            ProjectFillOpacity = 40,
            FamilyBarWidth = 6,
        };

        InCulture(culture, () =>
        {
            settings.Write(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"projectFillOpacity\": 40", text);
            Assert.Contains("\"enabled\": false", text);
            Assert.Equal(settings, SplashItSettings.Read(path));
        });
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Exchange_ExportsImportsAndReports_TheSame(string culture)
    {
        InCulture(culture, () =>
        {
            var exported = SettingsExchange.Export(SplashItSettings.Default);
            Assert.Contains("\"format\": \"splash-it-settings\"", exported);
            var imported = SettingsExchange.Import(exported, SplashItSettings.Default);
            Assert.True(imported.Succeeded, string.Join(" | ", imported.Errors));
            Assert.Equal(SplashItSettings.Default, imported.Settings);

            // Field names with an i (Family bar width, Project fill opacity) and numbers in
            // messages are not touched by the culture.
            var bad = SettingsExchange.Import(
                exported.Replace("\"familyBarWidth\": 3", "\"familyBarWidth\": 2.5")
                        .Replace("\"projectFillOpacity\": 100", "\"projectFillOpacity\": 1000"),
                SplashItSettings.Default);
            Assert.Equal(
            [
                "Project fill opacity must be from 10 to 100; it is 1000.",
                "Family bar width must be a whole number.",
            ], bad.Errors);

            Assert.Equal(
                ["Family bar width must be from 1 to 6; it is 1000."],
                (SplashItSettings.Default with { FamilyBarWidth = 1000 }).Validate());
        });
    }
}
