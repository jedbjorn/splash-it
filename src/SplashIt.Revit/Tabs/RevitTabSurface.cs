using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using SplashIt.Core.Logging;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// The ONLY class that touches Revit's internal UI tree (spec, RevitTabSurface). It reads
/// internal types by reflection and by name only: there is no compile-time reference to
/// any of Revit's private assemblies (AvalonDock, AdWindows, UIFramework).
/// <list type="number">
/// <item><b>Roots</b>: the WPF content of Revit's main window
/// (<c>UIApplication.MainWindowHandle</c> → <see cref="HwndSource"/> → root visual), plus
/// every other <see cref="HwndSource"/> whose top-level window is the main window (WPF
/// content hosted in child windows). Floating view windows are top-level windows of their
/// own and are left out (spec, Out of Scope).</item>
/// <item><b>Tabs</b>: <see cref="TreeTabSurface"/> walks those roots for the document pane
/// by type name (<c>LayoutDocumentPaneControl</c>) and reads its <see cref="TabItem"/>s.</item>
/// <item><b>Tab → document</b>: first by an internal handle: an object reachable by
/// reflection from the tab's data context or content that is a Revit
/// <see cref="Document"/>; the member path found is cached per type. Else by title prefix,
/// <c>Document.Title + " - "</c> (<see cref="RevitDocuments.FindByTitle"/>). Else the tab is
/// left alone.</item>
/// </list>
/// It never throws: an unexpected tree gives <see cref="TabSurfaceResult.Unavailable"/>.
/// It logs counts, type names and member names, never a title or a path. UI thread only.
/// </summary>
public sealed class RevitTabSurface : ITabSurface
{
    /// <summary>How deep the handle probe looks from a tab's data context or content.</summary>
    public const int ProbeDepth = 3;

    /// <summary>The most members the handle probe reads for one root type.</summary>
    public const int ProbeBudget = 600;

    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const uint GA_ROOT = 2;

    private readonly Func<IntPtr> _mainWindow;
    private readonly RevitDocuments _documents;
    private readonly ILog _log;
    private readonly TreeTabSurface _tree;
    private readonly Dictionary<Type, MemberInfo[]?> _handlePaths = new();

    private int _byHandle;
    private int _byTitle;
    private int _unresolved;
    private string _lastLog = "";

