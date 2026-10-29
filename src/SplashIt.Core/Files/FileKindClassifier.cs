namespace SplashIt.Core.Files;

/// <summary>How a document's tabs are treated.</summary>
public enum FileKind
{
    /// <summary>Takes no color; its tabs stay native.</summary>
    Other = 0,

    /// <summary>A project: a solid fill from Project colors.</summary>
    Project,

    /// <summary>A family: a left bar from Family colors.</summary>
    Family,
}

/// <summary>
/// Classifies a document from what Revit reports about it (spec, Behavior: File kinds).
/// <list type="table">
/// <item><term>Project</term><description>not a family document, and saved as <c>.rvt</c>, a cloud model, or not yet saved</description></item>
/// <item><term>Family</term><description>a family document, and saved as <c>.rfa</c> or not yet saved</description></item>
/// <item><term>Other</term><description>everything else: <c>.rte</c>, <c>.rft</c>, <c>.adsk</c> and any other extension</description></item>
/// </list>
/// Extensions compare case-insensitively. A null, empty or whitespace path means
/// not yet saved. The path is only inspected, never stored or logged.
/// </summary>
public static class FileKindClassifier
{
    public static FileKind Classify(bool isFamilyDocument, string? path, bool isCloud)
    {
        var unsaved = string.IsNullOrWhiteSpace(path);
        var extension = unsaved ? "" : ExtensionOf(path!);

        if (isFamilyDocument)
            return unsaved || Is(extension, ".rfa") ? FileKind.Family : FileKind.Other;

        return unsaved || isCloud || Is(extension, ".rvt") ? FileKind.Project : FileKind.Other;
    }

    /// <summary>
    /// The extension of the last path segment, including the dot, or "" when it has
    /// none. Both <c>\</c> and <c>/</c> separate segments on every OS, so Windows
    /// paths and cloud paths read the same here as on Revit's host.
    /// </summary>
    internal static string ExtensionOf(string path)
    {
        var trimmed = path.TrimEnd();
        var segmentStart = trimmed.LastIndexOfAny(['\\', '/']) + 1;
        var dot = trimmed.LastIndexOf('.');
        return dot >= segmentStart ? trimmed[dot..] : "";
    }

    private static bool Is(string extension, string expected) =>
        string.Equals(extension, expected, StringComparison.OrdinalIgnoreCase);
}
