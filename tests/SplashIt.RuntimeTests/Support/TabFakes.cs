using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SplashIt.Core.Files;
using SplashIt.Core.Logging;
using SplashIt.UI.Tabs;

namespace SplashIt.RuntimeTests.Support;

/// <summary>
/// A fake <see cref="ITabSurface"/> over real WPF <see cref="TabItem"/>s in a
/// <see cref="TabControl"/>. Each tab's document key is set with <see cref="Bind"/>. Its
/// <see cref="Read"/> allocates nothing in steady state, like the real surface's hot path.
/// </summary>
public sealed class FakeSurface : ITabSurface
{
    private readonly Dictionary<TabItem, object> _keys = new();

    public FakeSurface(TabControl strip) => Strip = strip;

    public TabControl Strip { get; }

    public int Reads { get; private set; }

    public int Invalidations { get; private set; }

    /// <summary>When set, <see cref="Read"/> reports Unavailable with this reason.</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>When set, <see cref="Read"/> throws it (a broken surface; the contract says never, the colorizer guards anyway).</summary>
    public Exception? Throw { get; set; }

    public void Bind(TabItem tab, object key) => _keys[tab] = key;

    public void Unbind(TabItem tab) => _keys.Remove(tab);

    public TabSurfaceResult Read(List<SurfaceTab> into)
    {
        Reads++;
        into.Clear();
        if (Throw is { } ex) throw ex;
        if (UnavailableReason is { } reason) return TabSurfaceResult.Unavailable(reason);
        var items = Strip.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var tab = (TabItem)items[i]!;
            _keys.TryGetValue(tab, out var key);
            into.Add(new SurfaceTab(tab, (string)tab.Header, tab.IsSelected, key));
        }
        return TabSurfaceResult.Available;
    }

    public void Invalidate() => Invalidations++;
}

/// <summary>A fake <see cref="IDocumentInfo"/>: documents by key with a settable kind.</summary>
public sealed class FakeDocuments : IDocumentInfo
{
    public Dictionary<object, FileKind> Kinds { get; } = new();

    public List<object> Open { get; } = new();

    public void Add(object key, FileKind kind)
    {
        Kinds[key] = kind;
        if (!Open.Contains(key)) Open.Add(key);
    }

    public void GetOpenDocuments(List<object> into)
    {
        into.Clear();
        into.AddRange(Open);
    }

    public FileKind Classify(object documentKey) => Kinds.TryGetValue(documentKey, out var kind) ? kind : FileKind.Other;
}

/// <summary>A fake event source: counts subscriptions and lets tests raise events.</summary>
public sealed class FakeEvents : IColorizerEventSource
{
    public ThemeKind CurrentTheme { get; set; } = ThemeKind.Light;

    public IColorizerEventSink? Sink { get; private set; }

    public int Subscribes { get; private set; }

    public int Unsubscribes { get; private set; }

    public Exception? ThrowOnSubscribe { get; set; }

    public bool IsSubscribed => Sink is not null;

    public void Subscribe(IColorizerEventSink sink)
    {
        if (ThrowOnSubscribe is { } ex) throw ex;
        Subscribes++;
        Sink = sink;
    }

    public void Unsubscribe()
    {
        Unsubscribes++;
        Sink = null;
    }

    public void Layout() => Sink?.OnLayoutUpdated();
}

public sealed class FakeGuard(bool running) : IForeignColorizerGuard
{
    public bool Running { get; set; } = running;

    public bool IsRunning(out string? detail)
    {
        detail = Running ? "pyRevit's tab colorizer is running (test)." : null;
        return Running;
    }
}

/// <summary>A log that keeps its lines.</summary>
public sealed class ListLog : ILog
{
    public List<string> Lines { get; } = new();

    public void Write(LogLevel level, string message) => Lines.Add(level + " " + message);
}

/// <summary>Counts every change of a tab's effective <c>Style</c>.</summary>
public sealed class StyleChangeCounter : IDisposable
{
    private static readonly DependencyPropertyDescriptor Descriptor =
        DependencyPropertyDescriptor.FromProperty(FrameworkElement.StyleProperty, typeof(TabItem));

    private readonly List<TabItem> _tabs = new();

    public StyleChangeCounter(IEnumerable<TabItem> tabs)
    {
        foreach (var tab in tabs)
        {
            Descriptor.AddValueChanged(tab, OnChanged);
            _tabs.Add(tab);
        }
    }

    public int Count { get; private set; }

    public void Reset() => Count = 0;

    public void Dispose()
    {
        foreach (var tab in _tabs) Descriptor.RemoveValueChanged(tab, OnChanged);
    }

    private void OnChanged(object? sender, EventArgs e) => Count++;
}
