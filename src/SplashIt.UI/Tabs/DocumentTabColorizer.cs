using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SplashIt.Core.Colors;
using SplashIt.Core.Files;
using SplashIt.Core.Logging;
using SplashIt.Core.Settings;
using SplashIt.Core.Theming;

namespace SplashIt.UI.Tabs;

/// <summary>
/// Colors the document tabs by file (spec, Behavior and Architecture): projects get a
/// fill from Project colors, families a left bar from Family colors, everything else stays
/// native. It works against <see cref="ITabSurface"/>, <see cref="IDocumentInfo"/> and
/// <see cref="IColorizerEventSource"/>, so it has no Revit reference and runs in tests
/// against real WPF tabs.
/// <list type="bullet">
/// <item><b>Allocation</b>: one <see cref="ColorAllocator{TKey}"/> per list. A document takes
/// its color when it opens; documents already open at <see cref="Start"/> take theirs in tab
/// order; closing frees it; <see cref="ResetAssignments"/> reassigns in tab order.</item>
/// <item><b>Hot path</b>: each layout pass reads the tabs into a reused list and compares
/// a signature (tab identity, title, selected, document key, and the tab's current style)
/// with the last pass. Unchanged returns at once with no allocation and no style
/// assignment. It never reads <see cref="ColorAllocator{TKey}.Keys"/>, which allocates.</item>
/// <item><b>Restore</b>: a tab's original style (its local value, or none) is captured when
/// the tab is first seen and put back by reference when the tab is no longer colored, when
/// it disappears, on <see cref="Stop"/>, on a theme change and after a fault. Tabs of
/// other kinds, and tabs not resolved to a document, are never assigned.</item>
/// <item><b>Safe failure</b>: every handler catches its own exceptions. After
/// <see cref="MaxConsecutiveFaults"/> faults in a row the colorizer stops for the session,
/// restores the tabs and reports <see cref="ColorizerStatus.Faulted"/>.</item>
/// <item><b>Theme change</b>: restore, drop the captured originals, recapture them, recompute
/// the text contrast and reapply.</item>
/// <item><b>Registration fallback</b> (SC-004, #26): documents are registered from the open
/// and create events, and also when their tabs appear. While a pass finds tabs that resolve
/// to no document, the colorizer asks the event source for a document scan
/// (<see cref="IColorizerEventSource.RequestDocumentScan"/>), at most once per
/// <see cref="DocumentScanIntervalMs"/> and at most <see cref="MaxScansPerChange"/> times
/// after each change of the tabs (a pass a scan itself forces is not a change). The source runs it where the document list may be read
/// (Revit: the next Idling, in API context), and the scan registers every open document the
/// events did not announce, so its tabs are colored. With every tab resolved, the unchanged
/// hot path only compares a counter.</item>
/// </list>
/// Runs on the UI thread only: the public methods verify it.
/// </summary>
public sealed class DocumentTabColorizer
{
    /// <summary>Faults in a row that stop the colorizer for the session.</summary>
    public const int MaxConsecutiveFaults = 3;

    /// <summary>
    /// How many times within <see cref="RecaptureWindowMs"/> a tab may be restyled by someone
    /// else (and re-adopted) before the colorizer leaves it alone (review m5).
    /// </summary>
    public const int MaxRecaptures = 5;

    public const long RecaptureWindowMs = 10_000;

    /// <summary>The least time between two document scan requests (SC-004).</summary>
    public const long DocumentScanIntervalMs = 1_000;

    /// <summary>
    /// How many scans unresolved tabs may ask for after each change of the tabs: a tab that
    /// matches no document even after the scans (two files with the same title) stops asking
    /// until the tabs change again.
    /// </summary>
    public const int MaxScansPerChange = 3;

    private readonly ITabSurface _surface;
    private readonly IDocumentInfo _documents;
    private readonly IColorizerEventSource _events;
    private readonly ITabStyleFactory _styles;
    private readonly IForeignColorizerGuard? _guard;
    private readonly ILog _log;
    private readonly Dispatcher _dispatcher;
    private readonly Func<long> _clockMs;
    private readonly Sink _sink;

    private readonly Dictionary<object, FileKind> _kinds = new();
    private readonly Dictionary<TabItem, TabState> _tabs = new();

    /// <summary>
    /// What is behind the tabs, per pane (the tabs' visual parent; decision #33). Weak keys: a pane
    /// Revit drops is not kept alive with its tabs and documents (delta review D-m3). Worked out at the
    /// top of a pass, before any style is assigned (D-m1); dropped on a theme change and on Stop.
    /// </summary>
    private readonly ConditionalWeakTable<DependencyObject, PaneBackdrop> _backdrops = new();

    /// <summary>A pane's backdrop is still to find: pass again on the next layout.</summary>
    private bool _backdropPending;

    /// <summary>After a theme change, until the next layout pass: the window still draws the old theme, so nothing is sampled.</summary>
    private bool _backdropNeedsLayout;

