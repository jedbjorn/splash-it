# Releasing Splash-It

<!-- Adapted from gantt-it docs/RELEASING.md (gantt-it commit aa83f35). -->

A release is a git tag. Pushing a tag `v<major>.<minor>.<patch>` (optionally
with a pre-release suffix, `v1.0.0-rc.1`) builds that commit and publishes a
GitHub Release with the installer attached. Nothing is bumped by hand: no
version lives in a file that has to be edited before a release.

## The v1 gate

As spec #1 (decision #20) sets it:

1. `v1.0.0-rc.1` is cut once every v1 lane has merged on CI evidence. It is
   the build the test VM checks.
2. `v1.0.0` is tagged after the VM part of AC10 (detection) and AC11 (the
   in-Revit checklist) pass on **Revit 2026 and 2027**. The VM has no
   Revit 2025.
3. Revit 2025 ships supported and is verified by the FnB from the public
   `v1.0.0` release, on another machine. A failure there is fixed in a patch
   release (`v1.0.1`).

## Cut a release

1. Make sure `main` is green and holds what should ship.
2. Tag the `main` commit and push the tag:

   ```sh
   git fetch origin
   git tag -a v1.0.0 origin/main -m "Splash-It 1.0.0"
   git push origin v1.0.0
   ```

3. Watch **Actions › release**. When it finishes, the release is at
   `https://github.com/jedbjorn/splash-it/releases/tag/v1.0.0`.

A tag with a hyphen (`v1.0.0-rc.1`, `v1.1.0-beta.2`) is published as a
**pre-release**; it does not become "latest". Tags that are not
`v<a>.<b>.<c>[-<suffix>]` fail the workflow before anything is built, and so
does a tag on a commit that is not on `main`.

## What the workflow does

`.github/workflows/release.yml`, on a pushed `v*` tag:

1. **Resolve version.** `v1.0.0-rc.1` → version `1.0.0-rc.1`. The MSI's
   ProductVersion is the numeric part, `1.0.0` (Windows Installer only accepts
   numbers; major and minor at most 255, patch at most 65535). Fails unless
   the tagged commit is reachable from `origin/main`.
