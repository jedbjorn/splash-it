using System.IO;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.RuntimeTests.Support;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Resources;
using SplashIt.UI.Settings;

namespace SplashIt.RuntimeTests.Settings;

/// <summary>
/// AC6: the settings view-model against a fake host and fake dialogs, writing to a temp
/// settings file. Each test runs on its own STA thread, as the window does in Revit.
/// </summary>
public class SettingsViewModelTests
{
    private static readonly Rgb Red = Rgb.Parse("#FF0000");
    private static readonly Rgb Green = Rgb.Parse("#00AA00");
    private static readonly Rgb Blue = Rgb.Parse("#0000FF");

    private sealed class Rig : IDisposable
    {
        public Rig(SplashItSettings? stored = null, bool writeFile = true)
        {
            Folder = new TempFolder();
            Path = Folder.File("settings.json");
            Stored = stored ?? SplashItSettings.Default;
            if (writeFile) Stored.Write(Path);
            Host.SettingsPath = Path;
            Model = new SettingsViewModel(SplashItSettings.Read(Path), Path, Host, Dialogs);
        }

        public TempFolder Folder { get; }

        public string Path { get; }

        public SplashItSettings Stored { get; }

        public FakeHost Host { get; } = new();

        public FakeDialogs Dialogs { get; } = new();

        public SettingsViewModel Model { get; }

        public byte[]? FileBytes() => File.Exists(Path) ? File.ReadAllBytes(Path) : null;

        public void Dispose() => Folder.Dispose();
    }

    private static void Sta(Action<Rig> test, SplashItSettings? stored = null, bool writeFile = true) =>
        Screenshots.RunSta(() =>
        {
            using var rig = new Rig(stored, writeFile);
            test(rig);
        });

    // ---- Save --------------------------------------------------------------------------------