    /// <summary>The panes met in this pass (reused; changed passes only).</summary>
    private readonly List<DependencyObject> _passPanes = new();

    /// <summary>The most passes one pane waits for a rendered sample before it settles for the tree's answer.</summary>
    internal const int MaxBackdropRetries = 20;

    /// <summary>One pane's backdrop: settled (<see cref="Done"/>, <see cref="Brush"/> null for "nothing solid"), or a guess while it waits.</summary>
    private sealed class PaneBackdrop
    {
        public bool Done;
        public Brush? Brush;
        public Brush? Guess;
        public int Retries;

        /// <summary>The color settled before the last theme change: a sample still showing it, while the tree says otherwise, is the old theme's paint (rc.3 F1).</summary>
        public Rgb? Stale;

        /// <summary>A sample that disagreed with the tree, waiting for a second, later sample to confirm it.</summary>
        public Rgb? Candidate;
    }

    /// <summary>Takes the rendered sample (<see cref="TabBackdrop.Rendered(DependencyObject, out string?)"/>); replaceable in tests.</summary>
    internal delegate (Rgb Color, string Source)? RenderedSampler(DependencyObject tab, out string? failure);

    /// <summary>The rendered sample (#33, step 1). Tests replace it to stand in for a window that has not repainted yet.</summary>
    internal RenderedSampler SampleRendered { get; set; } = TabBackdrop.Rendered;
    private readonly List<SurfaceTab> _read = new();
    private readonly List<Signature> _signature = new();
    private readonly List<TabItem> _drop = new();
    private readonly List<object> _keys = new();
    private readonly List<object> _gone = new();
    private readonly List<object> _projectOrder = new();
    private readonly List<object> _familyOrder = new();

    private ColorAllocator<object> _projects;
    private ColorAllocator<object> _families;
    private bool _running;
    private bool _signatureValid;
    private int _generation;
    private int _consecutiveFaults;
    private double _guardMs;
    private string _lastCounts = "";
    private int _lastScanUnresolved = -1;
    private int _unresolvedTabs;
    private int _scansLeft;
    private long _lastScanRequestMs = long.MinValue / 2;