2. **Build.** Runs the same reusable pipeline as every PR (`_build.yml`)
   against the tagged commit, with `-p:SplashItVersion=<version>`:
   - Core tests (net8.0 and net10.0), the six configuration builds, and the
     Windows runtime tests.
   - On Windows: `Release R25`, `Release R26` and `Release R27`, staged with
     `installer/stage.sh`, packed into one per-user MSI, and checked by
     `installer/inspect-msi.ps1`: UpgradeCode, per-user scope, file name and
     ProductVersion against the tag, the exact file set for each Revit year
     (against the staged folders too), folder cleanup on uninstall, the
     Revit detection wiring (per-year component conditions, AppSearch,
     launch condition), the upgrade rules, and each year's
     `SplashIt.Revit.dll` ProductVersion equal to the tag version.
   - Then, on a second Windows runner, `installer/test-detection.ps1` installs
     that MSI for real (`msiexec /qn`) against fake Revit installs and checks
     which `Addins\<year>\` folders it fills (see below).
3. **Release.** The read-only `assets` job collects and checksums the assets;
   then `publish`, the only job with write access, creates the GitHub Release
   for the tag with auto-generated notes and these assets:
   - `SplashIt-<version>.msi` — the installer (Revit 2025, 2026 and 2027;
     it installs for the years it finds).
   - `SplashIt-<version>-Revit2025.zip`, `SplashIt-<version>-Revit2026.zip`,
     `SplashIt-<version>-Revit2027.zip` — the contents of `Addins\<year>\`
     for a manual install.
   - `SHA256SUMS.txt`.

If any job fails, no release is created. Fix the problem on `main`, delete the
tag (`git push origin :refs/tags/v1.0.0` and `git tag -d v1.0.0`), and tag
again; or tag the next version instead.

## Where the version goes

`SplashItVersion` is the one MSBuild property that carries the tag
(`Directory.Build.props`). From it:

| Consumer | Release build `v1.0.0-rc.1` | Push/PR or local build |
|---|---|---|
| Assembly `InformationalVersion` (the DLL's Product version) | `1.0.0-rc.1` | `0.0.0-dev+<commit>` |
| `AssemblyVersion` / `FileVersion` | `1.0.0.0` | `0.0.0.0` |
| MSI ProductVersion | `1.0.0` | `0.0.0` |
| MSI file name | `SplashIt-1.0.0-rc.1.msi` | `SplashIt-0.0.0-dev.msi` |

The MSI's UpgradeCode (`080648F4-EEC7-4DB6-A793-683A1C008BFC`, in
`installer/Product.wxs`) is fixed forever: it is how Windows recognises a new
MSI as an upgrade of the installed one, and rst-c's chained install depends on
it. The add-in's ClientId (`src/SplashIt.Revit/SplashIt.addin`) is fixed
forever too. A newer MSI replaces the installed version in place (one entry in
Settings › Apps); an older one refuses to install over a newer one. An MSI with
the *same* ProductVersion also replaces it, so `1.0.0` installs cleanly over
`1.0.0-rc.1`.

The flip side: an rc and its final share one ProductVersion (`1.0.0-rc.1`
and `1.0.0` are both `1.0.0` to Windows), so **an rc MSI silently replaces an
installed final of the same a.b.c**, with no downgrade warning, and Settings ›
Apps shows the same version, `1.0.0`, for either. To tell which build is
installed, check the Product version of
`%AppData%\Autodesk\Revit\Addins\<year>\SplashIt\SplashIt.Revit.dll` (file
Properties › Details). Don't hand rc MSIs to users once the final is out.

## Installer behaviour

- **Per-user**, under `%AppData%\Autodesk\Revit\Addins\<year>\` (`SplashIt.addin`
  and the `SplashIt\` folder): no admin prompt, one **Splash-It** entry in
  Settings › Apps.
- **Revit detection** (standard AppSearch, no custom actions), per year: the
  `InstallLocation` of `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7346B4A0-<yy>00-0510-0000-705C0D862004}`
  (64-bit view) when it holds `Revit.exe`, and
  `C:\Program Files\Autodesk\Revit <year>\Revit.exe`; either one counts. A
  leftover entry with no `Revit.exe` does not count.
- **No supported Revit**: a first install stops with "Splash-It needs Revit
  2025, 2026 or 2027, and none was found on this computer." (exit code 1603
  when silent). Repair and uninstall are never blocked.
- **Repair** re-runs the detection: it adds a Revit year installed since and
  drops one uninstalled since. The setup UI's Repair button sets
  `REINSTALL=ALL REINSTALLMODE=ecmus`; silently:
  `msiexec /i SplashIt-<version>.msi REINSTALL=ALL REINSTALLMODE=ecmus /qn /norestart`.
  Running the same MSI again with a plain `/i … /qn` changes nothing and does
  not detect again. A newer MSI (an upgrade) detects again by itself.
- **Silent install**: see [Silent install and chaining](#silent-install-and-chaining).
- **Uninstall** removes `SplashIt.addin` and the `SplashIt\` folder of every
  year. It never removes an `Addins\<year>\` that existed before or that holds
  anything else; Windows Installer removes one only when it created it itself
  and it is empty again, which leaves the folders as they were. It keeps
  `%AppData%\SplashIt\settings.json` and the logs in
  `%LocalAppData%\SplashIt\logs\`, which the MSI does not own.

## Silent install and chaining

```bat
msiexec /i SplashIt-<version>.msi /qn /norestart /l*v splashit-install.log
msiexec /x SplashIt-<version>.msi /qn /norestart
```

- **Close Revit first.** While Revit runs, the Splash-It DLLs it has loaded
  are locked. An upgrade, repair or uninstall then finishes with exit code
  **3010** (the locked files are replaced at the next restart), or Windows may
  try to close Revit. `/norestart` makes sure Windows never restarts the
  computer by itself; it is recommended for every silent run.
- **Run it as the user.** The install is per-user: it lands in the profile of
  whoever runs `msiexec`. Run as SYSTEM (for example Intune or SCCM in device
  context) it installs into SYSTEM's profile, where Revit never looks. Deploy
  it in the user's context.
- **No supported Revit** (2025, 2026 or 2027): the install exits **1603** by
  design. That is the launch condition, and nothing is installed.
- **A Revit year installed later**: a second `/i … /qn` of the same MSI does
  not pick it up. Run the repair command above, or install a newer release.

### For bundles that chain this MSI

rst-c's setup (a WiX Burn bundle) chains this MSI unmodified (shared
handoff, item 2). The exit codes a bundle sees:

| Exit code | Meaning | Suggested handling |
|---|---|---|
| 0 | Installed (or uninstalled) | success |
| 1603 | No supported Revit found: the launch condition stopped the install, nothing was changed. Also any other fatal install error | mark the package non-vital, or show its checkbox only when a Revit 2025-2027 is detected (the same `InstallLocation` + `Revit.exe` checks as above), so it cannot roll back the whole bundle |
| 3010 | Installed; a restart is needed because Revit had the old DLLs locked | success, restart pending; ask users to close Revit before running setup |
| 1618 | Another installation is in progress | retry later |

The UpgradeCode never changes, so a bundle can detect an installed Splash-It
by it (`080648F4-EEC7-4DB6-A793-683A1C008BFC`).

## The detection test (AC10, CI part)

The `detect` job of `_build.yml` runs `installer/test-detection.ps1` on a
Windows runner (an administrator) against the MSI the `msi` job built. It
writes fake Revit uninstall entries (64-bit HKLM) pointing at temp folders,
with or without a dummy `Revit.exe`, and a dummy `Revit.exe` in the default
folder, then installs, repairs and uninstalls with `msiexec /qn`:

| Case | Fake Revit | Action | Expected |
|---|---|---|---|
| 1 | none | install | blocked by the launch condition (exit 1603), nothing installed |
| 2 | 2026 in a custom folder (registry) | install | only 2026 |
| 3 | a 2025 entry without `Revit.exe`; 2027 in the default folder only | install | only 2027 |
| 4 | 2025 and 2026 (registry), 2027 (default folder) | install | all three |
| 5 | 2026, then 2025 added | install, then repair | 2025 and 2026 |
| 5b | 2026 removed | repair | only 2025 |
| 6 | 2025 removed too (none left); `Addins\2025\` existed empty and `Addins\2026\` held another add-in before case 5 | uninstall | not blocked; no Splash-It files; both `Addins\<year>\` folders, the other add-in's file and `settings.json` kept |

It uploads `results.md`, the full transcript and every msiexec verbose log as
the `SplashIt-detection` artifact:

```sh
gh run download <run-id> -n SplashIt-detection
```

Do not run the script on a machine with Revit, or one you care about: it
writes fake Revit entries and deletes `Addins\2025\`, `Addins\2026\` and
`%AppData%\SplashIt\` of the user running it. Outside GitHub Actions it
refuses to start unless given `-IUnderstandThisDeletesUserFolders`.

## Rehearse without publishing

**Actions › release › Run workflow** asks for a version (for example
`1.0.0-rc.1`) and runs the resolve, build and assets jobs exactly as a tag
would, then skips `publish`: the assets job prints the release it *would*
create, nothing is published, and no job gets write access. Run it from
`main`; on another branch the "on main" check only warns. Its artifacts
(`SplashIt-msi`, `SplashIt-zips`) carry the chosen version, which also makes it
the way to get test MSIs with distinct versions:

```sh
gh workflow run release.yml -f version=1.0.0-rc.1
gh run download <run-id> -n SplashIt-msi
```

Every PR and push to `main` also uploads a `SplashIt-msi` artifact, with the
dev version `0.0.0-dev`.

## Verify a release

1. The release page lists the MSI, the three zips and `SHA256SUMS.txt`, and is
   marked pre-release exactly when the tag has a suffix.
2. The **Inspect MSI** step of the release run's MSI job passed and printed
   `ProductVersion` equal to the tag's numeric part, the `Addins\2025\`,
   `Addins\2026\` and `Addins\2027\` file lists, the Revit detection, and
   each year's `SplashIt.Revit.dll` ProductVersion equal to the tag version;
   and the **MSI detection** job passed every case.
3. On a Windows machine with Revit: install the MSI (no admin prompt), confirm
   Settings › Apps shows **Splash-It** with the new version and that only the
   `Addins\<year>\SplashIt\` folders of the installed Revit years were
   created, start one of them, choose **Always Load**, and check that the
   tabs are colored and the **Splash-It** button opens the settings.

## Build the MSI locally (Windows)

Needs the .NET 8 and .NET 10 SDKs; WiX comes from NuGet. From the repo root,
in Git Bash:

```sh
for r in R25 R26 R27; do
  dotnet build src/SplashIt.Revit/SplashIt.Revit.csproj -c "Release $r" -p:SplashItVersion=1.0.0
done
for year in 2025 2026 2027; do bash installer/stage.sh $year; done
dotnet build installer/SplashIt.Installer.wixproj -c Release -p:SplashItVersion=1.0.0
powershell -File installer/inspect-msi.ps1 -Msi installer/bin/Release/SplashIt-1.0.0.msi -Version 1.0.0 -Stage installer/stage
```

Omit `-p:SplashItVersion` for a dev build (`SplashIt-0.0.0-dev.msi`). The setup
artwork and the license page in `installer/branding/` are generated by
`tools/make_installer_branding.py` (the license page from `LICENSE`).

The add-in and the MSI are unsigned in v1: Revit shows its unsigned add-in
prompt and SmartScreen may warn on the MSI.
