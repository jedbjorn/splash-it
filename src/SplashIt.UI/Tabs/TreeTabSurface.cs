using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SplashIt.Core.Logging;

namespace SplashIt.UI.Tabs;

/// <summary>
/// An <see cref="ITabSurface"/> that finds document tabs by walking WPF visual trees and
/// recognizing the document pane by its type name only (no reference to the assembly that
/// defines it). It is the Revit-independent half of <c>RevitTabSurface</c>, which supplies
/// the roots (Revit's main window), whether documents are open, and how a tab resolves to
/// a document; tests drive it with synthetic trees.
/// <list type="bullet">
/// <item><b>Panes</b>: an element is a document pane when it is an <see cref="ItemsControl"/>
/// and its type, or a base type, is named one of <see cref="PaneTypeNames"/> (AvalonDock's
/// <c>LayoutDocumentPaneControl</c> by default). Its item containers that are
/// <see cref="TabItem"/>s are the document tabs, in item order.</item>
/// <item><b>Caching</b>: panes are found once and reused. They are looked for again after
/// <see cref="Invalidate"/> (a document opened, closed or renamed), when a pane found in a live
/// window leaves it, and at most every <see cref="RediscoverAfter"/> (a split or a new pane);
/// that periodic walk covers only the docking host (<c>DockingManager</c>) when one was found.
/// With no document open there is nothing to find, and nothing is walked.</item>
/// <item><b>Titles</b>: see <see cref="TitleOf"/> (the tool tip first, as Revit puts the
/// full "document - view" title there).</item>
/// <item><b>Keys</b>: resolved once per tab and title, then reused until the title changes
/// or <see cref="Invalidate"/>.</item>
/// <item><b>Unavailable</b>: documents are open but no pane has been found for
/// <see cref="UnavailableAfter"/> (a document's tabs can lag its open event, so a short gap
/// reads as "no tabs yet"); a tree larger than <see cref="MaxVisited"/> elements; or any
/// exception. It never throws.</item>
/// </list>
/// Logs counts and type names only, never titles. UI thread only.
/// </summary>
public sealed class TreeTabSurface : ITabSurface
{
    /// <summary>AvalonDock's document pane, the control Revit's document tabs live in.</summary>
    public static readonly IReadOnlyList<string> DefaultPaneTypeNames = ["LayoutDocumentPaneControl"];

    /// <summary>The docking host the periodic rediscovery walks under, when found.</summary>
    public static readonly IReadOnlyList<string> DefaultHostTypeNames = ["DockingManager"];

    /// <summary>The most elements one discovery walk visits before giving up.</summary>
    public const int MaxVisited = 200_000;

    public const string NoRootsReason = "The main window has no WPF content to search.";
    public const string NoPaneReason = "No document tab pane (LayoutDocumentPaneControl) was found under the main window.";
    public const string TooLargeReason = "The UI tree under the main window was too large to search.";

    private readonly Action<List<Visual>> _getRoots;
    private readonly Func<bool> _documentsOpen;
    private readonly Func<TabItem, string, object?> _resolveKey;
    private readonly ILog _log;
    private readonly Func<long> _clockMs;
    private readonly HashSet<string> _paneNames;
    private readonly HashSet<string> _hostNames;

    private readonly List<Visual> _roots = new();
    private readonly List<ItemsControl> _panes = new();
    private readonly Stack<DependencyObject> _stack = new();
    private readonly Dictionary<Type, bool> _isPane = new();
    private readonly Dictionary<Type, bool> _isHost = new();
    private readonly Dictionary<Type, PropertyInfo?> _titleProperty = new();
    private readonly Dictionary<TabItem, Resolved> _keys = new();

    private Visual? _host;
    private bool _invalid = true;
    private bool _panesLive;
    private long _lastWalk;
    private long? _missingSince;
    private string? _lastFailure;
    private string _lastLog = "";

