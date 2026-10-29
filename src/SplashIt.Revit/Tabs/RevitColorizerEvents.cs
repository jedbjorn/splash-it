using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SplashIt.Core.Logging;
using SplashIt.Revit.Theming;
using SplashIt.UI.Tabs;

namespace SplashIt.Revit.Tabs;

/// <summary>
/// Revit's side of the colorizer's events (spec, DocumentTabColorizer): DocumentOpened and
/// DocumentCreated (→ opened), DocumentClosing then DocumentClosed (→ closed, only when the
/// close succeeded), DocumentSavedAs (and DocumentSaved when it changed the title or kind), the UI <c>ThemeChanged</c>, and layout passes through a
/// <see cref="LayoutPulse"/>. Linked documents are ignored. Subscriptions exist only between
/// <see cref="Subscribe"/> and <see cref="Unsubscribe"/>, so turning the colorizer off leaves
/// none behind. The document handlers run in Revit's API context and keep
/// <see cref="RevitDocuments"/> current there. Every handler catches its own exceptions.
/// </summary>
public sealed class RevitColorizerEvents : IColorizerEventSource
{
    private readonly UIControlledApplication _application;
    private readonly RevitDocuments _documents;
    private readonly ILog _log;
    private readonly LayoutPulse _pulse = new();
    private IColorizerEventSink? _sink;

    public RevitColorizerEvents(UIControlledApplication application, RevitDocuments documents, ILog? log = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _log = log ?? NullLog.Instance;
    }

    public ThemeKind CurrentTheme => SplashItTheme.Detect();

    public void Subscribe(IColorizerEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Unsubscribe();
        _sink = sink;
        var app = _application.ControlledApplication;
        app.DocumentOpened += OnOpened;
        app.DocumentCreated += OnCreated;
        app.DocumentClosing += OnClosing;
        app.DocumentClosed += OnClosed;
        app.DocumentSavedAs += OnSavedAs;
        app.DocumentSaved += OnSaved;
        _application.ThemeChanged += OnThemeChanged;
        _pulse.Start(OnLayoutUpdated);
    }

    public void Unsubscribe()
    {
        if (_sink is null) return;
        _sink = null;
        _pulse.Stop();
        var app = _application.ControlledApplication;
        app.DocumentOpened -= OnOpened;
        app.DocumentCreated -= OnCreated;
        app.DocumentClosing -= OnClosing;
        app.DocumentClosed -= OnClosed;
        app.DocumentSavedAs -= OnSavedAs;
        app.DocumentSaved -= OnSaved;
        _application.ThemeChanged -= OnThemeChanged;
    }

    private void OnOpened(object? sender, DocumentOpenedEventArgs e)
    {
        if (e.Status != RevitAPIEventStatus.Succeeded) return;
        Opened(e.Document, "opened");
    }

    private void OnCreated(object? sender, DocumentCreatedEventArgs e)
    {
        if (e.Status != RevitAPIEventStatus.Succeeded) return;
        Opened(e.Document, "created");
    }

    private void Opened(Autodesk.Revit.DB.Document? document, string how)
    {
        try
        {
            if (_sink is not { } sink || document is null || document.IsLinked) return;
            var key = _documents.Register(document);
            _log.Info($"events: document {how} ({key}, {_documents.Classify(key)}).");
            sink.OnDocumentOpened(key);
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a document " + how + " failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnClosing(object? sender, DocumentClosingEventArgs e)
    {
        try
        {
            if (e.Document is { IsLinked: false } document) _documents.BeginClosing(e.DocumentId, document);
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a document closing failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnClosed(object? sender, DocumentClosedEventArgs e)
    {
        try
        {
            var closed = e.Status == RevitAPIEventStatus.Succeeded;
            if (_documents.EndClosing(e.DocumentId, closed) is { } key && closed && _sink is { } sink)
            {
                _log.Info($"events: document closed ({key}).");
                sink.OnDocumentClosed(key);
            }
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a document closed failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnSavedAs(object? sender, DocumentSavedAsEventArgs e)
    {
        try
        {
            if (e.Status != RevitAPIEventStatus.Succeeded || e.Document is not { IsLinked: false } document || _sink is not { } sink) return;
            var key = _documents.Update(document);
            _log.Info($"events: document saved as ({key}, now {_documents.Classify(key)}).");
            sink.OnDocumentSavedAs(key);
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a document saved as failed (" + ex.GetType().Name + ").");
        }
    }

    /// <summary>
    /// A plain Save: when it changed the title or the kind (a first Save of a new document can
    /// arrive here rather than as SavedAs), it is handled like a Save As (review m4).
    /// </summary>
    private void OnSaved(object? sender, DocumentSavedEventArgs e)
    {
        try
        {
            if (e.Status != RevitAPIEventStatus.Succeeded || e.Document is not { IsLinked: false } document || _sink is not { } sink) return;
            var key = _documents.Update(document, out var changed);
            if (!changed) return;
            _log.Info($"events: document saved with a new title or kind ({key}, now {_documents.Classify(key)}).");
            sink.OnDocumentSavedAs(key);
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a document saved failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        try
        {
            if (e.ThemeChangedType != ThemeType.UITheme || _sink is not { } sink) return;
            sink.OnThemeChanged(SplashItTheme.Detect());
        }
        catch (Exception ex)
        {
            _log.Error("events: handling a theme change failed (" + ex.GetType().Name + ").");
        }
    }

    private void OnLayoutUpdated() => _sink?.OnLayoutUpdated();
}
