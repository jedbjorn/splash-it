using System.Windows;

namespace SplashIt.UI.Tabs;

/// <summary>
/// Calls back after every WPF layout pass on the current thread's dispatcher.
/// <para>
/// <see cref="UIElement.LayoutUpdated"/> is raised for every subscribed element whenever the
/// dispatcher's layout manager finishes a pass, whichever elements were laid out. So a
/// private element, never placed in any tree, hears the docking host's layout passes
/// without holding a reference into Revit's UI, and its subscription is ours alone to
/// remove. This is the colorizer's hot-path trigger.
/// </para>
/// Create, start and stop on the UI thread.
/// </summary>
public sealed class LayoutPulse
{
    private readonly FrameworkElement _probe = new();
    private Action? _callback;

    public bool IsRunning => _callback is not null;

    /// <summary>Starts calling <paramref name="callback"/> after each layout pass (replacing any earlier callback).</summary>
    public void Start(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Stop();
        _callback = callback;
        _probe.LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>Removes the subscription. Safe to call when not running.</summary>
    public void Stop()
    {
        if (_callback is null) return;
        _probe.LayoutUpdated -= OnLayoutUpdated;
        _callback = null;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => _callback?.Invoke();
}
