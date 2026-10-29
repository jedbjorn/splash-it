using SplashIt.Core.Files;

namespace SplashIt.UI.Tabs;

/// <summary>
/// The document-info abstraction: what the colorizer needs to know about open documents,
/// by opaque key. In Revit the key is a per-document object kept by the Revit adapter;
/// the kind comes from <see cref="FileKindClassifier"/>.
/// </summary>
public interface IDocumentInfo
{
    /// <summary>Clears <paramref name="into"/> and fills it with the keys of the open, non-linked documents.</summary>
    void GetOpenDocuments(List<object> into);

    /// <summary>
    /// The document's kind now (<see cref="FileKindClassifier.Classify"/> on what Revit
    /// reports about it); <see cref="FileKind.Other"/> for an unknown key. Must not throw.
    /// </summary>
    FileKind Classify(object documentKey);
}

/// <summary>
/// What the colorizer reacts to. Implemented privately by the colorizer and handed to the
/// <see cref="IColorizerEventSource"/> while it runs.
/// </summary>
public interface IColorizerEventSink
{
    /// <summary>A document was opened or created.</summary>
    void OnDocumentOpened(object documentKey);

    /// <summary>A document was closed.</summary>
    void OnDocumentClosed(object documentKey);

    /// <summary>A document was saved under a new name (its kind may have changed).</summary>
    void OnDocumentSavedAs(object documentKey);

    /// <summary>A layout pass finished somewhere in the UI (the hot path).</summary>
    void OnLayoutUpdated();

    /// <summary>The UI theme changed to <paramref name="theme"/>.</summary>
    void OnThemeChanged(ThemeKind theme);
}

/// <summary>
/// The event-source abstraction: Revit's document events, the docking host's layout
/// passes and theme changes. The colorizer subscribes on <c>Start</c> and unsubscribes on
/// <c>Stop</c>; an implementation holds no subscriptions of its own while unsubscribed.
/// </summary>
public interface IColorizerEventSource
{
    /// <summary>The UI theme now.</summary>
    ThemeKind CurrentTheme { get; }

    /// <summary>Starts delivering events to <paramref name="sink"/> (replacing any earlier sink).</summary>
    void Subscribe(IColorizerEventSink sink);

    /// <summary>Stops delivering events and releases every underlying subscription.</summary>
    void Unsubscribe();
}

/// <summary>
/// Tells whether another tab colorizer (pyRevit's) is running, checked before
/// <c>Start</c>, on every enable and on every Save (spec, pyRevit guard).
/// </summary>
public interface IForeignColorizerGuard
{
    /// <summary>True when another colorizer is running; <paramref name="detail"/> names it (no paths).</summary>
    bool IsRunning(out string? detail);
}

/// <summary>The colorizer's state, shown in the settings window's status line.</summary>
public enum ColorizerStatus
{
    /// <summary>Turned off in the settings (or not started).</summary>
    Off = 0,

    /// <summary>Running and coloring tabs.</summary>
    Active,

    /// <summary>Running, but the tabs cannot be found on this Revit build; tabs are untouched.</summary>
    Unavailable,

    /// <summary>Not started because pyRevit's tab colorizer is running.</summary>
    BlockedByPyRevit,

    /// <summary>Stopped for the session after repeated errors; tabs restored.</summary>
    Faulted,
}