    /// <param name="getRoots">Clears and fills the list with the visual roots to search.</param>
    /// <param name="documentsOpen">Whether any document is open (so tabs are expected). Called on every read: keep it cheap.</param>
    /// <param name="resolveKey">The document key for a tab and its title; null when unresolved.</param>
    /// <param name="log">Where to log what was found (counts and type names).</param>
    /// <param name="paneTypeNames">Pane type names; <see cref="DefaultPaneTypeNames"/> when null.</param>
    /// <param name="clockMs">Milliseconds; <see cref="Environment.TickCount64"/> when null.</param>
    public TreeTabSurface(
        Action<List<Visual>> getRoots,
        Func<bool> documentsOpen,
        Func<TabItem, string, object?> resolveKey,
        ILog? log = null,
        IEnumerable<string>? paneTypeNames = null,
        Func<long>? clockMs = null)
    {
        _getRoots = getRoots ?? throw new ArgumentNullException(nameof(getRoots));
        _documentsOpen = documentsOpen ?? throw new ArgumentNullException(nameof(documentsOpen));
        _resolveKey = resolveKey ?? throw new ArgumentNullException(nameof(resolveKey));
        _log = log ?? NullLog.Instance;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
        _paneNames = new HashSet<string>(paneTypeNames ?? DefaultPaneTypeNames, StringComparer.Ordinal);
        _hostNames = new HashSet<string>(DefaultHostTypeNames, StringComparer.Ordinal);
    }

