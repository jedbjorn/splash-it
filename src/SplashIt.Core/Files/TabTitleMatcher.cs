namespace SplashIt.Core.Files;

/// <summary>The result of matching a tab title to the open documents.</summary>
/// <param name="Index">The matching document's index, or -1 when none matches or the match is a tie.</param>
/// <param name="Length">The length of the matching document title (0 when none).</param>
/// <param name="Tied">True when two or more documents tie for the longest match (the tab is left unresolved).</param>
public readonly record struct TitleMatch(int Index, int Length, bool Tied)
{
    public static TitleMatch None => new(-1, 0, false);

    public bool IsMatch => Index >= 0;
}

/// <summary>
/// Resolves a Revit view tab to its document by title (spec, RevitTabSurface: the title
/// prefix <c>Document.Title + " - "</c>; review m3). Pure and ordinal.
/// <list type="bullet">
/// <item>A document title matches a tab title when they are equal, or when the tab title
/// starts with the document title followed by <c>" - "</c>. So "Tower" never matches
/// "Tower B - Level 1".</item>
/// <item>The longest matching document title wins, so "A - B" beats "A" for "A - B - L1".</item>
/// <item>When two or more different documents tie for the longest match (two open
/// documents with the same title), there is no match: a wrong color is worse than none.</item>
/// <item>A document may carry several titles (with and without its extension, and its title
/// before a Save As); any of them can match. Empty titles never match.</item>
/// </list>
/// </summary>
public static class TabTitleMatcher
{
    public const string Separator = " - ";

    /// <summary>Whether <paramref name="documentTitle"/> is the document part of <paramref name="tabTitle"/>.</summary>
    public static bool Matches(string tabTitle, string documentTitle)
    {
        if (string.IsNullOrEmpty(tabTitle) || string.IsNullOrEmpty(documentTitle)) return false;
        if (string.Equals(tabTitle, documentTitle, StringComparison.Ordinal)) return true;
        return tabTitle.Length > documentTitle.Length + Separator.Length
               && tabTitle.StartsWith(documentTitle, StringComparison.Ordinal)
               && string.CompareOrdinal(tabTitle, documentTitle.Length, Separator, 0, Separator.Length) == 0;
    }

    /// <summary>
    /// The document whose title is the longest match for <paramref name="tabTitle"/>;
    /// <paramref name="documentTitles"/>[i] lists document i's current titles, and
    /// <paramref name="keptTitles"/>[i] (optional) the titles it had before a Save As.
    /// At the same length a current title outranks a kept one (re-review r1: Save As "Tower"
    /// as "Tower v2", then reopen Tower.rvt: the reopened file's current "Tower" wins over the
    /// first file's kept "Tower"). Two documents tying on current titles, or on kept titles
    /// with no current one at that length, is still a tie.
    /// </summary>
    public static TitleMatch Find(string tabTitle, IReadOnlyList<IReadOnlyList<string>> documentTitles,
        IReadOnlyList<IReadOnlyList<string>>? keptTitles = null)
    {
        ArgumentNullException.ThrowIfNull(documentTitles);
        var best = -1;
        var bestLength = 0;
        var bestCurrent = false;
        var tied = false;
        for (var i = 0; i < documentTitles.Count; i++)
        {
            var current = Longest(tabTitle, documentTitles[i]);
            var kept = keptTitles is not null && i < keptTitles.Count ? Longest(tabTitle, keptTitles[i]) : 0;
            var isCurrent = current >= kept;
            var longest = Math.Max(current, kept);
            if (longest == 0) continue;
            if (longest > bestLength || (longest == bestLength && isCurrent && !bestCurrent))
            {
                best = i;
                bestLength = longest;
                bestCurrent = isCurrent;
                tied = false;
            }
            else if (longest == bestLength && isCurrent == bestCurrent)
            {
                tied = true;
            }
        }
        if (best < 0) return TitleMatch.None;
        return tied ? new TitleMatch(-1, bestLength, true) : new TitleMatch(best, bestLength, false);
    }

    private static int Longest(string tabTitle, IReadOnlyList<string> titles)
    {
        var longest = 0;
        foreach (var title in titles)
            if (title.Length > longest && Matches(tabTitle, title)) longest = title.Length;
        return longest;
    }

    /// <summary>
    /// A document's titles as Revit may show them in its tabs: <paramref name="title"/>
    /// (<c>Document.Title</c>), the file name of <paramref name="path"/> (<c>Document.PathName</c>),
    /// and each without its Revit extension (<c>.rvt</c>, <c>.rfa</c>, <c>.rte</c>, <c>.rft</c>).
    /// Seen on the VM in Revit 2026: after a Save As, <c>Document.Title</c> is "Tower26" while the
    /// tabs read "Tower26.rvt - Floor Plan: L1"; the extension shows or not with the Windows
    /// setting.
    /// <para>
    /// When <paramref name="isFamilyDocument"/> is given, the title with its kind's extension is
    /// a form too: <c>.rfa</c> for a family, <c>.rvt</c> for a project, unless the title already
    /// ends in it. This is the rc.1 root cause of SC-004: a family opened with Edit Family has
    /// an empty path and the title "Support - Steel Bar", while its tabs read
    /// "Support - Steel Bar.rfa - 3D View: View 1", so no other form matched. A family opened
    /// from disk matched only through its path's file name.
    /// </para>
    /// Distinct, in that order; empty when there is no title and no path.
    /// </summary>
    public static IReadOnlyList<string> TitleForms(string? title, string? path = null, bool? isFamilyDocument = null)
    {
        var forms = new List<string>(4);
        void Add(string? form)
        {
            if (string.IsNullOrEmpty(form) || forms.Contains(form, StringComparer.Ordinal)) return;
            forms.Add(form);
        }

        Add(title);
        Add(WithoutRevitExtension(title));
        var fileName = FileNameOf(path);
        Add(fileName);
        Add(WithoutRevitExtension(fileName));
        if (isFamilyDocument is { } family && !string.IsNullOrEmpty(title))
        {
            var extension = family ? ".rfa" : ".rvt";
            if (!title.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) Add(title + extension);
        }
        return forms;
    }

    private static string? WithoutRevitExtension(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return null;
        var extension = name[dot..];
        foreach (var known in new[] { ".rvt", ".rfa", ".rte", ".rft" })
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
                return name[..dot];
        return null;
    }

    /// <summary>The last segment of a Windows or cloud path (both separators), or null.</summary>
    private static string? FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimEnd();
        var start = trimmed.LastIndexOfAny(['\\', '/']) + 1;
        return start < trimmed.Length ? trimmed[start..] : null;
    }
}
