using System.Text.Json;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Tests.Support;

namespace SplashIt.Core.Tests.Settings;

/// <summary>
/// AC3 (exchange): export and import round-trip; reject a wrong format, a newer
/// schema and invalid fields with all errors listed; never carry <c>enabled</c>.
/// </summary>
public sealed class SettingsExchangeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly SplashItSettings Mine = new()
    {
        Enabled = true,
        ProjectColors = [Rgb.Parse("#102030"), Rgb.Parse("#405060")],
        FamilyColors = [Rgb.Parse("#A0B0C0"), Rgb.Parse("#D0E0F0"), Rgb.Parse("#123456")],
        ProjectFillOpacity = 45,
        ProjectUnselectedOpacity = 35,
        FamilyBarWidth = 5,
        FamilyBarOpacity = 80,
    };

    /// <summary>A valid export body with one property replaced or removed (null).</summary>
    private static string ExportWith(string key, string? rawValue)
    {
        var props = new Dictionary<string, string>
        {
            ["format"] = "\"splash-it-settings\"",
            ["schemaVersion"] = "1",
            ["projectColors"] = "[\"#102030\", \"#405060\"]",
            ["familyColors"] = "[\"#A0B0C0\"]",
            ["projectFillOpacity"] = "45",
            ["projectUnselectedOpacity"] = "35",
            ["familyBarWidth"] = "5",
            ["familyBarOpacity"] = "80",
        };
        if (rawValue is null) props.Remove(key);
        else props[key] = rawValue;
        return "{" + string.Join(",", props.Select(p => $"\"{p.Key}\": {p.Value}")) + "}";
    }

    // -------------------------------------------------------------- export

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_NeverCarriesEnabled(bool enabled)
    {
        var json = SettingsExchange.Export(Mine with { Enabled = enabled });
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("enabled", out _));
        Assert.DoesNotContain("enabled", json);
    }

    [Fact]
    public void Export_StartsWithFormatAndSchemaVersion_ThenTheSettings()
    {
        using var doc = JsonDocument.Parse(SettingsExchange.Export(Mine));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            ["format", "schemaVersion", "projectColors", "familyColors", "projectFillOpacity", "projectUnselectedOpacity",
             "familyBarWidth", "familyBarOpacity"],
            names);
        Assert.Equal(35, doc.RootElement.GetProperty("projectUnselectedOpacity").GetInt32());
        Assert.Equal("splash-it-settings", doc.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(["#A0B0C0", "#D0E0F0", "#123456"],
            doc.RootElement.GetProperty("familyColors").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Export_InvalidSettings_IsRefused() =>
        Assert.Throws<InvalidOperationException>(() => SettingsExchange.Export(Mine with { ProjectColors = [] }));

    // --------------------------------------------------------- round-trip

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RoundTrip_CarriesEverythingButEnabled(bool exporterEnabled, bool importerEnabled)
    {
        var json = SettingsExchange.Export(Mine with { Enabled = exporterEnabled });
        var current = SplashItSettings.Default with { Enabled = importerEnabled };

        var result = SettingsExchange.Import(json, current);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.Equal(Mine with { Enabled = importerEnabled }, result.Settings);
    }

    [Fact]
    public void RoundTrip_ThroughAFile()
    {
        var path = _temp.File("team-colors.json");
        SettingsExchange.ExportFile(Mine, path);

        var result = SettingsExchange.ImportFile(path, SplashItSettings.Default);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(Mine, result.Settings);
        Assert.Equal(["team-colors.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void RoundTrip_CarriesTheUnselectedOpacity(int share)
    {
        var json = SettingsExchange.Export(Mine with { ProjectUnselectedOpacity = share });
        var result = SettingsExchange.Import(json, SplashItSettings.Default);
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(share, result.Settings!.ProjectUnselectedOpacity);
        Assert.Equal(share, SettingsExchange.Import(ExportWith("projectUnselectedOpacity", $"{share}"), SplashItSettings.Default)
            .Settings!.ProjectUnselectedOpacity);
    }

    [Fact]
    public void Import_IgnoresAnEnabledInTheFile()
    {
        var json = ExportWith("enabled", "false");
        var result = SettingsExchange.Import(json, SplashItSettings.Default with { Enabled = true });
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.True(result.Settings!.Enabled);
    }

    [Fact]
    public void Import_IgnoresUnknownProperties() =>
        Assert.True(SettingsExchange.Import(ExportWith("note", "\"from Ana\""), SplashItSettings.Default).Succeeded);

    [Fact]
    public void Import_AcceptsOneAndTwelveColors()
    {
        var twelve = "[" + string.Join(",", Enumerable.Range(0, 12).Select(i => $"\"#0000{i:X2}\"")) + "]";
        Assert.True(SettingsExchange.Import(ExportWith("projectColors", twelve), SplashItSettings.Default).Succeeded);
        Assert.True(SettingsExchange.Import(ExportWith("projectColors", "[\"#abcdef\"]"), SplashItSettings.Default).Succeeded);
    }

    // ------------------------------------------------------- wrong format

    [Theory]
    [InlineData("\"gantt-it-settings\"")]
    [InlineData("\"Splash-It-Settings\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Import_WrongFormat_IsRejected(string format)
    {
        var result = SettingsExchange.Import(ExportWith("format", format), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Equal(["This is not a Splash-It settings export: \"format\" must be \"splash-it-settings\"."], result.Errors);
    }

    [Fact]
    public void Import_MissingFormat_IsRejected()
    {
        var result = SettingsExchange.Import(ExportWith("format", null), SplashItSettings.Default);
        Assert.Equal(["This is not a Splash-It settings export: \"format\" must be \"splash-it-settings\"."], result.Errors);
    }

    [Fact]
    public void Import_AnOwnSettingsFile_IsRejected()
    {
        // settings.json itself is not an export: it has no format (and carries enabled).
        var result = SettingsExchange.Import(Mine.ToJson(), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not a Splash-It settings export"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Import_Empty_IsRejected(string? json) =>
        Assert.Equal(["The file is empty."], SettingsExchange.Import(json, SplashItSettings.Default).Errors);

    [Theory]
    [InlineData("{ \"format\": ")]
    [InlineData("not json at all")]
    public void Import_NotJson_IsRejected(string json)
    {
        var result = SettingsExchange.Import(json, SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
        Assert.StartsWith("The file is not valid JSON", result.Errors[0]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"splash-it-settings\"")]
    public void Import_NotAnObject_IsRejected(string json) =>
        Assert.Equal(["The file does not hold a settings object."], SettingsExchange.Import(json, SplashItSettings.Default).Errors);

    // ---------------------------------------------------------- schema

    [Theory]
    [InlineData("2", 2)]
    [InlineData("17", 17)]
    public void Import_NewerSchema_IsRejectedWithAClearMessage(string raw, int version)
    {
        var result = SettingsExchange.Import(ExportWith("schemaVersion", raw), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Equal(
            [$"This file was exported by a newer version of Splash-It (schema version {version}); this version reads schema version 1. Update Splash-It to import it."],
            result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void Import_MissingOrBadSchema_IsRejected(string? raw) =>
        Assert.Equal(["The file has no whole-number \"schemaVersion\"."],
            SettingsExchange.Import(ExportWith("schemaVersion", raw), SplashItSettings.Default).Errors);

    [Fact]
    public void Import_SchemaZero_IsRejected() =>
        Assert.Equal(["Schema version 0 is not supported."],
            SettingsExchange.Import(ExportWith("schemaVersion", "0"), SplashItSettings.Default).Errors);

    // ------------------------------------------------------ invalid fields

    [Theory]
    [InlineData("projectColors", "[]", "Project colors must have from 1 to 12 colors; there are 0.")]
    [InlineData("familyColors", "[]", "Family colors must have from 1 to 12 colors; there are 0.")]
    [InlineData("projectColors", "\"#102030\"", "Project colors must be a list of #RRGGBB colors.")]
    [InlineData("projectColors", "[\"#102030\", \"red\"]", "Project colors, color 2: \"red\" is not a #RRGGBB color.")]
    [InlineData("familyColors", "[\"#12345\"]", "Family colors, color 1: \"#12345\" is not a #RRGGBB color.")]
    [InlineData("familyColors", "[\"123456\"]", "Family colors, color 1: \"123456\" is not a #RRGGBB color.")]
    [InlineData("familyColors", "[\"#1234567\"]", "Family colors, color 1: \"#1234567\" is not a #RRGGBB color.")]
    [InlineData("familyColors", "[42]", "Family colors, color 1: 42 is not a #RRGGBB color.")]
    [InlineData("projectFillOpacity", "9", "Project fill opacity must be from 10 to 100; it is 9.")]
    [InlineData("projectFillOpacity", "101", "Project fill opacity must be from 10 to 100; it is 101.")]
    [InlineData("projectFillOpacity", "50.5", "Project fill opacity must be a whole number.")]
    [InlineData("projectFillOpacity", "\"50\"", "Project fill opacity must be a whole number.")]
    [InlineData("projectUnselectedOpacity", "9", "Unselected tab opacity must be from 10 to 100; it is 9.")]
    [InlineData("projectUnselectedOpacity", "101", "Unselected tab opacity must be from 10 to 100; it is 101.")]
    [InlineData("projectUnselectedOpacity", "50.5", "Unselected tab opacity must be a whole number.")]
    [InlineData("projectUnselectedOpacity", null, "The file has no \"projectUnselectedOpacity\".")]
    [InlineData("familyBarWidth", "0", "Family bar width must be from 1 to 6; it is 0.")]
    [InlineData("familyBarWidth", "7", "Family bar width must be from 1 to 6; it is 7.")]
    [InlineData("familyBarWidth", "1e12", "Family bar width must be from 1 to 6; it is 1e12.")]
    [InlineData("familyBarOpacity", "-5", "Family bar opacity must be from 10 to 100; it is -5.")]
    [InlineData("familyBarOpacity", null, "The file has no \"familyBarOpacity\".")]
    [InlineData("projectColors", null, "The file has no \"projectColors\".")]
    public void Import_InvalidField_IsRejected_NothingClamped(string key, string? raw, string expected)
    {
        var result = SettingsExchange.Import(ExportWith(key, raw), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Equal([expected], result.Errors);
    }

    [Fact]
    public void Import_EscapedLoneSurrogateColor_IsABadColor_NotThrown()
    {
        // "\ud800" is legal JSON, but JsonElement.GetString() throws on it.
        var result = SettingsExchange.Import(ExportWith("familyColors", """["#00FF00", "\ud800"]"""), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Equal(["Family colors, color 2: \"\\ud800\" is not a #RRGGBB color."], result.Errors);
    }

    [Fact]
    public void Import_EscapedLoneSurrogateFormat_IsTheWrongFormat_NotThrown()
    {
        var result = SettingsExchange.Import(ExportWith("format", "\"\\ud800\""), SplashItSettings.Default);
        Assert.Equal(["This is not a Splash-It settings export: \"format\" must be \"splash-it-settings\"."], result.Errors);
    }

    [Fact]
    public void ImportFile_SettingsWithAnEscapedLoneSurrogate_ListsErrors_NotThrown()
    {
        var path = _temp.File("broken.json");
        File.WriteAllText(path, """{ "format": "\udc00", "schemaVersion": 1, "projectColors": ["\ud800"] }""");

        var result = SettingsExchange.ImportFile(path, SplashItSettings.Default);

        Assert.False(result.Succeeded);
        Assert.Contains("This is not a Splash-It settings export: \"format\" must be \"splash-it-settings\".", result.Errors);
        Assert.Contains("Project colors, color 1: \"\\ud800\" is not a #RRGGBB color.", result.Errors);
        Assert.Contains("The file has no \"familyColors\".", result.Errors);
    }

    [Fact]
    public void Import_ThirteenColors_IsRejected()
    {
        var thirteen = "[" + string.Join(",", Enumerable.Range(0, 13).Select(i => $"\"#0000{i:X2}\"")) + "]";
        var result = SettingsExchange.Import(ExportWith("familyColors", thirteen), SplashItSettings.Default);
        Assert.Equal(["Family colors must have from 1 to 12 colors; there are 13."], result.Errors);
    }

    [Fact]
    public void Import_ListsEveryError_NotJustTheFirst()
    {
        var thirteen = string.Join(",", Enumerable.Range(0, 12).Select(i => $"\"#0000{i:X2}\"")) + ",\"oops\"";
        var json = $$"""
            {
              "format": "pyrevit-tabs",
              "schemaVersion": 3,
              "enabled": false,
              "projectColors": [{{thirteen}}],
              "familyColors": ["#GGGGGG", "#00FF00", ""],
              "projectFillOpacity": 5,
              "familyBarWidth": 2.5
            }
            """;

        var result = SettingsExchange.Import(json, SplashItSettings.Default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Equal(
        [
            "This is not a Splash-It settings export: \"format\" must be \"splash-it-settings\".",
            "This file was exported by a newer version of Splash-It (schema version 3); this version reads schema version 1. Update Splash-It to import it.",
            "Project colors, color 13: \"oops\" is not a #RRGGBB color.",
            "Project colors must have from 1 to 12 colors; there are 13.",
            "Family colors, color 1: \"#GGGGGG\" is not a #RRGGBB color.",
            "Family colors, color 3: \"\" is not a #RRGGBB color.",
            "Project fill opacity must be from 10 to 100; it is 5.",
            "The file has no \"projectUnselectedOpacity\".",
            "Family bar width must be a whole number.",
            "The file has no \"familyBarOpacity\".",
        ], result.Errors);
    }

    [Fact]
    public void Import_LongBadValue_IsShortenedInTheMessage()
    {
        var longValue = "\"" + new string('x', 200) + "\"";
        var result = SettingsExchange.Import(ExportWith("familyColors", $"[{longValue}]"), SplashItSettings.Default);
        Assert.Single(result.Errors);
        Assert.True(result.Errors[0].Length < 120, result.Errors[0]);
        Assert.Contains("...", result.Errors[0]);
    }

    // ------------------------------------------------------------- files

    [Fact]
    public void ImportFile_Missing_IsReported()
    {
        var result = SettingsExchange.ImportFile(_temp.File("nope.json"), SplashItSettings.Default);
        Assert.False(result.Succeeded);
        Assert.Equal(["The file does not exist."], result.Errors);
    }

    [Fact]
    public void ImportFile_TooLarge_IsRefusedUnread()
    {
        var path = _temp.File("big.json");
        File.WriteAllText(path, new string(' ', SettingsExchange.MaxImportBytes + 1));
        var result = SettingsExchange.ImportFile(path, SplashItSettings.Default);
        Assert.Equal(["The file is larger than 64 KB, too large for a settings export."], result.Errors);
    }
}