    public RevitTabSurface(Func<IntPtr> mainWindow, RevitDocuments documents, ILog? log = null)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _log = log ?? NullLog.Instance;
        _tree = new TreeTabSurface(GetRoots, () => _documents.Count > 0, ResolveKey, _log);
    }

    public TabSurfaceResult Read(List<SurfaceTab> into)
    {
        var result = _tree.Read(into);
        if (_byHandle + _byTitle + _unresolved > 0)
        {
            LogOnce(Inv($"tab surface: resolved {_byHandle} tabs by internal handle, {_byTitle} by title prefix, {_unresolved} unresolved."));
            _byHandle = _byTitle = _unresolved = 0;
        }
#if DEBUG
        TabDiagnostics.Dump(into, _documents.DebugTitles());
#endif
        return result;
    }

    public void Invalidate() => _tree.Invalidate();

    private void GetRoots(List<Visual> roots)
    {
        try
        {
            var main = _mainWindow();
            if (main == IntPtr.Zero) return;
            int mainRoots = 0, childRoots = 0;
            if (HwndSource.FromHwnd(main) is { RootVisual: { } mainRoot })
            {
                roots.Add(mainRoot);
                mainRoots++;
            }

            foreach (var source in PresentationSource.CurrentSources)
            {
                if (source is not HwndSource { IsDisposed: false } hwndSource || hwndSource.RootVisual is not { } root) continue;
                var handle = hwndSource.Handle;
                if (handle == main || GetAncestor(handle, GA_ROOT) != main) continue;
                if (roots.Contains(root)) continue;
                roots.Add(root);
                childRoots++;
            }
            LogOnce(Inv($"tab surface: main window {(mainRoots > 0 ? "is" : "is not")} a WPF source; {childRoots} child WPF sources under it."));
        }
        catch (Exception ex)
        {
            LogOnce("tab surface: listing the main window's WPF sources failed (" + ex.GetType().Name + ").");
        }
    }

    private object? ResolveKey(TabItem tab, string title)
    {
        try
        {
            if (ByHandle(tab) is { } document && _documents.KeyOf(document) is { } handleKey)
            {
                _byHandle++;
                return handleKey;
            }
        }
        catch (Exception ex)
        {
            LogOnce("tab surface: the handle probe failed (" + ex.GetType().Name + "); using titles.");
        }

        if (title.Length > 0 && _documents.FindByTitle(title) is { } titleKey)
        {
            _byTitle++;
            return titleKey;
        }
        _unresolved++;
        return null;
    }

    /// <summary>The <see cref="Document"/> reachable from the tab by the cached (or newly found) member path.</summary>
    private Document? ByHandle(TabItem tab)
    {
        foreach (var start in new[] { tab.DataContext, tab.Content, tab.Header })
        {
            if (start is null or string) continue;
            var type = start.GetType();
            if (!_handlePaths.TryGetValue(type, out var path))
            {
                path = FindPath(start);
                _handlePaths[type] = path;
                LogOnce(path is null
                    ? Inv($"tab surface: no document handle reachable from {type.FullName} within {ProbeDepth} members.")
                    : Inv($"tab surface: document handle found: {type.Name}.{string.Join(".", path.Select(m => m.Name))}."));
            }
            if (path is not null && Follow(start, path) is Document document) return document;
        }
        return null;
    }

    /// <summary>Breadth-first over instance fields and properties, looking for a <see cref="Document"/>.</summary>
    private static MemberInfo[]? FindPath(object start)
    {
        var queue = new Queue<(object Value, MemberInfo[] Path)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { start };
        queue.Enqueue((start, []));
        var budget = ProbeBudget;
        while (queue.Count > 0)
        {
            var (value, path) = queue.Dequeue();
            if (path.Length >= ProbeDepth) continue;
            foreach (var member in MembersOf(value.GetType()))
            {
                if (--budget < 0) return null;
                var next = Read(value, member);
                if (next is null || next is string || next.GetType().IsPrimitive || next is IEnumerable and not Document) continue;
                MemberInfo[] nextPath = [.. path, member];
                if (next is Document) return nextPath;
                if (IsFrameworkType(next.GetType()) || !seen.Add(next)) continue;
                queue.Enqueue((next, nextPath));
            }
        }
        return null;
    }

    private static object? Follow(object start, MemberInfo[] path)
    {
        var value = start;
        foreach (var member in path)
        {
            if (value is null) return null;
            value = Read(value, member);
        }
        return value;
    }

    private static IEnumerable<MemberInfo> MembersOf(Type type)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (IsFrameworkType(t)) yield break; // WPF and BCL base classes hold no Revit handle
            foreach (var field in t.GetFields(InstanceMembers | BindingFlags.DeclaredOnly))
                if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(string)) yield return field;
            foreach (var property in t.GetProperties(InstanceMembers | BindingFlags.DeclaredOnly))
                if (property.GetIndexParameters().Length == 0 && property.GetGetMethod(nonPublic: true) is not null
                    && !property.PropertyType.IsPrimitive && !property.PropertyType.IsEnum && property.PropertyType != typeof(string))
                    yield return property;
        }
    }

    private static object? Read(object target, MemberInfo member)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsFrameworkType(Type type)
    {
        var ns = type.Namespace ?? "";
        return ns.StartsWith("System", StringComparison.Ordinal) || ns.StartsWith("MS.", StringComparison.Ordinal)
               || ns.StartsWith("Microsoft", StringComparison.Ordinal);
    }

    private void LogOnce(string message)
    {
        if (message == _lastLog) return;
        _lastLog = message;
        _log.Info(message);
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
