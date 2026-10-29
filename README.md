# Splash-It

Splash-It is a Revit add-in that colors every open file's view tabs, so with several
models and many views open you can see at a glance which tab belongs to which file.
Projects (`.rvt`) get a color fill that fades through the close × to half strength at the tab's edge, so a project's tabs join with no gap; families
(`.rfa`, including one opened with Edit Family) get a colored L: a bar on the tab's left
edge and a thin line along its top.
Templates (`.rte`, `.rft`) stay native. Colors, opacity and bar width are set in a
small settings window, in light and dark, and can be exported and shared.

It is a sister app of [gantt-it](https://github.com/jedbjorn/gantt-it), by Designs/OS,
for Revit 2025, 2026 and 2027.

## Install

<!-- Adapted from gantt-it README.md, "Install" (gantt-it commit aa83f35). -->

1. Download `SplashIt-<version>.msi` from the [latest release](https://github.com/jedbjorn/splash-it/releases/latest).
2. Close Revit, then run the file.
3. If Windows says "Windows protected your PC", choose **More info**, then **Run anyway**. Windows asks this because Splash-It is not digitally signed yet. That is expected.
4. Accept the licence and click **Install**. The installer adds Splash-It to each Revit 2025, 2026 and 2027 it finds on your computer. It installs for you only and needs no administrator rights. If it finds none of them, it stops and says so.
5. Start Revit. Revit asks whether to load Splash-It, for the same reason: choose **Always Load**.

Your tabs are now colored by file, and the **Splash-It** button on the **Add-Ins** tab opens its settings.

**Upgrade:** close Revit, then run the newer installer. It replaces the old version. Revit asks again whether to load Splash-It on the next start: choose **Always Load** again.

**Installed another Revit year later?** Close Revit, run the installer again and choose **Repair**. It adds Splash-It to the Revit you just installed.

**Uninstall:** close Revit, then:

- Windows 11: **Settings › Apps › Installed apps**, find **Splash-It**, open its **⋯** menu and choose **Uninstall**.
- Windows 10: **Settings › Apps › Apps & features**, select **Splash-It** and choose **Uninstall**.

Uninstalling leaves two things behind: your settings in `%AppData%\SplashIt\settings.json` (your colors and choices; reinstalling picks them up again), and Splash-It's log files in `%LocalAppData%\SplashIt\logs`. To remove them as well, delete the `%AppData%\SplashIt` and `%LocalAppData%\SplashIt` folders.

**For IT:** close Revit, then run `msiexec /i SplashIt-<version>.msi /qn /norestart` in the user's own context. It installs for that user only, not when run as SYSTEM. It exits with 1603 when no Revit 2025, 2026 or 2027 is found, and can end with 3010 (restart needed) if Revit was open. To add a Revit year installed later, run `msiexec /i SplashIt-<version>.msi REINSTALL=ALL REINSTALLMODE=ecmus /qn /norestart`; installing the same MSI again does not. Details, including the exit codes for setup bundles that chain this installer, are in [docs/RELEASING.md](docs/RELEASING.md#silent-install-and-chaining).

## Group tabs by file

Revit adds every new view tab at the end of the strip, so after a while one file's tabs
are scattered among the others. Click **Group Tabs** on the **Add-Ins** tab (tooltip
"Group tabs by File") to put each file's tabs next to each other:

- Each file's tabs move to where that file's leftmost tab is now, so files keep their
  order and nothing moves further than it has to.
- A file's tabs keep their order among themselves.
- Every kind of file is grouped, colored or not. A tab Splash-It can't match to an open
  file goes after the grouped tabs.
- The selected tab and the active view stay the same.
- With tiled or split views, each group of tabs is sorted on its own; no tab moves to
  another tile. Views in floating windows are left alone.

It works the same with coloring on or off. It sorts once, when you click, and never on
its own, so you can still drag tabs anywhere afterwards. When the strip is already
grouped, nothing happens. To give it a keyboard shortcut, use Revit's **View › User
Interface › Keyboard Shortcuts** and search for "Group Tabs".

Group Tabs only changes the order of the tabs. It doesn't touch your model, and there is
nothing to undo. If a Revit update changes the tab strip in a way Splash-It doesn't
expect, the button says "Splash-It can't group tabs on this Revit build" and leaves the
tabs as they are.

## License

Splash-It is released under the [MIT License](LICENSE). Copyright (c) 2026 Designs/OS.

---

For developers: building and testing are in [docs/DEVELOPING.md](docs/DEVELOPING.md), releases in [docs/RELEASING.md](docs/RELEASING.md).
