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
    private readonly List<IReadOnlyList<string>> _titles = new();
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

    /// <summary>API context. Reads the title and kind again (after a Save As or a Save).</summary>
    public RevitDocumentKey Update(Document document) => Update(document, out _);

    /// <summary>
    /// API context. Reads the title and kind again; <paramref name="changed"/> tells whether
    /// either changed (a first Save of a new document may raise DocumentSaved, not SavedAs).
    /// </summary>
    public RevitDocumentKey Update(Document document, out bool changed)
    {
        if (Find(document) is not { } entry)
        {
            changed = true;
            return Register(document);
        }
        changed = entry.Read();
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
    /// The document a tab belongs to, by title (<see cref="TabTitleMatcher.Find"/>: equal, or
    /// the document title followed by <c>" - "</c>; ordinal; the longest wins; a tie between
    /// two documents leaves the tab unresolved). Null when none matches; <paramref name="tied"/>
    /// is true for a tie. Only cached titles are read: no Revit call.
    /// </summary>
    public RevitDocumentKey? FindByTitle(string tabTitle, out bool tied)
    {
        _titles.Clear();
        foreach (var entry in _entries) _titles.Add(entry.Titles);
        var match = TabTitleMatcher.Find(tabTitle, _titles);
        _titles.Clear();
        tied = match.Tied;
        return match.IsMatch ? _entries[match.Index].Key : null;
    }

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

        /// <summary>The most titles kept per document (the current forms plus earlier ones).</summary>
        private const int MaxTitles = 6;

        /// <summary>
        /// The title, and the title without its Revit extension when it has one
        /// (<see cref="TabTitleMatcher.TitleForms"/>), followed by the titles it had before a
        /// Save As (review m4): Revit may refresh a tab's tool tip after its event, so a tab
        /// still showing the old title keeps its color.
        /// </summary>
        public IReadOnlyList<string> Titles { get; private set; } = [];

        public FileKind Kind { get; private set; } = FileKind.Other;

        /// <summary>
        /// API context: caches the title and classifies the document (spec, Behavior: file
        /// kinds). True when the title or the kind changed.
        /// </summary>
        public bool Read()
        {
            var current = TabTitleMatcher.TitleForms(Document.Title ?? "", Document.PathName);
            var kind = FileKindClassifier.Classify(Document.IsFamilyDocument, Document.PathName, Document.IsModelInCloud);
            var changed = kind != Kind || Titles.Count == 0 || !string.Equals(Titles[0], current.Count > 0 ? current[0] : "", StringComparison.Ordinal);
            var titles = new List<string>(current);
            foreach (var old in Titles)
                if (titles.Count < MaxTitles && !titles.Contains(old, StringComparer.Ordinal)) titles.Add(old);
            Titles = titles;
            Kind = kind;
            return changed;
        }
    }
}
