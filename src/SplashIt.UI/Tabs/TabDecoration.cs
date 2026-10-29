using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SplashIt.UI.Tabs;

/// <summary>
/// What <see cref="TabDecoration"/> draws over a tab: a bar on the left edge (a family's
/// bar) and/or an accent along the top edge (the selected project tab's accent).
/// Immutable; the brushes are frozen.
/// </summary>
public sealed class TabDecorationSpec
{
    public TabDecorationSpec(Brush? leftBar, double leftBarWidth, Brush? topAccent, double topAccentHeight)
    {
        LeftBar = Frozen(leftBar);
        LeftBarWidth = leftBar is null ? 0 : Math.Max(0, leftBarWidth);
        TopAccent = Frozen(topAccent);
        TopAccentHeight = topAccent is null ? 0 : Math.Max(0, topAccentHeight);
    }

    public Brush? LeftBar { get; }

    public double LeftBarWidth { get; }

    public Brush? TopAccent { get; }

    public double TopAccentHeight { get; }

    private static Brush? Frozen(Brush? brush)
    {
        if (brush is { IsFrozen: false, CanFreeze: true }) brush.Freeze();
        return brush;
    }
}

/// <summary>
/// An attached property that draws a <see cref="TabDecorationSpec"/> over an element in
/// its adorner layer. A derived tab style sets it with a <see cref="Setter"/> (or a trigger
/// setter), so restoring the tab's original style clears it and removes the drawing:
/// one style assignment stays the only thing the colorizer changes on a tab.
/// <para>
/// <b>Why an adorner (spec, Shapes and settings; AC11 pending):</b> Revit's real TabItem
/// template is unknown until AC11. A left bar or a top accent drawn with setters would
/// need template parts (a border thickness the template honors, a named element) that
/// may not exist. An adorner sits above the element in the window's
/// <see cref="AdornerLayer"/> and draws in the element's own bounds whatever its
/// template is: no layout change, no hit testing (clicks reach the tab), and clipped
/// like the tab. The project fill itself cannot be an adorner (it would cover the text),
/// so the fill stays a Background setter; see <see cref="TabStyleFactory"/>.
/// </para>
/// <para>
/// When no adorner layer is reachable yet (the element is not in a tree), the adorner is
/// added on the element's next <c>Loaded</c> or layout pass; when it is unloaded the
/// adorner is removed and comes back on <c>Loaded</c>.
/// </para>
/// </summary>
public static class TabDecoration
{
    public static readonly DependencyProperty DecorationProperty = DependencyProperty.RegisterAttached(
        "Decoration",
        typeof(TabDecorationSpec),
        typeof(TabDecoration),
        new PropertyMetadata(null, OnDecorationChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(State),
        typeof(TabDecoration),
        new PropertyMetadata(null));

    public static TabDecorationSpec? GetDecoration(DependencyObject element) =>
        (TabDecorationSpec?)element.GetValue(DecorationProperty);

    public static void SetDecoration(DependencyObject element, TabDecorationSpec? value) =>
        element.SetValue(DecorationProperty, value);

    /// <summary>The adorner currently drawing <paramref name="element"/>'s decoration, if any.</summary>
    public static TabDecorationAdorner? FindAdorner(UIElement element) =>
        (element.GetValue(StateProperty) as State)?.Adorner;

    private static void OnDecorationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element) Refresh(element);
    }

    /// <summary>Adds, updates or removes the adorner. Never throws (it runs from WPF callbacks, review m6).</summary>
    private static void Refresh(UIElement element)
    {
        try
        {
            RefreshCore(element);
        }
        catch (Exception)
        {
            // Never into Revit's dispatcher: drop the drawing rather than fail.
            if (element.GetValue(StateProperty) is State state) state.Detach();
        }
    }

    private static void RefreshCore(UIElement element)
    {
        var spec = GetDecoration(element);
        var state = element.GetValue(StateProperty) as State;
        if (spec is null)
        {
            if (state is null) return;
            state.Detach();
            state.Unhook();
            element.ClearValue(StateProperty);
            return;
        }

        if (state is null)
        {
            state = new State(element);
            element.SetValue(StateProperty, state);
        }
        state.Hook();

        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
        {
            state.Detach();
            state.WaitForLayout();
            return;
        }
        state.StopWaiting();

        if (state.Adorner is { } existing && ReferenceEquals(state.Layer, layer))
        {
            existing.Spec = spec;
            return;
        }

        state.Detach();
        var adorner = new TabDecorationAdorner(element, spec);
        layer.Add(adorner);
        state.Adorner = adorner;
        state.Layer = layer;
        state.UpdateVisibility();
    }

    private sealed class State(UIElement element)
    {
        private bool _hooked;
        private bool _waiting;

        public TabDecorationAdorner? Adorner { get; set; }

        public AdornerLayer? Layer { get; set; }

        public void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            if (element is FrameworkElement fe)
            {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
            }
            element.IsVisibleChanged += OnIsVisibleChanged;
        }

        public void Unhook()
        {
            StopWaiting();
            if (!_hooked) return;
            _hooked = false;
            if (element is FrameworkElement fe)
            {
                fe.Loaded -= OnLoaded;
                fe.Unloaded -= OnUnloaded;
            }
            element.IsVisibleChanged -= OnIsVisibleChanged;
        }

        public void WaitForLayout()
        {
            if (_waiting) return;
            _waiting = true;
            element.LayoutUpdated += OnLayoutUpdated;
        }

        public void StopWaiting()
        {
            if (!_waiting) return;
            _waiting = false;
            element.LayoutUpdated -= OnLayoutUpdated;
        }

        public void Detach()
        {
            if (Adorner is { } adorner)
            {
                try
                {
                    Layer?.Remove(adorner);
                }
                catch (Exception)
                {
                    // The layer is gone with its window; nothing left to remove from.
                }
            }
            Adorner = null;
            Layer = null;
        }

        /// <summary>
        /// Hides the drawing while the tab is not visible in a live window (an overflowed
        /// tab, a hidden pane). Off-screen renders (no presentation source) always draw.
        /// </summary>
        public void UpdateVisibility()
        {
            if (Adorner is not { } adorner) return;
            var live = PresentationSource.FromVisual(element) is not null;
            adorner.Visibility = !live || element.IsVisible ? Visibility.Visible : Visibility.Hidden;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Refresh(element);

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Detach();
            }
            catch (Exception)
            {
                Adorner = null;
                Layer = null;
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                UpdateVisibility();
            }
            catch (Exception)
            {
                // Visibility is cosmetic; keep going.
            }
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            try
            {
                if (AdornerLayer.GetAdornerLayer(element) is null) return;
                StopWaiting();
                Refresh(element);
            }
            catch (Exception)
            {
                StopWaiting();
            }
        }
    }
}