    /// <summary>How often a read looks for new or lost panes even when nothing was invalidated.</summary>
    public TimeSpan RediscoverAfter { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>While documents are open, how long no pane may be found before the surface reports Unavailable.</summary>
    public TimeSpan UnavailableAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>While no pane is found, how often a read walks the tree again.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromMilliseconds(500);

    public IReadOnlyCollection<string> PaneTypeNames => _paneNames;

    /// <summary>The number of document panes found by the last walk.</summary>
    public int PaneCount => _panes.Count;

    /// <summary>The number of discovery walks so far (for tests and diagnostics).</summary>
    public int WalkCount { get; private set; }

    public void Invalidate()
    {
        _invalid = true;
        _keys.Clear();
    }

    public TabSurfaceResult Read(List<SurfaceTab> into)
    {
        try
        {
            into.Clear();
            if (!_documentsOpen())
            {
                _missingSince = null;
                return TabSurfaceResult.Available; // no documents, no tabs to find
            }

            if (!EnsurePanes(out var failure))
            {
                if (failure is not null) return TabSurfaceResult.Unavailable(failure);
                var now = _clockMs();
                _missingSince ??= now;
                return now - _missingSince.Value >= (long)UnavailableAfter.TotalMilliseconds
                    ? TabSurfaceResult.Unavailable(NoPaneReason)
                    : TabSurfaceResult.Available; // not yet: the tabs may still be on their way
            }
            _missingSince = null;

            foreach (var pane in _panes)
            {
                var items = pane.Items;
                var generator = pane.ItemContainerGenerator;
                for (var i = 0; i < items.Count; i++)
                {
                    if (generator.ContainerFromIndex(i) is not TabItem tab) continue;
                    var title = TitleOf(tab);
                    into.Add(new SurfaceTab(tab, title, tab.IsSelected, KeyOf(tab, title)));
                }
            }
            return TabSurfaceResult.Available;
        }
        catch (Exception ex)
        {
            _invalid = true;
            into.Clear();
            LogOnce("tab surface: reading the tabs failed (" + ex.GetType().Name + ").");
            return TabSurfaceResult.Unavailable("Reading the tabs failed (" + ex.GetType().Name + ").");
        }
    }

    /// <summary>
    /// The tab's full title; "" when none is found. The tool tip comes first when it is a
    /// string: in Revit 2026 (seen on the VM) the tab's <c>LayoutDocument.Title</c> is only
    /// the view name, and the tool tip is the full <c>"&lt;document&gt; - &lt;view&gt;"</c>.
    /// Then the header when it is a string, then a public string <c>Title</c> property of the
    /// header, data context or content.
    /// </summary>
    public string TitleOf(TabItem tab)
    {
        if (tab.ToolTip is string { Length: > 0 } tip) return tip;
        if (tab.Header is string header) return header;
        return TitleProperty(tab.Header) ?? TitleProperty(tab.DataContext) ?? TitleProperty(tab.Content) ?? "";
    }

    private object? KeyOf(TabItem tab, string title)
    {
        if (_keys.TryGetValue(tab, out var resolved) && string.Equals(resolved.Title, title, StringComparison.Ordinal))
            return resolved.Key;
        var key = _resolveKey(tab, title);
        _keys[tab] = new Resolved(title, key);
        return key;
    }

    /// <summary>
    /// True when there are panes to read. False with <paramref name="failure"/> null when
    /// none was found (yet); with it set when the walk could not finish.
    /// </summary>
    private bool EnsurePanes(out string? failure)
    {
        failure = null;
        var now = _clockMs();
        var sinceWalk = now - _lastWalk;
        if (!_invalid)
        {
            if (_panes.Count > 0 && sinceWalk < (long)RediscoverAfter.TotalMilliseconds && !AnyPaneGone()) return true;
            if (_panes.Count == 0 && sinceWalk < (long)RetryAfter.TotalMilliseconds)
            {
                failure = _lastFailure;
                return false;
            }
        }

        // A periodic rediscovery walks only the docking host; a full one (after Invalidate,
        // with no host yet, or when the host left its window) walks every root.
        var full = _invalid || _host is null || (_panesLive && PresentationSource.FromVisual(_host) is null);
        _roots.Clear();
        if (full)
        {
            _host = null;
            _getRoots(_roots);
        }
        else
        {
            _roots.Add(_host!);
        }

        WalkCount++;
        _panes.Clear();
        _invalid = false;
        _lastWalk = now;
        _lastFailure = null;
        var visited = 0;
        foreach (var root in _roots)
        {
            if (Walk(root, ref visited)) continue;
            _panes.Clear();
            _lastFailure = failure = TooLargeReason;
            LogOnce(Inv($"tab surface: gave up after {visited} elements under {_roots.Count} roots."));
            return false;
        }

        if (_roots.Count == 0)
        {
            _lastFailure = failure = NoRootsReason;
            LogOnce("tab surface: no WPF roots under the main window.");
            return false;
        }

        _panesLive = false;
        foreach (var pane in _panes)
            if (PresentationSource.FromVisual(pane) is not null) _panesLive = true;

        LogOnce(Inv($"tab surface: {_panes.Count} document panes ({PaneTypes()}) under {_roots.Count} roots, {visited} elements visited, host {(_host is null ? "none" : _host.GetType().Name)}."));
        return _panes.Count > 0;
    }

    /// <summary>In a live window, a pane without a presentation source has been removed.</summary>
    private bool AnyPaneGone()
    {
        if (!_panesLive) return false; // off-screen trees (tests) have no presentation source at all
        foreach (var pane in _panes)
            if (PresentationSource.FromVisual(pane) is null) return true;
        return false;
    }

    /// <summary>Depth-first walk collecting panes; false when it passed <see cref="MaxVisited"/>.</summary>
    private bool Walk(Visual root, ref int visited)
    {
        _stack.Clear();
        _stack.Push(root);
        while (_stack.Count > 0)
        {
            var node = _stack.Pop();
            if (++visited > MaxVisited)
            {
                _stack.Clear();
                return false;
            }

            if (node is ItemsControl items && Matches(node.GetType(), _paneNames, _isPane))
            {
                _panes.Add(items);
                FindHost(items);
                continue; // a pane's content is the documents' views, not more panes
            }

            if (node is not Visual and not System.Windows.Media.Media3D.Visual3D) continue;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = count - 1; i >= 0; i--)
                _stack.Push(VisualTreeHelper.GetChild(node, i));
        }
        return true;
    }

    private void FindHost(DependencyObject pane)
    {
        if (_host is not null) return;
        for (var node = VisualTreeHelper.GetParent(pane); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Visual visual && Matches(node.GetType(), _hostNames, _isHost))
            {
                _host = visual;
                return;
            }
        }
    }

    private static bool Matches(Type type, HashSet<string> names, Dictionary<Type, bool> cache)
    {
        if (cache.TryGetValue(type, out var known)) return known;
        var match = false;
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (!names.Contains(t.Name)) continue;
            match = true;
            break;
        }
        cache[type] = match;
        return match;
    }

    private string? TitleProperty(object? source)
    {
        if (source is null or string) return source as string;
        var type = source.GetType();
        if (!_titleProperty.TryGetValue(type, out var property))
        {
            property = type.GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
            if (property is not null && (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0))
                property = null;
            _titleProperty[type] = property;
        }
        return property?.GetValue(source) as string;
    }

    private string PaneTypes()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pane in _panes) names.Add(pane.GetType().Name);
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private void LogOnce(string message)
    {
        if (message == _lastLog) return;
        _lastLog = message;
        _log.Info(message);
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private readonly record struct Resolved(string Title, object? Key);
}
