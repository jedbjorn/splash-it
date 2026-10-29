using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SplashIt.Core.Logging;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// One of Revit's document tab panes, AvalonDock's <c>LayoutDocumentPane</c> (the layout model
/// behind a <c>LayoutDocumentPaneControl</c>), as an <see cref="IDocumentPane"/> (spec #2,
/// Architecture). It is the ONLY class that reaches AvalonDock's layout model, and it does so
/// by reflection, by member name: there is no compile-time reference to Xceed or to any of
/// Revit's private assemblies, and Splash-It ships no AvalonDock DLL. It uses exactly:
/// <list type="bullet">
/// <item><c>LayoutElement.Parent</c> of each tab's <c>LayoutDocument</c>, to find the pane
/// (<see cref="Finder"/>);</item>
/// <item><c>LayoutGroup&lt;T&gt;.Children</c> (read as <see cref="IList"/>), for the tabs in
/// order;</item>
/// <item><c>LayoutGroup&lt;T&gt;.MoveChild(int, int)</c>, to move a tab within the pane;</item>
/// <item><c>LayoutDocumentPane.SelectedContentIndex</c> (get and set), for the selected tab.</item>
/// </list>
/// Nothing else is called, so only the order and the selection of the pane's own tabs can
/// change: no tab is added, removed or moved to another pane, and nothing touches Revit's
/// model. Exceptions thrown inside AvalonDock reach the caller unwrapped (their own type, not
/// <see cref="TargetInvocationException"/>); <see cref="TabGrouper"/> catches them. UI thread only.
/// <para>No Revit API is used here, so the runtime tests compile this file against fake
/// AvalonDock-shaped types.</para>
/// </summary>
public sealed class AvalonDockPane : IDocumentPane
{
    /// <summary>The model type a document tab's <c>Parent</c> must be (or derive from).</summary>
    public const string PaneTypeName = "LayoutDocumentPane";

    private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private readonly object _pane;
    private readonly IList _children;
    private readonly MethodInfo _moveChild;
    private readonly PropertyInfo _selectedIndex;

    private AvalonDockPane(object pane, IList children, MethodInfo moveChild, PropertyInfo selectedIndex)
    {
        _pane = pane;
        _children = children;
        _moveChild = moveChild;
        _selectedIndex = selectedIndex;
    }

    /// <summary>The <c>LayoutDocumentPane</c> object itself.</summary>
    public object Model => _pane;

    public int Count => _children.Count;

    public object ItemAt(int index) => _children[index] ?? throw new InvalidOperationException("The pane holds a null tab.");

    public void Move(int from, int to) => Invoke(() => _moveChild.Invoke(_pane, [from, to]));

