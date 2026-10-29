using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SplashIt.Core.Files;
using SplashIt.Core.Logging;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// The ONLY class that touches Revit's internal UI tree (spec, RevitTabSurface). It reads
/// internal types by name only: there is no compile-time reference to any of Revit's private
/// assemblies (AvalonDock, AdWindows, UIFramework), and it runs no code of theirs beyond WPF's
/// own properties.
/// <list type="number">
/// <item><b>Roots</b>: the WPF content of Revit's main window
/// (<c>UIApplication.MainWindowHandle</c> → <see cref="HwndSource"/> → root visual), plus
/// every other <see cref="HwndSource"/> whose top-level window is the main window (WPF
/// content hosted in child windows). Floating view windows are top-level windows of their
/// own and are left out (spec, Out of Scope).</item>
/// <item><b>Tabs</b>: <see cref="TreeTabSurface"/> walks those roots for the document pane
/// by type name (<c>LayoutDocumentPaneControl</c>) and reads its <see cref="TabItem"/>s.</item>
/// <item><b>Tab → document</b>: by title (<see cref="RevitDocuments.FindByTitle"/>,
/// <see cref="TabTitleMatcher"/>): the tab's tool tip is <c>"&lt;document&gt; - &lt;view&gt;"</c>.
/// A tab that matches no document, or ties between two documents with the same title, is
/// left alone. The spec's "internal per-document handle" does not exist on Revit 2026 and
/// 2027: the VM's diagnostics found no <c>Document</c> reachable from a tab (its content is
/// <c>UIFramework.MFCMDIChildFrameControl</c>, which holds only a window handle), so no
/// reflection probe runs (review m2).</item>
/// </list>
/// It never throws: an unexpected tree gives <see cref="TabSurfaceResult.Unavailable"/>.
/// It logs counts and type names, never a title or a path. UI thread only.
/// </summary>
public sealed class RevitTabSurface : ITabSurface
{
    private const uint GA_ROOT = 2;

    private readonly Func<IntPtr> _mainWindow;
    private readonly RevitDocuments _documents;
    private readonly ILog _log;
    private readonly TreeTabSurface _tree;

    private int _byTitle;
    private int _tied;
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
        if (_byTitle + _tied + _unresolved > 0)
        {
            LogOnce(Inv($"tab surface: resolved {_byTitle} tabs by title, {_tied} left alone on a title tie, {_unresolved} unresolved."));
            _byTitle = _tied = _unresolved = 0;
        }
#if DEBUG
        TabDiagnostics.Dump(into, _documents.DebugTitles);
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
        if (title.Length == 0)
        {
            _unresolved++;
            return null;
        }
        if (_documents.FindByTitle(title, out var tied) is { } key)
        {
            _byTitle++;
            return key;
        }
        if (tied) _tied++;
        else _unresolved++;
        return null;
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
