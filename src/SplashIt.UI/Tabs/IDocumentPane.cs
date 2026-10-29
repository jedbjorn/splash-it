namespace SplashIt.UI.Tabs;

/// <summary>
/// One pane of document tabs, as <see cref="TabGrouper"/> reorders it (spec #2, Architecture).
/// In Revit it is <c>AvalonDockPane</c>, the only class that reaches AvalonDock's layout model;
/// tests use a fake. Members may throw: the grouper catches everything. UI thread only.
/// </summary>
public interface IDocumentPane
{
    /// <summary>The number of tabs in the pane.</summary>
    int Count { get; }

    /// <summary>The tab at <paramref name="index"/>, in tab order. Its reference is the tab's identity.</summary>
    object ItemAt(int index);

    /// <summary>
    /// Moves the tab at <paramref name="from"/> so that it ends up at <paramref name="to"/>
    /// (remove, then insert; AvalonDock's <c>MoveChild</c>). Only the order changes.
    /// </summary>
    void Move(int from, int to);

    /// <summary>The selected tab, or null when none is. Setting it selects that tab of this pane.</summary>
    object? SelectedItem { get; set; }
}
