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

/// <summary>Which rule of <see cref="FileKindClassifier"/> decided a document's kind (logged, SC-004).</summary>
public enum FileKindRule
{
    /// <summary>The Revit API decided: <c>Document.IsFamilyDocument</c> (logged as "api").</summary>
    Api = 0,

    /// <summary>A template extension demoted the document to Other (logged as "template-ext").</summary>
    TemplateExtension,
}

/// <summary>
/// Classifies a document from what Revit reports about it (spec, Behavior: File kinds;
/// FnB decision #26, from the rc.1 test: the API decides, the path only demotes templates).
/// <list type="table">
/// <item><term>Project</term><description><c>Document.IsFamilyDocument</c> is false, and the path is not a project template (<c>.rte</c>)</description></item>
/// <item><term>Family</term><description><c>Document.IsFamilyDocument</c> is true, and the path is not a family template (<c>.rft</c>)</description></item>
/// <item><term>Other</term><description>only those two template extensions</description></item>
/// </list>
/// A cloud model, an unsaved document (a null, empty or whitespace path), a temporary
/// path (Edit Family from a project opens a temporary family document) and any other or
/// missing extension all keep the API's kind: a path never makes a document Other unless
/// it names the template extension of the document's own kind. Extensions compare
/// case-insensitively. The path is only inspected, never stored or logged.
/// </summary>
public static class FileKindClassifier
{
    /// <summary>The project template extension: a project saved as one takes no color.</summary>
    public const string ProjectTemplateExtension = ".rte";

    /// <summary>The family template extension: a family saved as one takes no color.</summary>
    public const string FamilyTemplateExtension = ".rft";

    /// <summary>
    /// The document's kind. <paramref name="isCloud"/> does not change it: a cloud model is
    /// classified by the API like any other (#26). It stays a parameter so callers keep passing
    /// what Revit reports.
    /// </summary>
    public static FileKind Classify(bool isFamilyDocument, string? path, bool isCloud) =>
        Classify(isFamilyDocument, path, isCloud, out _);

    /// <summary>The document's kind, and the rule that decided it.</summary>
    public static FileKind Classify(bool isFamilyDocument, string? path, bool isCloud, out FileKindRule rule)
    {
        _ = isCloud;
        var extension = string.IsNullOrWhiteSpace(path) ? "" : ExtensionOf(path);
        var template = isFamilyDocument ? FamilyTemplateExtension : ProjectTemplateExtension;
        if (string.Equals(extension, template, StringComparison.OrdinalIgnoreCase))
        {
            rule = FileKindRule.TemplateExtension;
            return FileKind.Other;
        }
        rule = FileKindRule.Api;
        return isFamilyDocument ? FileKind.Family : FileKind.Project;
    }

    /// <summary>The rule's name as the log writes it: "api" or "template-ext".</summary>
    public static string NameOf(FileKindRule rule) => rule == FileKindRule.TemplateExtension ? "template-ext" : "api";

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
}
