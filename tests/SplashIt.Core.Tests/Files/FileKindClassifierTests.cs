using SplashIt.Core.Files;

namespace SplashIt.Core.Tests.Files;

/// <summary>
/// AC3 (kinds): classification as in the spec's Behavior table, after FnB decision #26
/// (SC-004): the API decides family vs project; the path only demotes templates.
/// </summary>
public class FileKindClassifierTests
{
    [Theory]
    // Projects: not a family document, saved as .rvt (any case).
    [InlineData(false, @"C:\Models\Tower.rvt", false, FileKind.Project)]
    [InlineData(false, @"C:\Models\TOWER.RVT", false, FileKind.Project)]
    [InlineData(false, @"C:\Models\Tower.Rvt", false, FileKind.Project)]
    [InlineData(false, @"\\server\share\Projects\A.b.c\Tower.v2.rvt", false, FileKind.Project)]
    [InlineData(false, "/home/user/Tower.rvt", false, FileKind.Project)]
    // Projects: a cloud model, whatever its path looks like.
    [InlineData(false, "Autodesk Docs://Project X/Tower.rvt", true, FileKind.Project)]
    [InlineData(false, "BIM 360://Project X/Tower", true, FileKind.Project)]
    [InlineData(false, "", true, FileKind.Project)]
    [InlineData(false, null, true, FileKind.Project)]
    // Projects: not yet saved.
    [InlineData(false, null, false, FileKind.Project)]
    [InlineData(false, "", false, FileKind.Project)]
    [InlineData(false, "   ", false, FileKind.Project)]
    // Projects: an unrecognised, temporary or missing extension keeps the API's kind (#26).
    [InlineData(false, @"C:\Exchange\Tower.adsk", false, FileKind.Project)]
    [InlineData(false, @"C:\Exports\Tower.ifc", false, FileKind.Project)]
    [InlineData(false, @"C:\Exports\Tower", false, FileKind.Project)]
    [InlineData(false, @"C:\Old.rvt\Tower", false, FileKind.Project)]
    [InlineData(false, @"C:\Users\x\AppData\Local\Temp\Tower_detached.tmp", false, FileKind.Project)]
    // Families: a family document, saved as .rfa (any case) or not yet saved.
    [InlineData(true, @"C:\Families\Door.rfa", false, FileKind.Family)]
    [InlineData(true, @"C:\Families\DOOR.RFA", false, FileKind.Family)]
    [InlineData(true, @"C:\Families\Door.Rfa", false, FileKind.Family)]
    [InlineData(true, null, false, FileKind.Family)]
    [InlineData(true, "", false, FileKind.Family)]
    [InlineData(true, "  ", false, FileKind.Family)]
    // Families: Edit Family from a project opens a temporary document (#26, the rc.1 report).
    [InlineData(true, @"C:\Users\x\AppData\Local\Temp\Door.rfa", false, FileKind.Family)]
    [InlineData(true, @"C:\Users\x\AppData\Local\Temp\{3F2A9C1E-7B4D-4E8A-9C21-5D6E7F8A9B0C}.tmp", false, FileKind.Family)]
    [InlineData(true, @"C:\Users\x\AppData\Local\Temp\Revit\Door_1A2B3C", false, FileKind.Family)]
    [InlineData(true, @"C:\Users\x\AppData\Local\Temp\Door.0001.rfa~", false, FileKind.Family)]
    // Families: no extension, or a path that looks like a project: the API wins.
    [InlineData(true, @"C:\Families\Door", false, FileKind.Family)]
    [InlineData(true, @"C:\Models\Tower.rvt", false, FileKind.Family)]
    [InlineData(true, @"C:\Models\TOWER.RVT", false, FileKind.Family)]
    [InlineData(true, "Autodesk Docs://Project X/Door.rfa", true, FileKind.Family)]
    [InlineData(true, "Autodesk Docs://Project X/Tower.rvt", true, FileKind.Family)]
    // A project whose path looks like a family: the API wins.
    [InlineData(false, @"C:\Families\Door.rfa", false, FileKind.Project)]
    [InlineData(false, @"C:\Families\DOOR.RFA", false, FileKind.Project)]
    // Other: only the template extension of the document's own kind (any case).
    [InlineData(false, @"C:\Templates\Architectural.rte", false, FileKind.Other)]
    [InlineData(false, @"C:\Templates\ARCHITECTURAL.RTE", false, FileKind.Other)]
    [InlineData(false, @"C:\Templates\Architectural.Rte", false, FileKind.Other)]
    [InlineData(false, "Autodesk Docs://Project X/Office.rte", true, FileKind.Other)]
    [InlineData(true, @"C:\Templates\Generic Model.rft", false, FileKind.Other)]
    [InlineData(true, @"C:\Templates\GENERIC MODEL.RFT", false, FileKind.Other)]
    [InlineData(true, @"C:\Templates\Generic Model.Rft", false, FileKind.Other)]
    // A template extension of the other kind does not demote (only the document's own template does).
    [InlineData(false, @"C:\Templates\Generic Model.rft", false, FileKind.Project)]
    [InlineData(true, @"C:\Templates\Architectural.rte", false, FileKind.Family)]
    // The extension is read from the file name, not a folder name.
    [InlineData(false, @"C:\Old.rte\Tower.rvt", false, FileKind.Project)]
    [InlineData(false, @"C:\Old.rte\Tower", false, FileKind.Project)]
    [InlineData(true, @"C:\Templates.rft\Door", false, FileKind.Family)]
    public void Classify_FollowsTheBehaviorTable(bool isFamily, string? path, bool isCloud, FileKind expected) =>
        Assert.Equal(expected, FileKindClassifier.Classify(isFamily, path, isCloud));

    [Theory]
    [InlineData(false, @"C:\Models\Tower.rvt", false, FileKindRule.Api)]
    [InlineData(false, null, false, FileKindRule.Api)]
    [InlineData(false, "BIM 360://Project X/Tower", true, FileKindRule.Api)]
    [InlineData(true, @"C:\Users\x\AppData\Local\Temp\Door.tmp", false, FileKindRule.Api)]
    [InlineData(true, @"C:\Models\Tower.rvt", false, FileKindRule.Api)]
    [InlineData(false, @"C:\Templates\Architectural.RTE", false, FileKindRule.TemplateExtension)]
    [InlineData(true, @"C:\Templates\Generic Model.RFT", false, FileKindRule.TemplateExtension)]
    public void Classify_ReportsTheRuleThatDecided(bool isFamily, string? path, bool isCloud, FileKindRule expected)
    {
        FileKindClassifier.Classify(isFamily, path, isCloud, out var rule);
        Assert.Equal(expected, rule);
    }

    [Fact]
    public void TheRuleNames_AreTheLogsWords()
    {
        Assert.Equal("api", FileKindClassifier.NameOf(FileKindRule.Api));
        Assert.Equal("template-ext", FileKindClassifier.NameOf(FileKindRule.TemplateExtension));
    }

    [Theory]
    [InlineData(@"C:\a\b.rvt", ".rvt")]
    [InlineData("C:/a/b.RFA", ".RFA")]
    [InlineData(@"C:\a.b\c", "")]
    [InlineData("noext", "")]
    [InlineData(@"C:\a\b.rvt  ", ".rvt")]
    [InlineData(@"C:\a\.rvt", ".rvt")]
    public void ExtensionOf_ReadsTheLastSegment(string path, string expected) =>
        Assert.Equal(expected, FileKindClassifier.ExtensionOf(path));
}
