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
    Colors/              Rgb, ColorMath, ColorAllocator<TKey>
    Files/               FileKind, FileKindClassifier
    Settings/            SplashItSettings (Read/Write/Validate), SplashItDefaults,
                         SettingsExchange (export/import), the shared JSON reader
    Resources/           CoreStrings.resx (user-facing messages) + CoreStrings.cs
  SplashIt.Revit/        the add-in (net8.0-windows for 2025 and 2026,
                         net10.0-windows for 2027)
    App.cs               IExternalApplication (skeleton for now)
    SplashIt.addin       manifest (VendorId DSOS; the ClientId never changes)
tests/
  SplashIt.Core.Tests/   xUnit, net8.0;net10.0; runs on any OS
  SplashIt.RuntimeTests/ xUnit, net8.0-windows with WPF, no Revit; runs on Windows
                         only (CI). Renders WPF off-screen to PNG.
docs/                    this file
```

## Configurations

The MSBuild configuration picks the Revit year (`Directory.Build.props`):

| Configuration | Revit | Add-in target framework |
|---|---|---|
| `Debug R25`, `Release R25` | 2025 | `net8.0-windows` |
| `Debug R26`, `Release R26` | 2026 | `net8.0-windows` |
| `Debug R27`, `Release R27` | 2027 | `net10.0-windows` |

`SplashIt.Core` and its tests ignore the Revit suffix and build `net8.0` and `net10.0`
in every configuration; `SplashIt.RuntimeTests` is `net8.0-windows` in every
configuration. The Revit API comes from the reference-only `Nice3point.Revit.Api.*`
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
blank.

A runtime test that needs `SplashIt.Revit` types must mind the target frameworks: the
add-in is `net10.0-windows` in the R27 configurations, which a `net8.0-windows` test
project cannot reference.

## CI

`.github/workflows/build.yml` runs on every pull request and push to `main`, through the
reusable `_build.yml`. Every job is required:

| Job | Runner | What it does |
|---|---|---|
| Core tests | `ubuntu-latest` | `SplashIt.Core.Tests` on `net8.0` and `net10.0` |
| Build `<configuration>` × 6 | `ubuntu-latest` | `SplashIt.sln` in each configuration; the Debug legs upload `SplashIt-Debug-R25` / `-R26` / `-R27` |
| Runtime tests (Windows, WPF) | `windows-latest` | `SplashIt.RuntimeTests`; uploads the PNGs as `SplashIt-screenshots` |

The `SplashIt-Debug-R<yy>` artifacts are laid out like an `Addins\<year>\` folder:

```text
SplashIt.addin
SplashIt\SplashIt.Revit.dll, SplashIt.Core.dll, PDBs, SplashIt.Revit.deps.json
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

`%AppData%\SplashIt\settings.json`, written atomically (temp file, then replace):

```json
{
  "schemaVersion": 1,
  "enabled": true,
  "projectColors": ["#4F8EF7", "..."],
  "familyColors": ["#D447BD", "..."],
  "projectFillOpacity": 100,
  "familyBarWidth": 3,
  "familyBarOpacity": 100
}
```

Opacities are whole percents (10–100) and the bar width whole pixels (1–6). A missing
or corrupt file reads as the defaults (enabled); a value out of range is clamped when
read from disk, and rejected by `Validate` everywhere else. An export has
`"format": "splash-it-settings"` first and never `enabled`.
