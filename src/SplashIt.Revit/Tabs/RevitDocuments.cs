using System.Globalization;
using Autodesk.Revit.DB;
using SplashIt.Core.Files;
using SplashIt.Core.Logging;
using SplashIt.UI.Tabs;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// The opaque key the colorizer holds for one open Revit document. Reference equality:
/// one key per document for as long as it is open. Its text is a serial number, so a key
/// can be logged without naming the document.
/// </summary>
public sealed class RevitDocumentKey
{
    internal RevitDocumentKey(int serial) => Serial = serial;

    public int Serial { get; }

    public override string ToString() => "doc#" + Serial.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The open, non-linked Revit documents, each with its <see cref="RevitDocumentKey"/>, its
/// title and its <see cref="FileKind"/> (the colorizer's <see cref="IDocumentInfo"/>).
/// <para>
/// <b>API context.</b> The Revit API may only be called from Revit's own callbacks (events,
/// commands). The colorizer's hot path runs from WPF layout passes, outside them. So every
/// Revit call happens here, in the document events and at start, and the title and kind
/// are cached: <see cref="Classify"/>, <see cref="Count"/> and <see cref="FindByTitle"/> only
/// read the cache. <see cref="Find"/> compares <see cref="Document"/> objects (reference
/// first, then Revit's <c>Document.Equals</c>), which reads no model data.
/// </para>
/// UI thread only.
/// </summary>
public sealed class RevitDocuments : IDocumentInfo
{
    private readonly Func<RevitApplication?> _application;
    private readonly ILog _log;
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<int, RevitDocumentKey> _closing = new();
    private int _serial;

    public RevitDocuments(Func<RevitApplication?> application, ILog? log = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The number of open documents known (cached; no Revit call).</summary>
    public int Count => _entries.Count;

    /// <summary>API context. The key for <paramref name="document"/>, registering it (title and kind) when new.</summary>
    public RevitDocumentKey Register(Document document)
    {
        if (Find(document) is { } existing)
        {
            Update(document);
            return existing.Key;
        }
        var entry = new Entry(document, new RevitDocumentKey(++_serial));
        entry.Read();
        _entries.Add(entry);
        return entry.Key;
    }

    /// <summary>API context. Reads the title and kind again (after a Save As).</summary>
    public RevitDocumentKey Update(Document document)
    {
        if (Find(document) is not { } entry) return Register(document);
        entry.Read();
        return entry.Key;
    }

    /// <summary>API context. Remembers which key <paramref name="documentId"/> (from DocumentClosing) is.</summary>
    public void BeginClosing(int documentId, Document document)
    {
        if (Find(document) is { } entry) _closing[documentId] = entry.Key;
    }

    /// <summary>
    /// The key DocumentClosing remembered for <paramref name="documentId"/>; when the close
    /// succeeded the document is forgotten too. Null when unknown.
    /// </summary>
    public RevitDocumentKey? EndClosing(int documentId, bool closed)
    {
        if (!_closing.Remove(documentId, out var key)) return null;
        if (closed) _entries.RemoveAll(e => ReferenceEquals(e.Key, key));
        return key;
    }

    /// <summary>
    /// API context (it lists Revit's documents). Re-reads the open, non-linked documents,
    /// adding new ones and forgetting closed ones, then lists their keys.
    /// </summary>
    public void GetOpenDocuments(List<object> into)
    {
        into.Clear();
        try
        {
            if (_application() is { } app)
            {
                var open = new List<Entry>();
                foreach (Document document in app.Documents)
                {
                    if (document.IsLinked) continue;
                    var key = Register(document);
                    open.Add(_entries.First(e => ReferenceEquals(e.Key, key)));
                }
                _entries.RemoveAll(e => !open.Contains(e));
            }
        }
        catch (Exception ex)
        {
            _log.Warn("documents: listing Revit's open documents failed (" + ex.GetType().Name + "); using the documents seen in events.");
        }
        foreach (var entry in _entries) into.Add(entry.Key);
    }

    /// <summary>The cached kind of <paramref name="documentKey"/>; Other when unknown. No Revit call.</summary>
    public FileKind Classify(object documentKey)
    {
        foreach (var entry in _entries)
            if (ReferenceEquals(entry.Key, documentKey)) return entry.Kind;
        return FileKind.Other;
    }

    /// <summary>The key of the open document that is <paramref name="document"/>; null when unknown.</summary>
    public RevitDocumentKey? KeyOf(Document document) => Find(document)?.Key;

    /// <summary>
    /// The title-prefix fallback (spec, RevitTabSurface): the document whose cached title,
    /// followed by <c>" - "</c>, begins <paramref name="tabTitle"/>, or that equals it. The
    /// longest title wins, so "A - B" beats "A" for a tab titled "A - B - Level 1". Null
    /// when none matches. No Revit call.
    /// </summary>
    public RevitDocumentKey? FindByTitle(string tabTitle)
    {
        Entry? best = null;
        var bestLength = 0;
        foreach (var entry in _entries)
        {
            foreach (var title in entry.Titles)
            {
                if (title.Length <= bestLength || !TitleMatches(tabTitle, title)) continue;
                best = entry;
                bestLength = title.Length;
            }
        }
        return best?.Key;
    }

    private static bool TitleMatches(string tabTitle, string title) =>
        string.Equals(tabTitle, title, StringComparison.Ordinal)
        || (tabTitle.Length > title.Length + 3
            && tabTitle.StartsWith(title, StringComparison.Ordinal)
            && string.CompareOrdinal(tabTitle, title.Length, " - ", 0, 3) == 0);

#if DEBUG
    /// <summary>Debug diagnostics only: the cached titles.</summary>
    internal IEnumerable<string> DebugTitles() => _entries.SelectMany(e => e.Titles);
#endif

    private Entry? Find(Document document)
    {
        foreach (var entry in _entries)
            if (ReferenceEquals(entry.Document, document)) return entry;
        foreach (var entry in _entries)
        {
            try
            {
                if (entry.Document.Equals(document)) return entry;
            }
            catch (Exception)
            {
                // A document object gone stale: it cannot be this one.
            }
        }
        return null;
    }

    private sealed class Entry(Document document, RevitDocumentKey key)
    {
        public Document Document { get; } = document;

        public RevitDocumentKey Key { get; } = key;

        /// <summary>
        /// The title, and the title without its Revit extension when it has one (Revit's
        /// <c>Document.Title</c> carries the extension when Windows shows file extensions;
        /// AC11 confirms which form the tabs show).
        /// </summary>
        public string[] Titles { get; private set; } = [];

        public FileKind Kind { get; private set; } = FileKind.Other;

        /// <summary>API context: caches the title and classifies the document (spec, Behavior: file kinds).</summary>
        public void Read()
        {
            var title = Document.Title ?? "";
            var extension = System.IO.Path.GetExtension(title).ToLowerInvariant();
            Titles = extension is ".rvt" or ".rfa" or ".rte" or ".rft"
                ? [title, title[..^extension.Length]]
                : [title];
            Kind = FileKindClassifier.Classify(Document.IsFamilyDocument, Document.PathName, Document.IsModelInCloud);
        }
    }
}