    public DocumentTabColorizer(
        ITabSurface surface,
        IDocumentInfo documents,
        IColorizerEventSource events,
        ITabStyleFactory styles,
        IForeignColorizerGuard? guard = null,
        ILog? log = null,
        Func<long>? clockMs = null)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _styles = styles ?? throw new ArgumentNullException(nameof(styles));
        _guard = guard;
        _log = log ?? NullLog.Instance;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
        _sink = new Sink(this);
        _projects = new ColorAllocator<object>(Settings.ProjectColors.Count);
        _families = new ColorAllocator<object>(Settings.FamilyColors.Count);
    }

    /// <summary>Raised on the UI thread whenever <see cref="Status"/> or <see cref="StatusDetail"/> changes.</summary>
    public event EventHandler? StatusChanged;

    public ColorizerStatus Status { get; private set; } = ColorizerStatus.Off;

    /// <summary>Why the status is Unavailable, BlockedByPyRevit or Faulted (no titles or paths); else null.</summary>
    public string? StatusDetail { get; private set; }

    /// <summary>The settings last applied (the defaults until <see cref="Apply"/>).</summary>
    public SplashItSettings Settings { get; private set; } = SplashItSettings.Default;

    /// <summary>The theme the current styles were built for.</summary>
    public UiTheme Theme { get; private set; } = UiTheme.Light;

    /// <summary>Whether the colorizer is subscribed and coloring (Active or Unavailable).</summary>
    public bool IsRunning => _running;

    /// <summary>How many tabs carry a Splash-It style right now.</summary>
    public int StyledTabCount
    {
        get
        {
            var n = 0;
            foreach (var state in _tabs.Values)
                if (state.Applied is not null) n++;
            return n;
        }
    }

    /// <summary>
    /// Starts coloring with <see cref="Settings"/> (whether or not they are enabled; see
    /// <see cref="Apply"/>). Does nothing when already running or <see cref="ColorizerStatus.Faulted"/>.
    /// When pyRevit's colorizer is running, does not start and reports
    /// <see cref="ColorizerStatus.BlockedByPyRevit"/>. Never throws.
    /// </summary>
    public void Start()
    {
        _dispatcher.VerifyAccess();
        if (_running || Status == ColorizerStatus.Faulted) return;
        if (IsBlocked()) return;
        StartCore();
    }

    /// <summary>
    /// Restores every tab's original style, releases every subscription and every color,
    /// and reports <see cref="ColorizerStatus.Off"/> (a Faulted colorizer stays Faulted).
    /// Never throws.
    /// </summary>
    public void Stop()
    {
        _dispatcher.VerifyAccess();
        StopCore();
        if (Status != ColorizerStatus.Faulted) SetStatus(ColorizerStatus.Off, null);
    }

    /// <summary>
    /// Takes new settings (after a Save, or at startup): turned off → <see cref="Stop"/>;
    /// turned on → the pyRevit check, then start or restyle every tab at once. Invalid
    /// settings are refused and logged; a Faulted colorizer stays stopped for the session.
    /// Never throws.
    /// </summary>
    public void Apply(SplashItSettings settings)
    {
        _dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            _log.Warn(Inv($"colorizer: settings refused ({errors.Count} problems); keeping the previous settings."));
            return;
        }

        Settings = settings;
        if (Status == ColorizerStatus.Faulted)
        {
            _log.Info("colorizer: settings saved, but the colorizer stays stopped for this session after errors.");
            return;
        }

        if (!settings.Enabled)
        {
            Stop();
            return;
        }

        if (IsBlocked()) return;
        if (!_running)
        {
            StartCore();
            return;
        }

        try
        {
            ConfigureAllocators();
            _styles.ClearCache();
            _signatureValid = false;
            Pass(force: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("apply", ex);
        }
    }

    /// <summary>
    /// Runs the document scan now (the SC-004 fallback scan, without waiting for a request): for
    /// a caller that is already where <see cref="IDocumentInfo.GetOpenDocuments(List{object}, out int)"/>
    /// may run, such as a Revit command (Group Tabs, spec #2, review n3). Documents no event
    /// announced are registered, those no longer open are forgotten, a changed kind is handled
    /// like a Save As, and when anything changed (a title too) the tabs resolve again and are
    /// restyled. Returns false, and lists nothing, while not running (coloring off, faulted or
    /// blocked); the caller then refreshes the documents itself. Never throws.
    /// </summary>
    public bool ScanDocuments()
    {
        _dispatcher.VerifyAccess();
        if (!_running) return false;
        OnDocumentScan();
        return true;
    }

    /// <summary>
    /// Reassigns every open file's color from the start of its list, in current tab order
    /// (files with no tab follow, in the order they were registered), then restyles.
    /// Does nothing while not running. Never throws.
    /// </summary>
    public void ResetAssignments()
    {
        _dispatcher.VerifyAccess();
        if (!_running) return;
        try
        {
            _projectOrder.Clear();
            _familyOrder.Clear();
            if (_surface.Read(_read).IsAvailable)
            {
                foreach (var tab in _read)
                    if (tab.DocumentKey is { } key) AddInOrder(key, KindOf(key));
            }
            foreach (var (key, kind) in _kinds) AddInOrder(key, kind);

            _projects.Reset(_projectOrder);
            _families.Reset(_familyOrder);
            _projectOrder.Clear();
            _familyOrder.Clear();
            _log.Info("colorizer: color assignments reset in tab order.");
            _signatureValid = false;
            Pass(force: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("reset assignments", ex);
        }
    }

    private void AddInOrder(object key, FileKind kind)
    {
        var list = kind switch
        {
            FileKind.Project => _projectOrder,
            FileKind.Family => _familyOrder,
            _ => null,
        };
        if (list is not null && !list.Contains(key)) list.Add(key);
    }

    private bool IsBlocked()
    {
        string? detail = null;
        bool running;
        try
        {
            var t0 = Stopwatch.GetTimestamp();
            running = _guard?.IsRunning(out detail) == true;
            _guardMs = Ms(t0, Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            _log.Warn(Inv($"colorizer: the pyRevit check failed ({ex.GetType().Name}); starting anyway."));
            running = false;
        }
        if (!running) return false;

        if (_running) StopCore();
        SetStatus(ColorizerStatus.BlockedByPyRevit, detail ?? "pyRevit's tab colorizer is running.");
        return true;
    }

    private void StartCore()
    {
        try
        {
            // Split timings, so the start cost (AC11 step 7) can be judged part by part.
            var t0 = Stopwatch.GetTimestamp();
            Theme = _events.CurrentTheme;
            var t1 = Stopwatch.GetTimestamp();
            ConfigureAllocators();
            _kinds.Clear();
            _documents.GetOpenDocuments(_keys);
            foreach (var key in _keys) _kinds[key] = Classify(key);
            _keys.Clear();
            var t2 = Stopwatch.GetTimestamp();

            _events.Subscribe(_sink);
            _running = true;
            var t3 = Stopwatch.GetTimestamp();
            _signatureValid = false;
            SetStatus(ColorizerStatus.Active, null);
            Pass(force: true);
            var t4 = Stopwatch.GetTimestamp();
            _consecutiveFaults = 0;
            _log.Info(Inv($"colorizer: started; {_kinds.Count} documents open, {Theme} theme (pyRevit check {_guardMs:0.0} ms, theme {Ms(t0, t1):0.0} ms, documents {Ms(t1, t2):0.0} ms, subscribe {Ms(t2, t3):0.0} ms, first pass {Ms(t3, t4):0.0} ms)."));
        }
        catch (Exception ex)
        {
            // A start that fails part-way must not leave subscriptions or styles behind.
            _log.Error(Inv($"colorizer: start failed ({ex.GetType().Name} in {Site(ex)})."));
            StopCore();
            SetStatus(ColorizerStatus.Faulted, "Start failed.");
        }
    }

    private void StopCore()
    {
        if (_running)
        {
            try
            {
                _events.Unsubscribe();
            }
            catch (Exception ex)
            {
                _log.Warn(Inv($"colorizer: unsubscribing failed ({ex.GetType().Name})."));
            }
            _running = false;
        }
        RestoreAll();
        _backdrops.Clear();
        _backdropPending = false;
        _backdropNeedsLayout = false;
        _kinds.Clear();
        _projects.Clear();
        _families.Clear();
        _signatureValid = false;
        _lastCounts = "";
        _lastScanUnresolved = -1;
        _unresolvedTabs = 0;
        _scansLeft = 0;
        try
        {
            _styles.ClearCache();
        }
        catch (Exception)
        {
            // A cache that cannot clear holds only styles; nothing to undo.
        }
    }

    private void ConfigureAllocators()
    {
        _projects.SetColorCount(Settings.ProjectColors.Count);
        _families.SetColorCount(Settings.FamilyColors.Count);
    }

    // ---- events (the sink) ----

    private void OnLayoutUpdated()
    {
        if (!_running) return;
        try
        {
            _backdropNeedsLayout = false;
            Pass(force: false, fromLayout: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("layout pass", ex);
        }
    }

    private void OnDocumentOpened(object key)
    {
        if (!_running) return;
        try
        {
            var kind = Classify(key);
            _kinds[key] = kind;
            AllocatorFor(kind)?.Assign(key);
            _surface.Invalidate();
            _signatureValid = false;
            Pass(force: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("document opened", ex);
        }
    }

    private void OnDocumentClosed(object key)
    {
        if (!_running) return;
        try
        {
            Forget(key);
            _surface.Invalidate();
            _signatureValid = false;
            Pass(force: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("document closed", ex);
        }
    }

    private void OnDocumentSavedAs(object key)
    {
        if (!_running) return;
        try
        {
            var kind = Classify(key);
            if (!_kinds.TryGetValue(key, out var old) || old != kind)
            {
                Forget(key);
                _kinds[key] = kind;
                AllocatorFor(kind)?.Assign(key);
                _log.Info(Inv($"colorizer: a document saved under a new name is now {kind} (was {old})."));
            }
            _surface.Invalidate(); // its title changed, so its tabs resolve again
            _signatureValid = false;
            Pass(force: true);
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("document saved as", ex);
        }
    }

    private void OnThemeChanged(UiTheme theme)
    {
        if (!_running) return;
        try
        {
            _log.Info(Inv($"colorizer: theme changed to {theme}; restyling."));
            Theme = theme;
            RestoreAll();           // originals back on every tab...
            ForgetBackdrops();      // ...what is behind the tabs found again, the old color remembered as stale...
            _backdropNeedsLayout = true; // what is drawn is the old theme until the next layout
            _styles.ClearCache();   // ...styles rebuilt for the new backdrop...
            _signatureValid = false;
            Pass(force: true);      // ...originals recaptured, contrast recomputed, reapplied.
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            Fault("theme change", ex);
        }
    }

    /// <summary>
    /// The fallback scan (SC-004): registers every open document no event announced, takes a
    /// changed kind of a known one like a Save As (review m1), and forgets those no longer open.
    /// When anything changed, including only a title, the surface forgets its resolutions (a tab
    /// that matched nothing may match now) and the tabs are restyled. Each scan is logged with
    /// what it found and the unresolved tabs left. Runs where the source allows the document
    /// list to be read.
    /// </summary>
    private void OnDocumentScan()
    {
        if (!_running) return;
        try
        {
            _documents.GetOpenDocuments(_keys, out var retitled);
            int projects = 0, families = 0, others = 0, rekinded = 0;
            foreach (var key in _keys)
            {
                var kind = Classify(key);
                if (_kinds.TryGetValue(key, out var old))
                {
                    if (old == kind) continue;
                    // A kind no event reported (a missed Save As): as OnDocumentSavedAs.
                    Forget(key);
                    _kinds[key] = kind;
                    AllocatorFor(kind)?.Assign(key);
                    rekinded++;
                    continue;
                }
                _kinds[key] = kind;
                AllocatorFor(kind)?.Assign(key);
                switch (kind)
                {
                    case FileKind.Project: projects++; break;
                    case FileKind.Family: families++; break;
                    default: others++; break;
                }
            }
            foreach (var key in _kinds.Keys)
                if (!_keys.Contains(key)) _gone.Add(key);
            foreach (var key in _gone) Forget(key);
            var gone = _gone.Count;
            _gone.Clear();
            _keys.Clear();

            var added = projects + families + others;
            var found = added + gone + retitled + rekinded > 0;
            if (found)
            {
                _surface.Invalidate(); // tabs that resolved to nothing resolve again
                _signatureValid = false;
                // The scan's own follow-up pass does not refill the scan budget (delta review d1):
                // only a change of the tabs or a document event does, so scans stay bounded.
                Pass(force: true, refillScans: false);
            }
            // Logged when it found something, or when the unresolved count moved (delta review d3).
            if (found || _unresolvedTabs != _lastScanUnresolved)
            {
                _lastScanUnresolved = _unresolvedTabs;
                _log.Info(Inv($"colorizer: a document scan registered {added} documents no event announced ({projects} project, {families} family, {others} other); {retitled} changed title or kind, {rekinded} reclassified; {gone} no longer open; {_unresolvedTabs} unresolved."));
            }
            _consecutiveFaults = 0;
        }
        catch (Exception ex)
        {
            _keys.Clear();
            _gone.Clear();
            Fault("document scan", ex);
        }
    }

    private void Forget(object key)
    {
        _kinds.Remove(key);
        _projects.Release(key);
        _families.Release(key);
    }

    // ---- the pass ----

    /// <param name="force">Restyle even when the signature is unchanged.</param>
    /// <param name="refillScans">Whether unresolved tabs after this pass refill the scan budget
    /// (false for the pass a scan forces, delta review d1).</param>
    /// <param name="fromLayout">Run from a layout pass: render data is current, so a pane's backdrop may be sampled (#33).</param>
    private void Pass(bool force, bool refillScans = true, bool fromLayout = false)
    {
        var result = _surface.Read(_read);
        if (!result.IsAvailable)
        {
            if (_tabs.Count > 0) RestoreAll();
            _signatureValid = false;
            _unresolvedTabs = 0;
            SetStatus(ColorizerStatus.Unavailable, result.Reason ?? "The document tabs were not found.");
            return;
        }

        if (!force && _signatureValid && Status == ColorizerStatus.Active && SignatureMatches())
        {
            MaybeRequestScan();
            return;
        }

        SetStatus(ColorizerStatus.Active, null);
        var generation = ++_generation;
        int projects = 0, families = 0, others = 0, unresolved = 0;

        // Before any style is assigned in this pass: what is behind each pane's tabs (#33).
        ResolveBackdrops(mayRender: fromLayout);

        foreach (var entry in _read)
        {
            var tab = entry.Tab;
            var state = StateFor(tab);
            state.Generation = generation;

            var desired = Desired(entry, state, ref projects, ref families, ref others, ref unresolved);
            if (desired is null)
            {
                Restore(tab, state);
            }
            else if (!ReferenceEquals(tab.Style, desired))
            {
                tab.Style = desired;
                state.Applied = desired;
            }
            else
            {
                state.Applied = desired;
            }
        }

        // Tabs that disappeared: put their original back (they may come back) and forget them.
        foreach (var (tab, state) in _tabs)
            if (state.Generation != generation) _drop.Add(tab);
        foreach (var tab in _drop)
        {
            Restore(tab, _tabs[tab]);
            _tabs.Remove(tab);
        }
        _drop.Clear();

        RecordSignature();
        LogCounts(projects, families, others, unresolved);

        _unresolvedTabs = unresolved;
        if (unresolved > 0 && refillScans) _scansLeft = MaxScansPerChange;
        MaybeRequestScan();
    }

    /// <summary>
    /// Asks for a document scan while tabs resolve to no document (SC-004): throttled, and no
    /// allocation, so it may run on the unchanged hot path.
    /// </summary>
    private void MaybeRequestScan()
    {
        if (_unresolvedTabs == 0 || _scansLeft <= 0) return;
        var now = _clockMs();
        if (now - _lastScanRequestMs < DocumentScanIntervalMs) return;
        _lastScanRequestMs = now;
        _scansLeft--;
        _events.RequestDocumentScan();
    }

    private Style? Desired(SurfaceTab entry, TabState state, ref int projects, ref int families, ref int others, ref int unresolved)
    {
        if (entry.DocumentKey is not { } key)
        {
            unresolved++;
            return null;
        }

        var kind = KindOf(key);
        TabTreatment treatment;
        switch (kind)
        {
            case FileKind.Project:
                projects++;
                treatment = TabTreatment.ProjectFill(Settings.ProjectColors[_projects.Assign(key)], Settings);
                break;
            case FileKind.Family:
                families++;
                treatment = TabTreatment.FamilyBar(Settings.FamilyColors[_families.Assign(key)], Settings);
                break;
            default:
                others++;
                return null;
        }

        if (state.Unstylable || state.Abandoned) return null;
        try
        {
            // A project's text and × are chosen against what shows behind the tab (#31/#36: the fill
            // is translucent); a family bar has no text, so its backdrop does not matter.
            var backdrop = kind == FileKind.Project ? BackdropFor(entry.Tab, state) : state.OriginalBackground;
            return _styles.Create(state.BaseStyle, treatment, backdrop, Theme);
        }
        catch (ArgumentException)
        {
            // The tab's own style targets a type a TabItem style cannot derive from: leave it native.
            state.Unstylable = true;
            _log.Warn(Inv($"colorizer: a tab's style targets {state.BaseStyle?.TargetType.Name}; that tab stays native."));
            return null;
        }
    }

    /// <summary>
    /// The backdrop brush for a project tab's contrast: its pane's, as <see cref="ResolveBackdrops"/>
    /// left it at the top of this pass (settled, or the tree's guess while it waits); else (not in a
    /// tree, or not laid out) the tab's own original background, which the factory blends over the
    /// theme surface. Looks up only: it never samples, so it cannot read a tab styled earlier in the pass.
    /// </summary>
    private Brush? BackdropFor(TabItem tab, TabState state)
    {
        if (VisualTreeHelper.GetParent(tab) is not { } pane || !_backdrops.TryGetValue(pane, out var backdrop))
            return state.OriginalBackground;
        return (backdrop.Done ? backdrop.Brush : backdrop.Guess) ?? state.OriginalBackground;
    }

    /// <summary>
    /// Decision #33, at the top of a pass, before any style is assigned (delta review D-m1): works out
    /// each pane's backdrop once. A rendered sample is taken only in a pass run from a layout pass
    /// (<paramref name="mayRender"/>; render data is current then), and not before the first layout
    /// after a theme change. Until a sample is taken the pane uses the tree's answer, uncached, and the
    /// next layout passes look again, at most <see cref="MaxBackdropRetries"/> times per pane (D-m2);
    /// then the tree's answer is settled. Every settled pane is logged once, with what each method gave.
    /// </summary>
    private void ResolveBackdrops(bool mayRender)
    {
        _passPanes.Clear();
        foreach (var entry in _read)
            if (VisualTreeHelper.GetParent(entry.Tab) is { } pane && !_passPanes.Contains(pane))
                _passPanes.Add(pane);

        foreach (var pane in _passPanes)
        {
            var backdrop = _backdrops.GetValue(pane, static _ => new PaneBackdrop());
            if (backdrop.Done) continue;
            var tab = FirstTab(pane);
            if (tab is null || pane is not FrameworkElement { ActualWidth: > 0 })
            {
                // Not laid out yet: nothing to read; look again on the next layout pass (bounded).
                if (backdrop.Retries++ < MaxBackdropRetries) _backdropPending = true;
                continue;
            }

            (Rgb Color, string Source)? rendered = null;
            string? failure = "not taken yet: waiting for a layout pass";
            long sampleTicks = 0;
            if (mayRender && !_backdropNeedsLayout)
            {
                var t0 = Stopwatch.GetTimestamp();
                rendered = SampleRendered(tab, out failure);
                sampleTicks = Stopwatch.GetTimestamp() - t0;
                if (rendered is { } r && !Confirmed(backdrop, r.Color, tab, out var why))
                {
                    rendered = null;
                    failure = why;
                }
            }
            if (rendered is null && backdrop.Retries++ < MaxBackdropRetries)
            {
                _backdropPending = true;
                backdrop.Guess = TabBackdrop.FromTree(tab) is { } guess ? Frozen(guess.Color) : null;
                continue;
            }

            backdrop.Done = true;
            backdrop.Guess = null;
            backdrop.Stale = null;
            backdrop.Candidate = null;
            var sampled = TabBackdrop.Describe(rendered, failure);
            if ((rendered ?? TabBackdrop.FromTree(tab)) is { } behind)
            {
                backdrop.Brush = Frozen(behind.Color);
                _log.Info(Inv($"colorizer: tabs sit on {behind.Color.ToHex()} ({behind.Source}); project text and x are chosen against it (sample {Ms(0, sampleTicks):0.0} ms). [{TabBackdrop.Explain(tab, sampled)}]"));
            }
            else
            {
                _log.Info("colorizer: nothing solid behind the tabs; project text and x are chosen against each tab's own background, else the theme surface. [" + TabBackdrop.Explain(tab, sampled) + "]");
            }
        }
    }

    /// <summary>
    /// Whether a rendered sample of <paramref name="color"/> may settle the pane (rc.3 F1). A sample that
    /// agrees with the tree's answer (the painted-layer search, else the ancestor walk), or when the tree
    /// has none, is taken. One that disagrees is taken only once a later layout pass samples the same
    /// color again, and never while it is the color settled before the last theme change: then the
    /// window has not repainted yet (Revit 2027 lays out before it repaints the strip), and the tree,
    /// which already moved with the theme, is believed. Until then the pane keeps waiting, within its
    /// budget, and settles for the tree's answer when the budget runs out.
    /// </summary>
    private static bool Confirmed(PaneBackdrop backdrop, Rgb color, TabItem tab, out string? why)
    {
        why = null;
        if (TabBackdrop.FromTree(tab) is not { } tree || tree.Color == color) return true;
        if (backdrop.Stale == color)
        {
            why = Inv($"the sample {color.ToHex()} is the color before the theme change, the tree says {tree.Color.ToHex()}: not repainted yet");
            return false;
        }
        if (backdrop.Candidate == color) return true; // two layout passes agree: what is drawn
        backdrop.Candidate = color;
        why = Inv($"the sample {color.ToHex()} differs from the tree's {tree.Color.ToHex()}: waiting for a second sample");
        return false;
    }

    /// <summary>
    /// After a theme change: every pane's backdrop is found again, and the color each had settled on is
    /// kept as <see cref="PaneBackdrop.Stale"/>, so a sample of the old paint is not taken for the new one.
    /// </summary>
    private void ForgetBackdrops()
    {
        foreach (var (_, backdrop) in _backdrops)
        {
            backdrop.Stale = backdrop.Done && backdrop.Brush is SolidColorBrush b ? new Rgb(b.Color.R, b.Color.G, b.Color.B) : backdrop.Stale;
            backdrop.Done = false;
            backdrop.Brush = null;
            backdrop.Guess = null;
            backdrop.Candidate = null;
            backdrop.Retries = 0;
        }
    }

    /// <summary>A tab of <paramref name="pane"/> read in this pass.</summary>
    private TabItem? FirstTab(DependencyObject pane)
    {
        foreach (var entry in _read)
            if (ReferenceEquals(VisualTreeHelper.GetParent(entry.Tab), pane)) return entry.Tab;
        return null;
    }

    private static Brush Frozen(Rgb c)
    {
        var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private FileKind KindOf(object key)
    {
        if (_kinds.TryGetValue(key, out var kind)) return kind;
        // A document the events did not announce (open before Start, or missed): classify it now.
        kind = Classify(key);
        _kinds[key] = kind;
        return kind;
    }

    private FileKind Classify(object key)
    {
        try
        {
            return _documents.Classify(key);
        }
        catch (Exception ex)
        {
            _log.Warn(Inv($"colorizer: classifying a document failed ({ex.GetType().Name}); it stays native."));
            return FileKind.Other;
        }
    }

    private ColorAllocator<object>? AllocatorFor(FileKind kind) => kind switch
    {
        FileKind.Project => _projects,
        FileKind.Family => _families,
        _ => null,
    };

    /// <summary>
    /// The tab's captured state; captured now when the tab is new, or when something other
    /// than Splash-It replaced its style since (the host restyled it, or recycled it).
    /// </summary>
    private TabState StateFor(TabItem tab)
    {
        var current = tab.Style;
        if (_tabs.TryGetValue(tab, out var state))
        {
            if (state.Abandoned) return state;
            var expected = state.Applied ?? state.BaseStyle;
            if (ReferenceEquals(current, expected) || TabStyleFactory.IsDerived(current)) return state;

            // Replaced by someone else: what is there now is the new original. Someone who
            // keeps doing it (another colorizer, a binding) would make every pass restyle the
            // tab; past the limit the tab is theirs and is left alone (review m5).
            var now = _clockMs();
            if (now - state.RecaptureWindowStart > RecaptureWindowMs)
            {
                state.RecaptureWindowStart = now;
                state.Recaptures = 0;
            }
            state.Applied = null;
            if (++state.Recaptures > MaxRecaptures)
            {
                state.Abandoned = true;
                _log.Warn(Inv($"colorizer: a tab was restyled by something else {MaxRecaptures} times in {RecaptureWindowMs / 1000} s; it is left alone."));
                return state;
            }
            state.Capture(tab);
            return state;
        }

        state = new TabState();
        state.Capture(tab);
        _tabs.Add(tab, state);
        return state;
    }

    private void RestoreAll()
    {
        foreach (var (tab, state) in _tabs)
        {
            try
            {
                Restore(tab, state);
            }
            catch (Exception ex)
            {
                _log.Warn(Inv($"colorizer: restoring a tab failed ({ex.GetType().Name})."));
            }
        }
        _tabs.Clear();
        _signatureValid = false;
    }

    /// <summary>Puts the original style back by reference, if the tab still carries ours.</summary>
    private static void Restore(TabItem tab, TabState state)
    {
        if (state.Applied is null) return;
        var current = tab.Style;
        state.Applied = null;
        if (!TabStyleFactory.IsDerived(current)) return; // someone else's style now: leave it

        switch (state.OriginalLocal)
        {
            case BindingBase binding:
                BindingOperations.SetBinding(tab, FrameworkElement.StyleProperty, binding);
                break;
            case Style style:
                tab.Style = style;
                break;
            default:
                tab.ClearValue(FrameworkElement.StyleProperty);
                break;
        }
    }

    // ---- the signature ----

    private bool SignatureMatches()
    {
        if (_signature.Count != _read.Count) return false;
        for (var i = 0; i < _read.Count; i++)
        {
            var now = _read[i];
            var then = _signature[i];
            if (!ReferenceEquals(now.Tab, then.Tab)
                || now.IsSelected != then.IsSelected
                || !Equals(now.DocumentKey, then.DocumentKey)
                || !string.Equals(now.Title, then.Title, StringComparison.Ordinal)
                || !ReferenceEquals(now.Tab.Style, then.Style))
                return false;
        }
        return true;
    }

    private void RecordSignature()
    {
        _signature.Clear();
        foreach (var entry in _read)
            _signature.Add(new Signature(entry.Tab, entry.Title, entry.IsSelected, entry.DocumentKey, entry.Tab.Style));
        // A pane not laid out yet had no backdrop to find: pass again on the next layout, a bounded
        // number of times (a pane that never lays out must not keep the hot path busy).
        _signatureValid = !_backdropPending; // a pane still to settle: pass again on the next layout (bounded per pane)
        _backdropPending = false;
    }

    // ---- status, faults, logging ----

    private void Fault(string where, Exception ex)
    {
        _consecutiveFaults++;
        _log.Error(Inv($"colorizer: {where} failed ({ex.GetType().Name} in {Site(ex)}); fault {_consecutiveFaults} of {MaxConsecutiveFaults}."));
        if (_consecutiveFaults < MaxConsecutiveFaults) return;

        _log.Error("colorizer: stopped for this session after repeated errors; tabs restored.");
        StopCore();
        SetStatus(ColorizerStatus.Faulted, Inv($"Stopped after {MaxConsecutiveFaults} errors in a row."));
    }

    private void SetStatus(ColorizerStatus status, string? detail)
    {
        if (Status == status && StatusDetail == detail) return;
        Status = status;
        StatusDetail = detail;
        switch (status)
        {
            case ColorizerStatus.Unavailable:
                _log.Warn("colorizer: status Unavailable: " + detail);
                break;
            case ColorizerStatus.BlockedByPyRevit:
                _log.Warn("colorizer: status BlockedByPyRevit: " + detail);
                break;
            case ColorizerStatus.Faulted:
                break; // logged by Fault / StartCore
            default:
                _log.Info("colorizer: status " + status);
                break;
        }
        try
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A subscriber's failure (the settings window) must not reach Revit's dispatcher (review m6).
            _log.Warn("colorizer: a status listener failed (" + ex.GetType().Name + ").");
        }
    }

    private void LogCounts(int projects, int families, int others, int unresolved)
    {
        var counts = Inv($"{_read.Count} tabs: {projects} project, {families} family, {others} other, {unresolved} unresolved");
        if (counts == _lastCounts) return;
        _lastCounts = counts;
        _log.Info("colorizer: " + counts + ".");
    }

    private static string Site(Exception ex) => ex.TargetSite?.Name ?? "?";

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private readonly record struct Signature(TabItem Tab, string Title, bool IsSelected, object? DocumentKey, Style? Style);

    private sealed class TabState
    {
        /// <summary>The tab's local Style value at capture: a Style, a binding, or <see cref="DependencyProperty.UnsetValue"/>.</summary>
        public object? OriginalLocal { get; private set; }

        /// <summary>The tab's effective style at capture (local or implicit): what ours are BasedOn.</summary>
        public Style? BaseStyle { get; private set; }

        /// <summary>The tab's background under its original style, for text contrast.</summary>
        public Brush? OriginalBackground { get; private set; }

        /// <summary>The Splash-It style on the tab now; null when it carries its original.</summary>
        public Style? Applied { get; set; }

        public int Generation { get; set; }

        public bool Unstylable { get; set; }

        /// <summary>Left alone for good: something else keeps restyling it (review m5).</summary>
        public bool Abandoned { get; set; }

        public int Recaptures { get; set; }

        public long RecaptureWindowStart { get; set; } = long.MinValue / 2;

        public void Capture(TabItem tab)
        {
            var local = tab.ReadLocalValue(FrameworkElement.StyleProperty);
            var current = tab.Style;
            Unstylable = false;
            if (TabStyleFactory.IsDerived(current))
            {
                // Ours, left from an earlier run we lost track of: its base is the original.
                OriginalLocal = (object?)current!.BasedOn ?? DependencyProperty.UnsetValue;
                BaseStyle = current.BasedOn;
                OriginalBackground = null;
                Applied = current;
                return;
            }

            OriginalLocal = local switch
            {
                BindingExpressionBase expression => expression.ParentBindingBase,
                Style style => style,
                _ => DependencyProperty.UnsetValue,
            };
            BaseStyle = current;
            OriginalBackground = tab.Background;
        }
    }

    /// <summary>The colorizer's side of the event source, kept private so only the source can call it.</summary>
    private sealed class Sink(DocumentTabColorizer owner) : IColorizerEventSink
    {
        public void OnDocumentOpened(object documentKey) => owner.OnDocumentOpened(documentKey);

        public void OnDocumentClosed(object documentKey) => owner.OnDocumentClosed(documentKey);

        public void OnDocumentSavedAs(object documentKey) => owner.OnDocumentSavedAs(documentKey);

        public void OnLayoutUpdated() => owner.OnLayoutUpdated();

        public void OnThemeChanged(UiTheme theme) => owner.OnThemeChanged(theme);

        public void OnDocumentScan() => owner.OnDocumentScan();
    }
}
