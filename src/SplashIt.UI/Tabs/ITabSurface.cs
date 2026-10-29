using System.Windows.Controls;

namespace SplashIt.UI.Tabs;

/// <summary>
/// One document tab as the surface sees it.
/// </summary>
/// <param name="Tab">The tab itself: its reference is the tab's stable identity, and the
/// colorizer reads and assigns its <c>Style</c>.</param>
/// <param name="Title">The tab's title (used for the change signature only; never logged).</param>
/// <param name="IsSelected">Whether the tab shows as selected.</param>
/// <param name="DocumentKey">The opaque key of the document the tab belongs to, as the
/// <see cref="IDocumentInfo"/> and the event source use it; null when the tab could not be
/// resolved to a document, and then the tab is left alone.</param>
public readonly record struct SurfaceTab(TabItem Tab, string Title, bool IsSelected, object? DocumentKey);

/// <summary>The result of reading the tabs: available, or unavailable with a reason.</summary>
public readonly record struct TabSurfaceResult
{
    private TabSurfaceResult(bool isAvailable, string? reason)
    {
        IsAvailable = isAvailable;
        Reason = reason;
    }

    public bool IsAvailable { get; }

    /// <summary>Why the tabs could not be read (type names and counts only; no titles or paths).</summary>
    public string? Reason { get; }

    public static TabSurfaceResult Available { get; } = new(true, null);

    public static TabSurfaceResult Unavailable(string reason) => new(false, reason);
}

/// <summary>
/// Where the document tabs live. In Revit it is <c>RevitTabSurface</c>, the only class that
/// touches Revit's internal UI tree; tests use a fake built from real WPF tabs.
/// </summary>
public interface ITabSurface
{
    /// <summary>
    /// Clears <paramref name="into"/> and fills it with the current document tabs in tab
    /// order. When the tabs cannot be found (an unexpected tree), returns
    /// <see cref="TabSurfaceResult.Unavailable"/>. Must not throw; the colorizer still guards
    /// against it. Called on every layout pass, so it should not allocate in steady state.
    /// </summary>
    TabSurfaceResult Read(List<SurfaceTab> into);

    /// <summary>Documents opened or closed: forget any cached tree positions before the next <see cref="Read"/>.</summary>
    void Invalidate();
}
