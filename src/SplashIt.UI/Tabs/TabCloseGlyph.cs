using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SplashIt.UI.Tabs;

/// <summary>
/// Recolors a tab's close button (spec, Shapes and settings: the close glyph follows the text
/// color, black or white, and never stays Revit's grey on a colored fill; review M1).
/// <para>
/// In Revit 2026 and 2027 the × is <c>Button#mDocumentCloseButton</c> inside AvalonDock's
/// <c>LayoutDocumentTabItem</c> header template, with its own fixed grey (<c>#A2A6B0</c> light,
/// <c>#808080</c> dark), so the tab's <c>Foreground</c> never reaches it. A derived style sets
/// this attached property on the tab, per state (unselected, hover, selected), to the same
/// black or white as the text. While it is set, the button found by name under the tab gets
/// that brush as a local <c>Foreground</c>; when it clears (the tab's original style is back),
/// the button's own local value is put back exactly (or cleared, when it had none). A style
/// resource override was not used: a resource has one value per style, but the glyph needs
/// the text color of each state.
/// </para>
/// <para>
/// The button is found by a bounded walk of the tab's visual tree (<see cref="MaxVisited"/>
/// nodes) for a <see cref="Control"/> named one of <see cref="ButtonNames"/>; when the header
/// template is not built yet, the walk is retried on the tab's next layout pass and on
/// <c>Loaded</c>. Every entry point catches its own exceptions: nothing reaches the
/// dispatcher. UI thread only.
/// </para>
/// </summary>
public static class TabCloseGlyph
{
    /// <summary>Names of the close button in the tab's tree (AvalonDock / Revit).</summary>
    public static readonly IReadOnlyList<string> ButtonNames = ["mDocumentCloseButton", "PART_CloseButton"];

    /// <summary>The most nodes the walk under one tab visits.</summary>
    public const int MaxVisited = 200;

    /// <summary>Layout passes to keep looking for a button that is not there yet, before giving up until the next <c>Loaded</c>.</summary>
    public const int MaxLayoutRetries = 20;

    [ThreadStatic] private static Stack<DependencyObject>? _stack;

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground",
        typeof(Brush),
        typeof(TabCloseGlyph),
        new PropertyMetadata(null, OnForegroundChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(State),
        typeof(TabCloseGlyph),
        new PropertyMetadata(null));

    public static Brush? GetForeground(DependencyObject element) => (Brush?)element.GetValue(ForegroundProperty);

    public static void SetForeground(DependencyObject element, Brush? value) => element.SetValue(ForegroundProperty, value);

    /// <summary>The close button currently recolored under <paramref name="tab"/>, if any.</summary>
    public static Control? FindRecolored(FrameworkElement tab) => (tab.GetValue(StateProperty) as State)?.Button;

    /// <summary>The close button under <paramref name="root"/>, by name; null when there is none.</summary>
    public static Control? FindButton(DependencyObject root)
    {
        var stack = _stack ??= new Stack<DependencyObject>();
        stack.Clear();
        stack.Push(root);
        var visited = 0;
        while (stack.Count > 0 && visited++ < MaxVisited)
        {
            var node = stack.Pop();
            if (node is Control control && !ReferenceEquals(node, root) && IsCloseButtonName(control.Name))
            {
                stack.Clear();
                return control;
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = count - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
        stack.Clear();
        return null;
    }

    private static bool IsCloseButtonName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var candidate in ButtonNames)
            if (string.Equals(name, candidate, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement tab) Refresh(tab);
    }

    private static void Refresh(FrameworkElement tab)
    {
        try
        {
            var brush = GetForeground(tab);
            var state = tab.GetValue(StateProperty) as State;
            if (brush is null)
            {
                if (state is null) return;
                state.Restore();
                state.Unhook();
                tab.ClearValue(StateProperty);
                return;
            }

            if (state is null)
            {
                state = new State(tab);
                tab.SetValue(StateProperty, state);
            }
            state.Hook();

            var button = FindButton(tab);
            if (button is null)
            {
                state.WaitForLayout();
                return;
            }
            state.StopWaiting();
            if (!ReferenceEquals(state.Button, button))
            {
                state.Restore(); // a rebuilt template: the old button is gone
                state.Capture(button);
            }
            if (!Equals(button.ReadLocalValue(Control.ForegroundProperty), brush))
                button.SetValue(Control.ForegroundProperty, brush);
        }
        catch (Exception)
        {
            // Never into the dispatcher: the glyph keeps whatever color it has.
        }
    }

    private sealed class State(FrameworkElement tab)
    {
        private bool _hooked;
        private bool _waiting;
        private int _retries;
        private object? _originalLocal;

        public Control? Button { get; private set; }

        public void Capture(Control button)
        {
            Button = button;
            var local = button.ReadLocalValue(Control.ForegroundProperty);
            _originalLocal = local is System.Windows.Data.BindingExpressionBase expression ? expression.ParentBindingBase : local;
        }

        /// <summary>Puts the button's own local Foreground back (or clears ours).</summary>
        public void Restore()
        {
            if (Button is not { } button) return;
            Button = null;
            try
            {
                switch (_originalLocal)
                {
                    case System.Windows.Data.BindingBase binding:
                        System.Windows.Data.BindingOperations.SetBinding(button, Control.ForegroundProperty, binding);
                        break;
                    case Brush original:
                        button.SetValue(Control.ForegroundProperty, original);
                        break;
                    default:
                        button.ClearValue(Control.ForegroundProperty);
                        break;
                }
            }
            catch (Exception)
            {
                // The button left with its template; nothing to restore.
            }
            _originalLocal = null;
        }

        public void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            tab.Loaded += OnLoaded;
        }

        public void Unhook()
        {
            StopWaiting();
            if (!_hooked) return;
            _hooked = false;
            tab.Loaded -= OnLoaded;
        }

        public void WaitForLayout()
        {
            if (_waiting || _retries >= MaxLayoutRetries) return;
            _waiting = true;
            tab.LayoutUpdated += OnLayoutUpdated;
        }

        public void StopWaiting()
        {
            if (!_waiting) return;
            _waiting = false;
            tab.LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _retries = 0;
            Refresh(tab);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            try
            {
                if (FindButton(tab) is null)
                {
                    if (++_retries >= MaxLayoutRetries) StopWaiting(); // a template without one: stop looking
                    return;
                }
                StopWaiting();
                Refresh(tab);
            }
            catch (Exception)
            {
                StopWaiting();
            }
        }
    }
}