/// <summary>
/// Draws a <see cref="TabDecorationSpec"/> in the adorned element's bounds: the left bar
/// at full height, the top accent at full width. Never hit-testable.
/// </summary>
public sealed class TabDecorationAdorner : Adorner
{
    private TabDecorationSpec _spec;

    public TabDecorationAdorner(UIElement adornedElement, TabDecorationSpec spec)
        : base(adornedElement)
    {
        _spec = spec;
        IsHitTestVisible = false;
        IsClipEnabled = true;
        SnapsToDevicePixels = true;
    }

    public TabDecorationSpec Spec
    {
        get => _spec;
        set
        {
            if (ReferenceEquals(_spec, value)) return;
            _spec = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            RenderCore(drawingContext);
        }
        catch (Exception)
        {
            // A failed draw leaves the tab undecorated; it never reaches the dispatcher.
        }
    }

    private void RenderCore(DrawingContext drawingContext)
    {
        var element = AdornedElement;
        var size = element.RenderSize;
        if (element.Visibility != Visibility.Visible || size.Width <= 0 || size.Height <= 0) return;

        if (_spec.LeftBar is { } bar && _spec.LeftBarWidth > 0)
            DrawSnapped(drawingContext, bar, new Rect(0, 0, Math.Min(_spec.LeftBarWidth, size.Width), size.Height));

        if (_spec.TopAccent is { } accent && _spec.TopAccentHeight > 0)
            DrawSnapped(drawingContext, accent, new Rect(0, 0, size.Width, Math.Min(_spec.TopAccentHeight, size.Height)));
    }

    /// <summary>Draws on whole device pixels, so a 1 px bar stays one crisp pixel at any position.</summary>
    private static void DrawSnapped(DrawingContext dc, Brush brush, Rect rect)
    {
        dc.PushGuidelineSet(new GuidelineSet([rect.Left, rect.Right], [rect.Top, rect.Bottom]));
        dc.DrawRectangle(brush, null, rect);
        dc.Pop();
    }
}
