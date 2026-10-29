using SplashIt.Core.Files;

namespace SplashIt.Core.Tests.Files;

/// <summary>AC3 (kinds): classification as in the spec's Behavior table.</summary>
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
    // Projects: not yet saved.
    [InlineData(false, null, false, FileKind.Project)]
    [InlineData(false, "", false, FileKind.Project)]
    [InlineData(false, "   ", false, FileKind.Project)]
    // Families: a family document, saved as .rfa (any case) or not yet saved.
    [InlineData(true, @"C:\Families\Door.rfa", false, FileKind.Family)]
    [InlineData(true, @"C:\Families\DOOR.RFA", false, FileKind.Family)]
    [InlineData(true, @"C:\Families\Door.Rfa", false, FileKind.Family)]
    [InlineData(true, null, false, FileKind.Family)]
    [InlineData(true, "", false, FileKind.Family)]
    // Other: project templates, family templates, anything else.
    [InlineData(false, @"C:\Templates\Architectural.rte", false, FileKind.Other)]
    [InlineData(false, @"C:\Templates\ARCHITECTURAL.RTE", false, FileKind.Other)]
    [InlineData(true, @"C:\Templates\Generic Model.rft", false, FileKind.Other)]
    [InlineData(true, @"C:\Templates\GENERIC MODEL.RFT", false, FileKind.Other)]
    [InlineData(false, @"C:\Templates\Generic Model.rft", false, FileKind.Other)]
    [InlineData(false, @"C:\Exchange\Tower.adsk", false, FileKind.Other)]
    [InlineData(false, @"C:\Exports\Tower.ifc", false, FileKind.Other)]
    [InlineData(false, @"C:\Exports\Tower", false, FileKind.Other)]
    // The extension is read from the file name, not a folder name.
    [InlineData(false, @"C:\Old.rvt\Tower", false, FileKind.Other)]
    [InlineData(false, @"C:\Old.rte\Tower.rvt", false, FileKind.Project)]
    // A kind and an extension that disagree take no color.
    [InlineData(true, @"C:\Models\Tower.rvt", false, FileKind.Other)]
    [InlineData(false, @"C:\Families\Door.rfa", false, FileKind.Other)]
    [InlineData(true, "Autodesk Docs://Project X/Door.rfa", true, FileKind.Family)]
    [InlineData(true, "Autodesk Docs://Project X/Tower.rvt", true, FileKind.Other)]
    public void Classify_FollowsTheBehaviorTable(bool isFamily, string? path, bool isCloud, FileKind expected) =>
        Assert.Equal(expected, FileKindClassifier.Classify(isFamily, path, isCloud));

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