    [Fact]
    public void Save_Invalid_WritesNothing_ListsEveryError_AndDoesNotApply() => Sta(rig =>
    {
        var before = rig.FileBytes();
        var stamp = File.GetLastWriteTimeUtc(rig.Path);
        rig.Model.ProjectFillOpacity = 5;
        rig.Model.FamilyBarWidth = 9;
        rig.Model.FamilyBarOpacity = 101;

        rig.Model.SaveCommand.Execute(null);

        Assert.Equal(before, rig.FileBytes());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(rig.Path));
        Assert.Empty(rig.Host.Applied);
        Assert.Equal(UiStrings.Settings_Invalid, rig.Model.ErrorHeading);
        Assert.Equal(rig.Model.Current.Validate(), rig.Model.Errors);
        Assert.Equal(3, rig.Model.Errors.Count);
        Assert.True(rig.Model.HasErrors);
        Assert.True(rig.Model.IsDirty);
        Assert.Null(rig.Model.Notice);
    });

    [Fact]
    public void Save_Invalid_WithNoFile_CreatesNone() => Sta(rig =>
    {
        rig.Model.FamilyBarWidth = 0;
        Assert.False(rig.Model.Save());
        Assert.False(File.Exists(rig.Path));
        Assert.Empty(Directory.GetFiles(rig.Folder.Path));
        Assert.Empty(rig.Host.Applied);
    }, writeFile: false);

    [Fact]
    public void Save_Valid_WritesTheFile_ThenAppliesExactlyOnce() => Sta(rig =>
    {
        rig.Model.ProjectFillOpacity = 40;
        rig.Model.FamilyBarWidth = 6;
        rig.Model.ProjectColors.Load([Red, Green]);
        var expected = rig.Model.Current;

        rig.Model.SaveCommand.Execute(null);

        Assert.Equal(expected, SplashItSettings.Read(rig.Path));
        Assert.Equal(expected.ToJson(), File.ReadAllText(rig.Path));
        var applied = Assert.Single(rig.Host.Applied);
        Assert.Equal(expected, applied);
        // The file already held the new settings when Apply ran: write first, then apply.
        Assert.Equal(expected.ToJson(), Assert.Single(rig.Host.FileAtApply));
        Assert.False(rig.Model.IsDirty);
        Assert.Equal(UiStrings.Settings_Saved, rig.Model.Notice);
        Assert.False(rig.Model.HasErrors);
        Assert.DoesNotContain(Directory.GetFiles(rig.Folder.Path), f => !f.EndsWith("settings.json", StringComparison.Ordinal));
    });

    [Fact]
    public void Save_Twice_AppliesOncePerSave() => Sta(rig =>
    {
        rig.Model.ProjectFillOpacity = 70;
        rig.Model.SaveCommand.Execute(null);
        rig.Model.FamilyBarOpacity = 30;
        rig.Model.SaveCommand.Execute(null);
        Assert.Equal(2, rig.Host.Applied.Count);
        Assert.Equal(30, rig.Host.Applied[1].FamilyBarOpacity);
    });

    [Fact]
    public void Save_Disable_WritesOff_AndRereadsTheStatus() => Sta(rig =>
    {
        rig.Host.StatusAfterApply = s => new SettingsStatus(s.Enabled ? SettingsStatusKind.Active : SettingsStatusKind.Off);
        Assert.Equal(SettingsStatusKind.Active, rig.Model.StatusKind);
        rig.Model.Enabled = false;

        rig.Model.SaveCommand.Execute(null);

        Assert.False(SplashItSettings.Read(rig.Path).Enabled);
        Assert.False(Assert.Single(rig.Host.Applied).Enabled);
        Assert.Equal(SettingsStatusKind.Off, rig.Model.StatusKind);
        Assert.Equal(UiStrings.Status_Off, rig.Model.StatusText);
        Assert.False(rig.Model.CanResetAssignments);
    });

    [Fact]
    public void Save_WhenTheFileCannotBeWritten_ShowsTheError_AndDoesNotApply() => Screenshots.RunSta(() =>
    {
        using var folder = new TempFolder();
        // The settings "folder" is a file, so the write fails.
        var blocker = folder.File("not-a-folder");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, "settings.json");
        var host = new FakeHost();
        var model = new SettingsViewModel(SplashItSettings.Default, path, host, new FakeDialogs());
        model.ProjectFillOpacity = 50;

        Assert.False(model.Save());

        Assert.Empty(host.Applied);
        Assert.StartsWith(UiStrings.Settings_SaveFailed("").TrimEnd(), model.ErrorHeading);
        Assert.True(model.IsDirty);
    });

    [Fact]
    public void Save_WhenApplyFails_KeepsTheFile_AndShowsTheError() => Sta(rig =>
    {
        rig.Host.ApplyThrows = new InvalidOperationException("tree changed");
        rig.Model.ProjectFillOpacity = 60;

        Assert.True(rig.Model.Save());

        Assert.Equal(60, SplashItSettings.Read(rig.Path).ProjectFillOpacity);
        Assert.Single(rig.Host.Applied);
        Assert.Equal(UiStrings.Settings_ApplyFailed("tree changed"), rig.Model.ErrorHeading);
        Assert.False(rig.Model.IsDirty);
    });

    // ---- Import and export --------------------------------------------------------------------

    [Fact]
    public void Import_LoadsUnsaved_KeepsOnOff_AndSaysSaveToApply() => Sta(rig =>
    {
        var team = rig.Folder.File("team.json");
        var exported = new SplashItSettings
        {
            Enabled = true,
            ProjectColors = [Red, Green, Blue],
            FamilyColors = [Blue],
            ProjectFillOpacity = 35,
            FamilyBarWidth = 5,
            FamilyBarOpacity = 45,
        };
        SettingsExchange.ExportFile(exported, team);
        var before = rig.FileBytes();
        rig.Dialogs.OpenResult = team;

        rig.Model.ImportCommand.Execute(null);

        Assert.Equal(exported with { Enabled = false }, rig.Model.Current);
        Assert.False(rig.Model.Enabled); // the stored settings are off; import never switches it
        Assert.Equal(UiStrings.Settings_Imported("team.json"), rig.Model.Notice);
        Assert.Equal("Imported from team.json; Save to apply.", rig.Model.Notice);
        Assert.True(rig.Model.IsDirty);
        Assert.Equal(before, rig.FileBytes());
        Assert.Empty(rig.Host.Applied);
        Assert.Equal([Red, Green, Blue], rig.Model.ProjectColors.Colors);
        Assert.Equal(3, rig.Model.ProjectColors.Chips.Count);
    }, stored: SplashItSettings.Default with { Enabled = false });

    [Fact]
    public void Import_Bad_ListsEveryError_AndChangesNothing() => Sta(rig =>
    {
        var bad = rig.Folder.File("broken.json");
        File.WriteAllText(bad, """
            {
              "format": "splash-it-settings",
              "schemaVersion": 1,
              "projectColors": ["#4F8EF7", "#F28E2B", "#59A14F", "#E15759", "#B07AA1", "#76B7B2", "#EDC948",
                                "#FF9DA7", "#9C755F", "#BAB0AC", "#8CD17D", "#499894", "#123456"],
              "familyColors": ["#D447BD", "blue"],
              "projectFillOpacity": 0,
              "familyBarWidth": 9,
              "familyBarOpacity": 100
            }
            """);
        var expectedErrors = SettingsExchange.ImportFile(bad, SplashItSettings.Default).Errors;
        Assert.True(expectedErrors.Count >= 4, string.Join(" | ", expectedErrors));
        var before = rig.Model.Current;
        rig.Dialogs.OpenResult = bad;

        rig.Model.ImportCommand.Execute(null);

        Assert.Equal(UiStrings.Settings_ImportFailed("broken.json"), rig.Model.ErrorHeading);
        Assert.Equal(expectedErrors, rig.Model.Errors);
        Assert.Equal(before, rig.Model.Current);
        Assert.False(rig.Model.IsDirty);
        Assert.Null(rig.Model.Notice);
        Assert.Empty(rig.Host.Applied);
    });

    [Fact]
    public void Import_NewerSchema_IsRefusedWithItsMessage() => Sta(rig =>
    {
        var newer = rig.Folder.File("newer.json");
        File.WriteAllText(newer, """{ "format": "splash-it-settings", "schemaVersion": 2 }""");
        rig.Dialogs.OpenResult = newer;
        rig.Model.ImportCommand.Execute(null);
        Assert.Contains(rig.Model.Errors, e => e.Contains("newer version", StringComparison.Ordinal));
        Assert.False(rig.Model.IsDirty);
    });

    [Fact]
    public void Import_Cancelled_ChangesNothing() => Sta(rig =>
    {
        rig.Dialogs.OpenResult = null;
        rig.Model.ImportCommand.Execute(null);
        Assert.Equal(1, rig.Dialogs.OpenCalls);
        Assert.False(rig.Model.IsDirty);
        Assert.False(rig.Model.HasErrors);
        Assert.Null(rig.Model.Notice);
    });

    [Fact]
    public void Export_WritesTheWindowsValues_WithoutEnabled() => Sta(rig =>
    {
        var target = rig.Folder.File("out.json");
        rig.Dialogs.SaveResult = target;
        rig.Model.ProjectFillOpacity = 55;

        rig.Model.ExportCommand.Execute(null);

        var text = File.ReadAllText(target);
        Assert.DoesNotContain("enabled", text, StringComparison.OrdinalIgnoreCase);
        var back = SettingsExchange.ImportFile(target, SplashItSettings.Default);
        Assert.True(back.Succeeded);
        Assert.Equal(55, back.Settings!.ProjectFillOpacity);
        Assert.Equal(UiStrings.Settings_Exported("out.json"), rig.Model.Notice);
        Assert.Empty(rig.Host.Applied);
    });

    [Fact]
    public void Export_Invalid_IsRefused_BeforeAskingForAFile() => Sta(rig =>
    {
        rig.Model.FamilyBarOpacity = 0;
        rig.Model.ExportCommand.Execute(null);
        Assert.Equal(0, rig.Dialogs.SaveCalls);
        Assert.Equal(UiStrings.Settings_ExportInvalid, rig.Model.ErrorHeading);
        Assert.Single(rig.Model.Errors);
    });

    // ---- Close --------------------------------------------------------------------------------

    [Fact]
    public void Close_WithChanges_AsksThroughTheDialogService_AndStaysOpenWhenDeclined() => Sta(rig =>
    {
        var closed = 0;
        rig.Model.CloseRequested += () => closed++;
        rig.Model.ProjectFillOpacity = 20;
        rig.Dialogs.ConfirmResult = false;

        rig.Model.CloseCommand.Execute(null);

        var ask = Assert.Single(rig.Dialogs.ConfirmCalls);
        Assert.Equal(UiStrings.Settings_DiscardTitle, ask.Title);
        Assert.Equal(UiStrings.Settings_DiscardMessage, ask.Message);
        Assert.Equal(UiStrings.Settings_Discard, ask.ConfirmText);
        Assert.True(ask.Danger);
        Assert.Equal(MessageDialog.DiscardChangesId, ask.AutomationId);
        Assert.Equal(0, closed);
        Assert.True(rig.Model.IsDirty);

        // The title bar's X goes through the same question; still declined, still open.
        Assert.False(rig.Model.ConfirmClose());
        Assert.Equal(2, rig.Dialogs.ConfirmCalls.Count);
        Assert.Empty(rig.Host.Applied);
    });

    [Fact]
    public void Close_WithChanges_ClosesWhenDiscarded_AndAsksOnlyOnce() => Sta(rig =>
    {
        var closed = 0;
        rig.Model.CloseRequested += () => closed++;
        rig.Model.Enabled = false;
        rig.Dialogs.ConfirmResult = true;

        rig.Model.CloseCommand.Execute(null);
        // The window's Closing handler asks the model again: no second prompt.
        Assert.True(rig.Model.ConfirmClose());

        Assert.Equal(1, closed);
        Assert.Single(rig.Dialogs.ConfirmCalls);
        Assert.Empty(rig.Host.Applied);
        Assert.Equal(rig.Stored, SplashItSettings.Read(rig.Path));
    });

    [Fact]
    public void Close_WithoutChanges_DoesNotAsk() => Sta(rig =>
    {
        var closed = 0;
        rig.Model.CloseRequested += () => closed++;
        rig.Model.CloseCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.Empty(rig.Dialogs.ConfirmCalls);
    });

    [Fact]
    public void Close_AfterSave_DoesNotAsk() => Sta(rig =>
    {
        rig.Model.ProjectFillOpacity = 80;
        rig.Model.Save();
        Assert.True(rig.Model.ConfirmClose());
        Assert.Empty(rig.Dialogs.ConfirmCalls);
    });

    [Fact]
    public void Close_ChangedBackToTheSavedValues_DoesNotAsk() => Sta(rig =>
    {
        rig.Model.ProjectFillOpacity = 80;
        rig.Model.ProjectFillOpacity = rig.Stored.ProjectFillOpacity;
        Assert.False(rig.Model.IsDirty);
        Assert.True(rig.Model.ConfirmClose());
        Assert.Empty(rig.Dialogs.ConfirmCalls);
    });

    // ---- Color lists ----------------------------------------------------------------------------

    [Fact]
    public void Add_IsDisabledAtTwelve() => Sta(rig =>
    {
        var list = rig.Model.ProjectColors;
        Assert.Equal(12, list.Count);
        Assert.False(list.CanAdd);
        Assert.False(list.AddCommand.CanExecute(null));
        list.Add();
        Assert.Equal(12, list.Count);

        list.Remove();
        Assert.Equal(11, list.Count);
        Assert.True(list.AddCommand.CanExecute(null));
        list.AddCommand.Execute(null);
        Assert.Equal(12, list.Count);
        Assert.False(list.AddCommand.CanExecute(null));
    });

    [Fact]
    public void Remove_IsDisabledAtOne() => Sta(rig =>
    {
        var list = rig.Model.FamilyColors;
        list.Load([Red, Green]);
        Assert.True(list.RemoveCommand.CanExecute(null));
        list.RemoveCommand.Execute(null);
        Assert.Equal(1, list.Count);
        Assert.False(list.CanRemove);
        Assert.False(list.RemoveCommand.CanExecute(null));
        list.Remove();
        Assert.Equal([Green], list.Colors);
    });

    [Fact]
    public void Add_TakesTheFirstDefaultNotInTheList_AndSelectsIt() => Sta(rig =>
    {
        var list = rig.Model.ProjectColors;
        list.Load([SplashItDefaults.ProjectColors[0], SplashItDefaults.ProjectColors[2]]);
        list.Add();
        Assert.Equal([SplashItDefaults.ProjectColors[0], SplashItDefaults.ProjectColors[2], SplashItDefaults.ProjectColors[1]], list.Colors);
        Assert.Same(list.Chips[2], list.Selected);
        Assert.Equal(3, list.Selected!.Number);
        Assert.Equal("Project color 3", list.Selected.Name);
        Assert.True(rig.Model.IsDirty);
    });

    [Fact]
    public void Remove_TakesTheSelectedChip_AndRenumbers() => Sta(rig =>
    {
        var list = rig.Model.FamilyColors;
        list.Load([Red, Green, Blue]);
        list.Selected = list.Chips[1];
        list.Remove();
        Assert.Equal([Red, Blue], list.Colors);
        Assert.Equal([1, 2], list.Chips.Select(c => c.Number));
        Assert.Equal(Blue, list.Selected!.Color);
        Assert.Equal("Family color 2", list.Selected.Name);
    });

    [Fact]
    public void Move_LeftAndRight_Reorders_AndStopsAtTheEnds() => Sta(rig =>
    {
        var list = rig.Model.ProjectColors;
        list.Load([Red, Green, Blue]);
        Assert.Same(list.Chips[0], list.Selected);
        Assert.False(list.MoveLeftCommand.CanExecute(null));
        Assert.True(list.MoveRightCommand.CanExecute(null));

        list.MoveRightCommand.Execute(null);
        Assert.Equal([Green, Red, Blue], list.Colors);
        list.MoveRightCommand.Execute(null);
        Assert.Equal([Green, Blue, Red], list.Colors);
        Assert.False(list.MoveRightCommand.CanExecute(null));
        Assert.Equal(3, list.Selected!.Number);

        list.MoveLeftCommand.Execute(null);
        Assert.Equal([Green, Red, Blue], list.Colors);
        Assert.Equal([1, 2, 3], list.Chips.Select(c => c.Number));
    });

    [Fact]
    public void ClickingAChip_EditsItInThePicker_WithItsListsPalette() => Sta(rig =>
    {
        var list = rig.Model.FamilyColors;
        var chip = list.Chips[2];
        rig.Dialogs.PickResults.Enqueue(Red);

        list.EditCommand.Execute(chip);

        var call = Assert.Single(rig.Dialogs.PickCalls);
        Assert.Equal(SplashItDefaults.FamilyColors[2], call.Current);
        Assert.Equal(SplashItDefaults.FamilyColors, call.Palette);
        Assert.Equal("Family color 3", call.Title);
        Assert.Equal(Red, chip.Color);
        Assert.Same(chip, list.Selected);
        Assert.True(rig.Model.IsDirty);
        Assert.Equal(Red, rig.Model.Current.FamilyColors[2]);
    });

    [Fact]
    public void ACancelledPicker_ChangesNothing_ButSelectsTheChip() => Sta(rig =>
    {
        var list = rig.Model.ProjectColors;
        list.EditCommand.Execute(list.Chips[4]);
        Assert.Single(rig.Dialogs.PickCalls);
        Assert.Equal(SplashItDefaults.ProjectColors, list.Colors);
        Assert.Same(list.Chips[4], list.Selected);
        Assert.False(rig.Model.IsDirty);
    });

    [Fact]
    public void ResetToDefaults_RestoresTheListsDefaultColors() => Sta(rig =>
    {
        Assert.Equal(new[] { Red }, rig.Model.ProjectColors.Colors);
        Assert.Equal(new[] { Blue, Green }, rig.Model.FamilyColors.Colors);

        rig.Model.ProjectColors.ResetCommand.Execute(null);
        rig.Model.FamilyColors.ResetCommand.Execute(null);

        Assert.Equal(SplashItDefaults.ProjectColors, rig.Model.ProjectColors.Colors);
        Assert.Equal(SplashItDefaults.FamilyColors, rig.Model.FamilyColors.Colors);
        Assert.True(rig.Model.IsDirty);
        Assert.Empty(rig.Host.Applied);
        rig.Model.Save();
        Assert.Equal(SplashItDefaults.ProjectColors, SplashItSettings.Read(rig.Path).ProjectColors);
    }, stored: SplashItSettings.Default with { ProjectColors = [Red], FamilyColors = [Blue, Green] });

    // ---- Bounds ---------------------------------------------------------------------------------

    [Fact]
    public void SliderBounds_AreTheSpecsRanges()
    {
        Assert.Equal(10, SettingsViewModel.MinOpacity);
        Assert.Equal(100, SettingsViewModel.MaxOpacity);
        Assert.Equal(1, SettingsViewModel.MinBarWidth);
        Assert.Equal(6, SettingsViewModel.MaxBarWidth);
    }

    [Theory]
    [InlineData(10, 1, 10, true)]
    [InlineData(100, 6, 100, true)]
    [InlineData(55, 3, 30, true)]
    [InlineData(9, 3, 100, false)]
    [InlineData(101, 3, 100, false)]
    [InlineData(100, 0, 100, false)]
    [InlineData(100, 7, 100, false)]
    [InlineData(100, 3, 9, false)]
    [InlineData(100, 3, 101, false)]
    public void Save_HoldsTheOpacityAndWidthBounds(int fill, int width, int bar, bool saves) => Sta(rig =>
    {
        var before = rig.FileBytes();
        rig.Model.ProjectFillOpacity = fill;
        rig.Model.FamilyBarWidth = width;
        rig.Model.FamilyBarOpacity = bar;

        Assert.Equal(saves, rig.Model.Save());

        if (saves)
        {
            var read = SplashItSettings.Read(rig.Path);
            Assert.Equal((fill, width, bar), (read.ProjectFillOpacity, read.FamilyBarWidth, read.FamilyBarOpacity));
            Assert.Single(rig.Host.Applied);
        }
        else
        {
            Assert.Equal(before, rig.FileBytes());
            Assert.Empty(rig.Host.Applied);
            Assert.Single(rig.Model.Errors);
        }
    });

    [Fact]
    public void ValueTexts_ShowPercentAndPixels() => Sta(rig =>
    {
        rig.Model.ProjectFillOpacity = 40;
        rig.Model.FamilyBarWidth = 6;
        rig.Model.FamilyBarOpacity = 30;
        Assert.Equal("40%", rig.Model.ProjectFillOpacityText);
        Assert.Equal("6 px", rig.Model.FamilyBarWidthText);
        Assert.Equal("30%", rig.Model.FamilyBarOpacityText);
    });

    // ---- Status line and Reset assignments ---------------------------------------------------------

    [Theory]
    [InlineData(SettingsStatusKind.Active, null, "Active: open tabs are colored by file.")]
    [InlineData(SettingsStatusKind.Off, null, "Off: tabs keep Revit's own look.")]
    [InlineData(SettingsStatusKind.Unavailable, "no document tabs found", "Unavailable on this Revit build: no document tabs found")]
    [InlineData(SettingsStatusKind.BlockedByPyRevit, null, "Blocked by pyRevit: pyRevit's tab coloring is running, so Splash-It stays off. Turn off pyRevit's tab coloring and restart Revit.")]
    [InlineData(SettingsStatusKind.Faulted, null, "Stopped after an error: the tabs were restored. Restart Revit to try again.")]
    public void StatusLine_ShowsEveryState(SettingsStatusKind kind, string? reason, string expected) => Screenshots.RunSta(() =>
    {
        var host = new FakeHost { Status = new SettingsStatus(kind, reason) };
        var model = new SettingsViewModel(SplashItSettings.Default, Path.Combine(Path.GetTempPath(), "unused.json"), host, new FakeDialogs());
        Assert.Equal(kind, model.StatusKind);
        Assert.Equal(expected, model.StatusText);
        Assert.Equal(kind == SettingsStatusKind.Active, model.ResetAssignmentsCommand.CanExecute(null));
    });

    [Fact]
    public void StatusLine_Unavailable_WithoutAReason_StillExplains() => Screenshots.RunSta(() =>
    {
        var host = new FakeHost { Status = new SettingsStatus(SettingsStatusKind.Unavailable) };
        var model = new SettingsViewModel(SplashItSettings.Default, Path.Combine(Path.GetTempPath(), "unused.json"), host, new FakeDialogs());
        Assert.Equal(UiStrings.Status_UnavailableNoReason, model.StatusText);
    });

    [Fact]
    public void ResetAssignments_CallsTheHostOnce_AndSavesNothing() => Sta(rig =>
    {
        var before = rig.FileBytes();
        rig.Model.ResetAssignmentsCommand.Execute(null);
        Assert.Equal(1, rig.Host.ResetAssignmentsCount);
        Assert.Empty(rig.Host.Applied);
        Assert.Equal(before, rig.FileBytes());
        Assert.Equal(UiStrings.Settings_AssignmentsReset, rig.Model.Notice);
    });

    [Fact]
    public void Opening_ReadsTheStoredValues_AndIsClean() => Sta(rig =>
    {
        Assert.Equal(rig.Stored, rig.Model.Current);
        Assert.False(rig.Model.IsDirty);
        Assert.False(rig.Model.HasErrors);
        Assert.Same(rig.Model.ProjectColors.Chips[0], rig.Model.ProjectColors.Selected);
    }, stored: SplashItSettings.Default with { ProjectFillOpacity = 45, FamilyBarWidth = 2, ProjectColors = [Red, Green] });
}
