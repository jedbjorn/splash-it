# Developing Splash-It

Notes for working on Splash-It itself. For what it is, see the [README](../README.md).

Splash-It is a sister app of [gantt-it](https://github.com/jedbjorn/gantt-it) and follows
its build layout. Files copied or adapted from gantt-it name their gantt-it source path
and commit in a header comment. `global.json` (no comment syntax) is gantt-it's, as is.

## Layout

```text
src/
  SplashIt.Core/         net8.0;net10.0 — settings and their defaults, export/import,
                         file-kind classification, color allocation, color math.
                         No Revit, no WPF, no NuGet packages.
    Colors/              Rgb, ColorMath, ColorAllocator<TKey>, RecentColors
    Theming/, Picking/   UiPalette (light and dark window palettes, WCAG-checked) and
                         ScreenSampling, vendored from gantt-it
    Files/               FileKind, FileKindClassifier
    Settings/            SplashItSettings (Read/Write/Validate), SplashItDefaults,
                         SettingsExchange (export/import), the shared JSON reader
    Resources/           CoreStrings.resx (user-facing messages) + CoreStrings.cs
    Logging/             FileLog (%LocalAppData%\SplashIt\logs, 7 days), ILog
  SplashIt.UI/           net8.0-windows;net10.0-windows, WPF, NO Revit references:
                         the tab style factory, the colorizer engine (against
                         ITabSurface and a document-info abstraction), the settings
                         window and view-models, the vendored theme and color picker.
                         Theme detection reaches it as an input.
    Theme.xaml, Theming/ gantt-it's theme (vendored); SplashItTheme.Apply(theme) sets
                         the light or dark palette for windows opened after it
    Mvvm/, Dialogs/,     gantt-it's MVVM base, dialogs and color picker (vendored),
    Swatches/            strings in Resources/UiStrings.resx
    Settings/            SettingsWindow + SettingsViewModel, ISettingsHost (the
                         Revit-facing calls), SettingsLauncher.Show(owner, host),
                         Preview/ (the sample tab strip)
    Tabs/                TabStyleFactory (ITabStyleFactory), TabTreatment,
                         TabDecoration (adorner: family bar, selected accent),
                         DocumentTabColorizer, ITabSurface + TreeTabSurface (the
                         tree walk by type name), LayoutPulse, PyRevitGuard
  SplashIt.Revit/        the add-in (net8.0-windows for 2025 and 2026,
                         net10.0-windows for 2027): App, the command, the Revit tab
                         surface, Revit event and theme adapters, logger wiring.
                         References SplashIt.UI and SplashIt.Core.
    App.cs               IExternalApplication: ribbon button, icons, colorizer start
    Commands/            OpenSettings → SettingsLauncher.Show (the settings window),
                         over ColorizerSettingsHost
    Tabs/                RevitTabSurface (the only class touching Revit's UI tree),
                         RevitDocuments (keys, titles, kinds; API context only),
                         RevitColorizerEvents, TabDiagnostics (Debug only)
    Theming/             RevitThemeDetector.Detect() → UiTheme, pushed into SplashItTheme
    Resources/           RevitStrings.resx; icons\splash_{16,32}[_dark].png
    SplashIt.addin       manifest (VendorId DSOS; the ClientId never changes)
tests/
  SplashIt.Core.Tests/   xUnit, net8.0;net10.0; runs on any OS (CI runs it on Linux
                         and on Windows)
  SplashIt.RuntimeTests/ xUnit, net8.0-windows with WPF, references SplashIt.UI, no
                         Revit; runs on Windows only (CI). Renders WPF off-screen to PNG.
docs/                    this file, RELEASING.md
installer/               the per-user WiX MSI (Product.wxs, SplashIt.Installer.wixproj),
                         stage.sh, inspect-msi.ps1, test-detection.ps1, branding/
tools/                   glyph.py (the splash glyph), make_icons.py (ribbon icons),
                         make_installer_branding.py (setup artwork, license page)
```

## Configurations

The MSBuild configuration picks the Revit year (`Directory.Build.props`):

| Configuration | Revit | Add-in target framework |
|---|---|---|
| `Debug R25`, `Release R25` | 2025 | `net8.0-windows` |
| `Debug R26`, `Release R26` | 2026 | `net8.0-windows` |
| `Debug R27`, `Release R27` | 2027 | `net10.0-windows` |

`SplashIt.Core` and its tests ignore the Revit suffix and build `net8.0` and `net10.0`
in every configuration. `SplashIt.UI` builds `net8.0-windows` and `net10.0-windows` in
every configuration, and each consumer picks the nearest: `SplashIt.Revit` gets the
`net8.0-windows` build for R25 and R26 and the `net10.0-windows` build for R27, and
`SplashIt.RuntimeTests` (`net8.0-windows` in every configuration) gets `net8.0-windows`.

The split exists for testing (spec #1, Project split, decision #16): the Nice3point
packages are reference-only, so a test that loads a type touching the Revit API fails
at runtime, and R27's `net10.0-windows` add-in can't be referenced from a
`net8.0-windows` test project. Everything the runtime tests exercise lives in
`SplashIt.UI`, which never references Revit; `SplashIt.Revit` stays a thin adapter. The Revit API comes from the reference-only `Nice3point.Revit.Api.*`
packages (versions per year in `Directory.Packages.props`, which manages every package
version centrally), so everything builds without Revit installed, including on Linux.

The version is `0.0.0-dev` unless a build passes `-p:SplashItVersion=<version>`
(the release pipeline will pass the tag without its `v`).

## Building and testing locally

With the .NET 8 and .NET 10 SDKs installed:

```sh
dotnet test tests/SplashIt.Core.Tests -c "Release R26"
dotnet build SplashIt.sln -c "Debug R26"
```

### Without a local SDK (Docker)

The `mcr.microsoft.com/dotnet/sdk:10.0` image builds every target. It has only the
.NET 10 runtime, so `DOTNET_ROLL_FORWARD=Major` lets the `net8.0` tests run on it.
Run the container as root (with rootless Docker, files still land owned by you), and
bind-mount a real host path:

```sh
docker run --rm -e NUGET_PACKAGES=/nuget -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_ROLL_FORWARD=Major \
  -v "$HOME/.cache/splashit-nuget:/nuget" -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'dotnet test tests/SplashIt.Core.Tests -c "Release R26" &&
         for c in "Debug R25" "Release R25" "Debug R26" "Release R26" "Debug R27" "Release R27"; do
           dotnet build SplashIt.sln -c "$c" --nologo || exit 1; done'
```

`SplashIt.RuntimeTests` builds in that loop (it sets `EnableWindowsTargeting`) but only
runs on Windows, so its tests run in CI.

## Runtime tests and screenshots

`tests/SplashIt.RuntimeTests` holds WPF tests that need Windows but not Revit. Its
`Support/Screenshots` helper is the screenshot pipeline: `RunSta` runs WPF code on an STA
thread, `Render` lays an element out off-screen and renders it with `RenderTargetBitmap`,
and `Save` writes `<repo>/TestResults/screenshots/<name>.png` (or under
`$SPLASHIT_SCREENSHOTS` when set). `ScreenshotPipelineTests` is the smoke test: it draws
both default color lists on the light and dark theme surfaces and checks the PNG is not
blank. `UiProjectTests` checks that `SplashIt.UI` loads here and references no Revit
assembly.

`Settings/SettingsViewModelTests` drive the settings view-model against a fake
`ISettingsHost` and a fake `IDialogService` (`Support/Fakes`). `Settings/SettingsWindowScreenshotTests`
show the real windows off-screen (`Support/WindowCapture`: shown at -32000 without
activation, client area only, so the DWM title bar is not in the PNG) and render every
status state, a 12-color list, the color picker on a chip, import errors and the preview
strip, in light and dark. Tests that open windows share the `WpfThemeCollection`, which
runs them one at a time because `SplashItTheme.Current` is process-wide.

Runtime tests reference `SplashIt.UI` and `SplashIt.Core`, never `SplashIt.Revit`.

## CI

`.github/workflows/build.yml` runs on every pull request and push to `main`, through the
reusable `_build.yml`. Every job is required:

| Job | Runner | What it does |
|---|---|---|
| Core tests | `ubuntu-latest` | `SplashIt.Core.Tests` on `net8.0` and `net10.0` |
| Build `<configuration>` × 6 | `ubuntu-latest` | `SplashIt.sln` in each configuration; the Debug legs upload `SplashIt-Debug-R25` / `-R26` / `-R27` |
| Windows tests (Core + runtime, WPF) | `windows-latest` | `SplashIt.Core.Tests` on `net8.0` and `net10.0` again, for Windows file semantics; then `SplashIt.RuntimeTests`; uploads the PNGs as `SplashIt-screenshots` |
| MSI (Release R25 + R26 + R27) | `windows-latest` | builds, stages and packs the per-user MSI, checks it with `installer/inspect-msi.ps1`; uploads `SplashIt-msi` and `SplashIt-zips` |
| MSI detection (silent installs, fake Revit) | `windows-latest` | `installer/test-detection.ps1`: real `msiexec /qn` installs against fake Revit installs; uploads `SplashIt-detection` |

The installer, the release workflow and the detection test are described in
[RELEASING.md](RELEASING.md).

The `SplashIt-Debug-R<yy>` artifacts are laid out like an `Addins\<year>\` folder:

```text
SplashIt.addin
SplashIt\SplashIt.Revit.dll, SplashIt.UI.dll, SplashIt.Core.dll, PDBs, SplashIt.Revit.deps.json,
         Resources\icons\splash_16.png, splash_32.png (+ _dark variants)
```

Download them, or the screenshots, with:

```sh
gh run download <run-id> -n SplashIt-Debug-R26
gh run download <run-id> -n SplashIt-screenshots
```

## Dev deployment

Copy a `SplashIt-Debug-R<yy>` artifact's contents into
`%AppData%\Autodesk\Revit\Addins\<year>\`. Revit locks loaded DLLs, so close Revit
before redeploying. The add-in is unsigned, so Revit asks whether to load it on first
launch.

## Settings file

`%AppData%\SplashIt\settings.json` is written atomically (`AtomicFile`), and the file
is never left missing. Losing it would turn coloring back on for a user who had turned it off.

1. **Write a temp file.** The new text goes to a temp file in the same folder and is
   flushed to disk.
2. **Move it over the target.** `File.Move(temp, target, overwrite: true)` is one rename,
   and a rename never leaves the target missing.
3. **Fall back to `File.Replace` only if the move is refused.** Windows refuses that
   rename while any other handle has the target open, even one opened with
   `FileShare.Delete`. In that case only, and only when the target exists, the write
   uses `File.Replace` (Win32 `ReplaceFile`), which works over a reader that shares
   delete.
4. **Put the file back if `ReplaceFile` removed it.** `ReplaceFile` can fail with
   `ERROR_UNABLE_TO_MOVE_REPLACEMENT` (1176) after it has already removed the target.
   Whenever a `ReplaceFile` call fails and the target is gone, the temp file is moved
   into place at once.
5. **Retry, then give up.** The swap is tried up to 5 times over about 0.3 s, each
   failure logged, which covers a scanner or sync client that holds the file without
   `FileShare.Delete`. After that the error surfaces and the previous file is kept.
6. **Never delete the new contents while the target is gone.** If the target is missing
   and every attempt failed, the temp file is kept, and the log names it.

Anything that reads the file must open it with `FileShare.ReadWrite | FileShare.Delete`,
as `SplashItSettings.Read` does.

```json
{
  "schemaVersion": 1,
  "enabled": true,
  "projectColors": ["#4F8EF7", "..."],
  "familyColors": ["#D447BD", "..."],
  "projectFillOpacity": 100,
  "projectUnselectedOpacity": 50,
  "familyBarWidth": 3,
  "familyBarOpacity": 100
}
```

Opacities are whole percents (10–100) and the bar width whole pixels (1–6). A missing
or corrupt file reads as the defaults (enabled); a value out of range is clamped when
read from disk, and rejected by `Validate` everywhere else. An export has
`"format": "splash-it-settings"` first and never `enabled`.

`projectUnselectedOpacity` (decision #23) is a share of `projectFillOpacity`: a project's
unselected tabs show its color at fill × share, the selected tab at the full fill. It was
added within schema 1 (v1 had not shipped), so a settings file without it reads 50; an
import must carry it, like every other field.

A string that can't be decoded (an escaped lone surrogate such as `"\ud800"`, legal
JSON) counts as a bad value: dropped when reading the settings file, reported when
importing.

**Newer schema (known, accepted for v1):** a `settings.json` with a newer
`schemaVersion` is read for the fields this version knows and comes back as schema 1,
so the next Save rewrites it as schema 1 and drops the fields this version doesn't know.
A user who goes back to v1 after a future v2 loses the v2-only settings. Import, by
contrast, refuses a newer schema outright.

## The tab colorizer

`DocumentTabColorizer` (SplashIt.UI) colors document tabs through `ITabSurface`; in Revit
the surface is `RevitTabSurface`, which finds AvalonDock's `LayoutDocumentPaneControl` under
Revit's main window by type name and resolves each tab to its document by title
(`TabTitleMatcher` in Core): the tab's tool tip is `"<document> - <view>"`, matched against
the document's title, its saved file name, each with and without the Revit extension, and
its titles before a Save As; ordinal, `" - "`-separated, the longest wins, and a tie between
two documents leaves the tab uncolored. No reflection probe runs on Revit's internals (none
finds a document handle on 2026 or 2027).

Styles come from `TabStyleFactory` (decisions #22 and #23): the selected project tab shows the
color at the fill opacity with a 3 px darker top line; the project's other tabs show it at the
Unselected tab opacity (a share of the fill opacity, default 50 %). The fill is
`Background`/`Foreground` setters plus `IsSelected`/hover triggers. Its shape (#36, which
supersedes #34 and #30's end point; `ProjectFillShape.Default`): solid from the left edge to a fade
start 26 px before the close button's left edge (where #30's 24 px fade started, so the solid part
and where titles sit are unchanged), then a linear fade through the × to 50 % of the state's
opacity at the tab's right edge. It is never transparent, so a project's tabs, which Revit lays
flush, meet with no colorless gap; on Revit's tabs the fade is about 44 px. The selected tab's top
line takes the same shape. The `Background` value is a binding to the tab's width
(`ProjectFillConverter`), because the fade's start depends on where that tab's close button is:
the button's layout slot (Revit keeps it `Hidden`, not collapsed, on tabs that are not selected or
hovered, so hovering moves nothing), else the tab's right edge less 18 px. A tab too narrow for the
solid part fades from its left edge. Contrast: the text is black or white, whichever has the higher
*minimum* contrast over two points (#31), the solid part and the fade under a title's last column
(Revit leaves 12 px between a title and its ×, so a title that fills its tab ends 13.5 px into the
44 px fade, at about 85 % of the opacity); the × sits on the fade (#36), so it is black or white
per state (unselected, hover, selected) by the higher minimum contrast over the fill under each of
its 16 pixel columns. All of these are blended over the backdrop: whatever is behind the tab and
shows through the translucent fill, since the fill replaces the tab's own background (decision #33).
The colorizer finds it with `TabBackdrop`, in this order:

1. **Rendered sample.** What the window actually draws, sampled (one 1 px render) at a point that
   shows only the backdrop: the tabs' panel just past its last drawn tab (up to 4 px right of it).
   Since #36 no point on a filled tab shows the backdrop alone, so the sample never reads a tab: the
   point lies off every drawn tab (`Visibility` Visible; Revit leaves Hidden tabs at stale positions,
   and they are not drawn). A strip with under 2 px past its last tab (an overflowing one) gives no
   sample, and the painted search answers. It is taken only in a pass run from a layout pass (render
   data is current then), and not before the first layout after a theme change. A sample that
   disagrees with the painted search is taken only once a later layout pass samples the same color
   again, and never while it is the color settled before the last theme change (rc.3 F1: Revit 2027
   lays out before it repaints the strip, so the first sample after Dark → Light read the old
   `#454F61`); until then the pane uses the tree's answer and keeps waiting within its budget.
2. **Painted-layer search.** What is painted under the centre of the tabs' panel, from the nearest
   layer down (at each of the panel's ancestors, the siblings drawn below the tabs' branch, then the
   ancestor's own background), skipping every tab: the first border or panel background (or shape
   fill) covering that point that is an opaque `SolidColorBrush`, with translucent ones above it
   blended over it. It reads the tree rather than hit-testing it.
3. **Ancestor walk.** The tab's visual parents, to the first panel or border with an opaque solid
   background.
4. Else the tab's original background, then the theme surface.

On Revit 2026 and 2027 (VM) the rendered sample and the painted search agree with the pixels,
`#CCCCCC` light and `#454F61` dark; the ancestor walk alone gives `#EEEEEE` and `#2E3440`, and a hit
test finds nothing. Each pane (the tabs' parent) is worked out at the top of a pass, before any style
is assigned. Until a sample is taken it uses the tree's answer, uncached, and the next layout passes
look again, at most 20 times per pane; then the tree's answer is settled. Settled panes are kept in a
weak table (a pane Revit drops is not kept alive), found again on a theme change (the old color
kept as stale, see above) and dropped on Stop, and each is
logged once: "tabs sit on #RRGGBB (…) (sample N ms). [rendered: …; painted: …; layers met: …;
ancestors: …; chain: …]". The per-pass hot path is untouched. Over every default color, fill,
unselected share and state, the chosen black or white reaches 3:1 over the whole title (from the
solid part to its end in the fade) and over the fade under the ×, on each candidate strip
(`ProjectFillShapeTests`, which writes the worst cases to `project-fill-floor-<theme>.txt`). The
settings preview passes its tab color, which its template lays under the fill. The
family marks (#27: the
side bar of the bar width, plus a fixed 2 px top bar from the left edge over 95 % of the tab,
fading out over its last 15 % (#29), `FamilyTopBarShape.Default`) and the project's top line are drawn by an adorner
(`TabDecoration`), so they do not depend on Revit's template, follow a resize, and go with
the original style.
Revit's close button (`mDocumentCloseButton`) takes its grey from its own style
(`DynamicResource DockWindowButtonNormalForegroundBrush`, defined in UIFramework's
AvalonDock theme), so `TabCloseGlyph` sets it to black or white (since #36, per state, chosen over the fade under it and what
is behind the tab, see above) and puts the button's own value back when the original style
returns. All of this was checked in Revit
2026 and 2027 on the VM (`shared/evidence/lane-b/vm/`).

- Revit API calls happen only in Revit's own events (`RevitDocuments` caches titles and
  kinds there); the hot path runs from WPF layout passes and reads only WPF and the cache.
- Registration fallback (SC-004): when a pass finds tabs that resolve to no document, the
  colorizer asks for a document scan (at most once a second, at most 3 times per change of
  the tabs; the pass a scan forces is not a change). A layout pass is outside the API context, so `RevitColorizerEvents` only sets
  a flag; the next `Idling` (API context) lists `Application.Documents`, registers every
  open, non-linked document no event announced, re-reads the known ones (a changed kind is
  handled like a Save As) and, when anything changed (a title too), drops the surface's
  cached resolutions and restyles.
- Each document is logged once when registered:
  `documents: doc#<n> registered by <event|start|fallback>: <kind> (rule <api|template-ext>); <count> open.`
  The kind comes from `Document.IsFamilyDocument` (`api`); only `.rte` (projects) and
  `.rft` (families) demote to Other (`template-ext`). A scan that found or changed something,
  or whose unresolved count moved, logs
  `colorizer: a document scan registered <n> documents no event announced (…); <n> changed title or kind, <n> reclassified; <n> no longer open; <n> unresolved.`
- The log (`%LocalAppData%\SplashIt\logs\splashit-<yyyyMMdd>.log`) records counts, kinds
  and type names, never titles or paths. The start cost is logged as
  `colorizer: start took <n> ms`, split into the settings read, the pyRevit check, theme
  detection, the document listing, the subscriptions and the first pass.
- **Debug builds only:** create `%LocalAppData%\SplashIt\diag.on` (looked for once a second)
  and the surface writes `diag-<n>.txt` beside it: every tab's title, the members behind it,
  its visual tree with brushes, and where the close button's color comes from. That file
  holds titles, so it never exists in a Release build.
- Ribbon icons: `python3 tools/make_icons.py` (Pillow) regenerates
  `src/SplashIt.Revit/Resources/icons/`. The glyph is in `tools/glyph.py`.
