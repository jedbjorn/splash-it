using System.IO;
using System.Windows.Input;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Mvvm;
using SplashIt.UI.Resources;

namespace SplashIt.UI.Settings;

/// <summary>
/// The settings window (spec #1, Settings window): the status line, the enable toggle, the
/// two color lists with their sliders, and Export, Import, Reset assignments, Save and Close.
/// Edits a copy: nothing is written until Save, and an invalid Save writes nothing. A valid
/// Save writes the settings file, then calls <see cref="ISettingsHost.Apply"/> exactly once, so
/// the open tabs restyle while the window stays open. Import fills the window unsaved. Close
/// with unsaved changes asks first. Revit is reached only through <see cref="ISettingsHost"/>,
/// and files and prompts only through <see cref="IDialogService"/>, so all of it is testable
/// without Revit.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly string _path;
    private readonly ISettingsHost _host;
    private readonly IDialogService _dialogs;
    private readonly Action<string>? _log;
    private SplashItSettings _saved;
    private bool _enabled;
    private int _projectFillOpacity;
    private int _familyBarWidth;
    private int _familyBarOpacity;
    private SettingsStatus _status;
    private string? _notice;
    private string? _errorHeading;
    private IReadOnlyList<string> _errors = [];
    private bool _closeConfirmed;
    private bool _loading;

    /// <param name="settings">The settings as read from <paramref name="settingsPath"/> (the baseline for unsaved changes).</param>
    /// <param name="settingsPath">Where Save writes (<see cref="SplashItSettings.DefaultPath"/> in Revit).</param>
    /// <param name="log">Receives the atomic write's retry lines.</param>
    public SettingsViewModel(SplashItSettings settings, string settingsPath, ISettingsHost host, IDialogService dialogs, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _path = settingsPath;
        _host = host;
        _dialogs = dialogs;
        _log = log;
        _saved = settings;
        _status = ReadStatus();

        ProjectColors = new ColorListViewModel(
            UiStrings.Settings_ProjectColors, UiStrings.Settings_ProjectColorsHint, settings.ProjectColors, SplashItDefaults.ProjectColors,
            n => UiStrings.Settings_ProjectColorN(n), dialogs, "Settings.ProjectColors");
        FamilyColors = new ColorListViewModel(
            UiStrings.Settings_FamilyColors, UiStrings.Settings_FamilyColorsHint, settings.FamilyColors, SplashItDefaults.FamilyColors,
            n => UiStrings.Settings_FamilyColorN(n), dialogs, "Settings.FamilyColors");
        ProjectColors.Changed += OnChanged;
        FamilyColors.Changed += OnChanged;
        LoadValues(settings);

        SaveCommand = new RelayCommand(() => Save());
        CloseCommand = new RelayCommand(Close);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ResetAssignmentsCommand = new RelayCommand(ResetAssignments, () => CanResetAssignments);
    }

    /// <summary>Asks the window to close (after <see cref="ConfirmClose"/> allowed it).</summary>
    public event Action? CloseRequested;

    // ---- slider bounds (spec, Shapes and settings) ------------------------------------------

    public static int MinOpacity => SplashItDefaults.MinOpacity;
    public static int MaxOpacity => SplashItDefaults.MaxOpacity;
    public static int MinBarWidth => SplashItDefaults.MinBarWidth;
    public static int MaxBarWidth => SplashItDefaults.MaxBarWidth;

    // ---- status line ------------------------------------------------------------------------

    public SettingsStatus Status
    {
        get => _status;
        private set
        {
            if (!SetField(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(CanResetAssignments));
            RelayCommand.Refresh();
        }
    }

    public SettingsStatusKind StatusKind => Status.Kind;

    public string StatusText => Status.Kind switch
    {
        SettingsStatusKind.Active => UiStrings.Status_Active,
        SettingsStatusKind.Off => UiStrings.Status_Off,
        SettingsStatusKind.Unavailable => string.IsNullOrWhiteSpace(Status.Reason)
            ? UiStrings.Status_UnavailableNoReason
            : UiStrings.Status_Unavailable(Status.Reason),
        SettingsStatusKind.BlockedByPyRevit => UiStrings.Status_BlockedByPyRevit,
        SettingsStatusKind.Faulted => UiStrings.Status_Faulted,
        _ => UiStrings.Status_Off,
    };

    // ---- values ------------------------------------------------------------------------------

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value)) OnChanged();
        }
    }

    public ColorListViewModel ProjectColors { get; }

    public ColorListViewModel FamilyColors { get; }

    /// <summary>Percent. The slider keeps it in 10–100; anything else is rejected at Save.</summary>
    public int ProjectFillOpacity
    {
        get => _projectFillOpacity;
        set
        {
            if (!SetField(ref _projectFillOpacity, value)) return;
            OnPropertyChanged(nameof(ProjectFillOpacityText));
            OnChanged();
        }
    }

    /// <summary>Pixels. The slider keeps it in 1–6; anything else is rejected at Save.</summary>
    public int FamilyBarWidth
    {
        get => _familyBarWidth;
        set
        {
            if (!SetField(ref _familyBarWidth, value)) return;
            OnPropertyChanged(nameof(FamilyBarWidthText));
            OnChanged();
        }
    }

    /// <summary>Percent. The slider keeps it in 10–100; anything else is rejected at Save.</summary>
    public int FamilyBarOpacity
    {
        get => _familyBarOpacity;
        set
        {
            if (!SetField(ref _familyBarOpacity, value)) return;
            OnPropertyChanged(nameof(FamilyBarOpacityText));
            OnChanged();
        }
    }

    public string ProjectFillOpacityText => UiStrings.Settings_Percent(ProjectFillOpacity);

    public string FamilyBarWidthText => UiStrings.Settings_Pixels(FamilyBarWidth);

    public string FamilyBarOpacityText => UiStrings.Settings_Percent(FamilyBarOpacity);

    /// <summary>The window's values as settings (possibly invalid). The preview draws from it.</summary>
    public SplashItSettings Current => new()
    {
        Enabled = Enabled,
        ProjectColors = ProjectColors.Colors,
        FamilyColors = FamilyColors.Colors,
        ProjectFillOpacity = ProjectFillOpacity,
        FamilyBarWidth = FamilyBarWidth,
        FamilyBarOpacity = FamilyBarOpacity,
    };

    /// <summary>The settings last saved (or read when the window opened).</summary>
    public SplashItSettings Saved => _saved;

    /// <summary>True when the window holds values that differ from the saved settings.</summary>
    public bool IsDirty => !Current.Equals(_saved);

    // ---- messages ------------------------------------------------------------------------------

    /// <summary>An informational line ("Imported from team.json; Save to apply."), or null.</summary>
    public string? Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value);
    }

    /// <summary>The heading over <see cref="Errors"/> ("Not saved. Fix these first:"), or null.</summary>
    public string? ErrorHeading
    {
        get => _errorHeading;
        private set => SetField(ref _errorHeading, value);
    }

    /// <summary>Every problem from the last Save, Export or Import; empty when there is none.</summary>
    public IReadOnlyList<string> Errors
    {
        get => _errors;
        private set
        {
            if (SetField(ref _errors, value)) OnPropertyChanged(nameof(HasErrors));
        }
    }

    public bool HasErrors => Errors.Count > 0 || ErrorHeading is not null;

    // ---- commands --------------------------------------------------------------------------------

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ResetAssignmentsCommand { get; }

    /// <summary>Reset assignments needs a running colorizer.</summary>
    public bool CanResetAssignments => Status.Kind == SettingsStatusKind.Active;

    /// <summary>
    /// Validates, writes the settings file, then calls <see cref="ISettingsHost.Apply"/> once.
    /// Invalid values show every problem and write nothing. True when the file was written.
    /// </summary>
    public bool Save()
    {
        ClearMessages();
        var settings = Current;
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            ShowErrors(UiStrings.Settings_Invalid, errors);
            return false;
        }

        try
        {
            settings.Write(_path, _log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            ShowErrors(UiStrings.Settings_SaveFailed(ex.Message), []);
            return false;
        }

        _saved = settings;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Saved));

        try
        {
            _host.Apply(settings);
            Notice = UiStrings.Settings_Saved;
        }
        catch (Exception ex)
        {
            ShowErrors(UiStrings.Settings_ApplyFailed(ex.Message), []);
        }
        Status = ReadStatus();
        return true;
    }

    /// <summary>
    /// Whether the window may close: true with no unsaved changes; otherwise asks through
    /// <see cref="IDialogService.Confirm"/>, and true when the user discards. Asks at most once:
    /// after a discard, later calls (the window's Closing) return true.
    /// </summary>
    public bool ConfirmClose()
    {
        if (_closeConfirmed || !IsDirty) return true;
        if (!_dialogs.Confirm(UiStrings.Settings_DiscardTitle, UiStrings.Settings_DiscardMessage, [],
                UiStrings.Settings_Discard, danger: true, MessageDialog.DiscardChangesId))
            return false;
        _closeConfirmed = true;
        return true;
    }

    private void Close()
    {
        if (ConfirmClose()) CloseRequested?.Invoke();
    }

    /// <summary>Writes the window's values (saved or not) to a file the user picks. Never carries <c>enabled</c>.</summary>
    private void Export()
    {
        var settings = Current;
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            ClearMessages();
            ShowErrors(UiStrings.Settings_ExportInvalid, errors);
            return;
        }
        if (_dialogs.SaveFile(UiStrings.Settings_ExportTitle, UiStrings.Settings_FileFilter, UiStrings.Settings_ExportFileName) is not { } path)
            return;
        ClearMessages();
        try
        {
            SettingsExchange.ExportFile(settings, path, _log);
            Notice = UiStrings.Settings_Exported(Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            ShowErrors(UiStrings.Settings_ExportFailed(ex.Message), []);
        }
    }

    /// <summary>
    /// Loads an export into the window, unsaved, keeping the window's on/off. A bad file lists
    /// every problem and changes nothing.
    /// </summary>
    private void Import()
    {
        if (_dialogs.OpenFile(UiStrings.Settings_ImportTitle, UiStrings.Settings_FileFilter) is not { } path) return;
        ClearMessages();
        var name = Path.GetFileName(path);
        var result = SettingsExchange.ImportFile(path, Current);
        if (!result.Succeeded || result.Settings is not { } imported)
        {
            ShowErrors(UiStrings.Settings_ImportFailed(name), result.Errors);
            return;
        }
        LoadValues(imported with { Enabled = Enabled });
        Notice = UiStrings.Settings_Imported(name);
    }

    private void ResetAssignments()
    {
        ClearMessages();
        try
        {
            _host.ResetAssignments();
            Notice = UiStrings.Settings_AssignmentsReset;
        }
        catch (Exception ex)
        {
            ShowErrors(UiStrings.Settings_ResetAssignmentsFailed(ex.Message), []);
        }
        Status = ReadStatus();
    }

    private void LoadValues(SplashItSettings settings)
    {
        _loading = true;
        try
        {
            Enabled = settings.Enabled;
            ProjectColors.Load(settings.ProjectColors);
            FamilyColors.Load(settings.FamilyColors);
            ProjectFillOpacity = settings.ProjectFillOpacity;
            FamilyBarWidth = settings.FamilyBarWidth;
            FamilyBarOpacity = settings.FamilyBarOpacity;
        }
        finally
        {
            _loading = false;
        }
        OnChanged();
    }

    private void OnChanged()
    {
        if (_loading) return;
        _closeConfirmed = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    private SettingsStatus ReadStatus()
    {
        try
        {
            return _host.Status ?? new SettingsStatus(SettingsStatusKind.Off);
        }
        catch (Exception ex)
        {
            return new SettingsStatus(SettingsStatusKind.Unavailable, ex.Message);
        }
    }

    private void ClearMessages()
    {
        Notice = null;
        ErrorHeading = null;
        Errors = [];
        OnPropertyChanged(nameof(HasErrors));
    }

    private void ShowErrors(string heading, IReadOnlyList<string> errors)
    {
        ErrorHeading = heading;
        Errors = errors;
        OnPropertyChanged(nameof(HasErrors));
    }
}