    public object? SelectedItem
    {
        get
        {
            var index = (int)Invoke(() => _selectedIndex.GetValue(_pane))!;
            return index >= 0 && index < _children.Count ? _children[index] : null;
        }
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            var index = IndexOf(value);
            if (index < 0) throw new InvalidOperationException("The tab to select is not in this pane.");
            Invoke(() =>
            {
                _selectedIndex.SetValue(_pane, index);
                return null;
            });
        }
    }

    private int IndexOf(object item)
    {
        for (var i = 0; i < _children.Count; i++)
            if (ReferenceEquals(_children[i], item)) return i;
        return -1;
    }

    /// <summary>Runs a reflective call and rethrows what AvalonDock threw, not the reflection wrapper.</summary>
    private static object? Invoke(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // not reached
        }
    }

    /// <summary>
    /// Wraps <paramref name="pane"/> when it has the members this class uses; otherwise null
    /// with <paramref name="reason"/> naming what is missing.
    /// </summary>
    public static AvalonDockPane? TryCreate(object pane, out string? reason, out bool hasMoveChild)
    {
        ArgumentNullException.ThrowIfNull(pane);
        var type = pane.GetType();
        hasMoveChild = false;

        var moveChild = FindMethod(type, "MoveChild", typeof(int), typeof(int));
        hasMoveChild = moveChild is not null;
        if (moveChild is null)
        {
            reason = type.Name + " has no public MoveChild(int, int).";
            return null;
        }

        var childrenProperty = FindProperty(type, "Children");
        if (childrenProperty is not { CanRead: true } || childrenProperty.GetIndexParameters().Length > 0)
        {
            reason = type.Name + " has no public Children property.";
            return null;
        }

        var selected = FindProperty(type, "SelectedContentIndex");
        if (selected is null || selected.PropertyType != typeof(int) || selected.GetGetMethod() is null || selected.GetSetMethod() is null)
        {
            reason = type.Name + " has no public read-write int SelectedContentIndex.";
            return null;
        }

        if (Invoke(() => childrenProperty.GetValue(pane)) is not IList children)
        {
            reason = type.Name + ".Children is not a list.";
            return null;
        }

        reason = null;
        return new AvalonDockPane(pane, children, moveChild, selected);
    }

    /// <summary>The most derived public instance property named <paramref name="name"/> (a <c>new</c> member hides its base).</summary>
    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            foreach (var property in t.GetProperties(Members))
                if (property.Name == name && property.GetIndexParameters().Length == 0)
                    return property;
        return null;
    }

    /// <summary>The most derived public instance method with that name and exactly those parameter types.</summary>
    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameters)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            foreach (var method in t.GetMethods(Members))
            {
                if (method.Name != name || method.IsGenericMethodDefinition) continue;
                var actual = method.GetParameters();
                if (actual.Length != parameters.Length) continue;
                var same = true;
                for (var i = 0; i < actual.Length && same; i++)
                    same = actual[i].ParameterType == parameters[i];
                if (same) return method;
            }
        return null;
    }

    private static bool IsNamed(Type type, string name)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            if (t.Name == name) return true;
        return false;
    }

    /// <summary>
    /// Finds the panes of the given tabs' <c>LayoutDocument</c>s (in Revit, each tab's
    /// <c>DataContext</c>) through their <c>Parent</c>. If any tab's parent is not a
    /// <c>LayoutDocumentPane</c>, or a pane lacks a member <see cref="AvalonDockPane"/> uses,
    /// the whole lookup is unavailable, so nothing moves. Logs the AvalonDock assembly version
    /// and whether <c>MoveChild</c> was found, once per finder. Never throws.
    /// </summary>
    public sealed class Finder
    {
        public const string NoTabsReason = "No document tab's layout model was found.";

        private readonly ILog _log;
        private bool _versionLogged;

        public Finder(ILog? log = null) => _log = log ?? NullLog.Instance;

        public DocumentPaneLookup Find(IEnumerable<object?> documents)
        {
            try
            {
                var panes = new List<IDocumentPane>();
                var seen = new List<object>();
                foreach (var document in documents)
                {
                    if (document is null) continue;
                    var parentProperty = FindProperty(document.GetType(), "Parent");
                    if (parentProperty is not { CanRead: true })
                        return Unavailable(document.GetType().Name + " has no public Parent property.");
                    var parent = Invoke(() => parentProperty.GetValue(document));
                    if (parent is null) continue; // a tab that is closing has left its pane
                    if (!IsNamed(parent.GetType(), PaneTypeName))
                        return Unavailable("A document tab's parent is " + parent.GetType().Name + ", not " + PaneTypeName + ".");
                    if (seen.Exists(p => ReferenceEquals(p, parent))) continue;
                    seen.Add(parent);

                    var pane = TryCreate(parent, out var reason, out var hasMoveChild);
                    LogVersion(parent.GetType(), hasMoveChild);
                    if (pane is null) return Unavailable(reason!);
                    panes.Add(pane);
                }
                return panes.Count == 0 ? Unavailable(NoTabsReason) : new DocumentPaneLookup(panes, null);
            }
            catch (Exception ex)
            {
                return Unavailable("Finding the document panes failed (" + ex.GetType().Name + ").");
            }
        }

        private static DocumentPaneLookup Unavailable(string reason) => DocumentPaneLookup.Unavailable(reason);

        /// <summary>AC4 step 1: the AvalonDock assembly and whether MoveChild exists, once.</summary>
        private void LogVersion(Type paneType, bool hasMoveChild)
        {
            if (_versionLogged) return;
            _versionLogged = true;
            try
            {
                var assembly = paneType.Assembly;
                var name = assembly.GetName();
                var file = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "?";
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
                _log.Write(hasMoveChild ? LogLevel.Info : LogLevel.Warn, string.Create(CultureInfo.InvariantCulture,
                    $"group tabs: {paneType.Name} from {name.Name} {name.Version} (file {file}, product {informational}); MoveChild {(hasMoveChild ? "found" : "NOT found")}."));
            }
            catch (Exception ex)
            {
                _log.Warn("group tabs: reading the AvalonDock version failed (" + ex.GetType().Name + ").");
            }
        }
    }
}
