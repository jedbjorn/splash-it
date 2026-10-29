using System.IO;
using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.UI.Dialogs;
using SplashIt.UI.Settings;

namespace SplashIt.RuntimeTests.Support;

/// <summary>The Revit side as the settings window sees it: records every call.</summary>
public sealed class FakeHost : ISettingsHost
{
    /// <summary>The settings file, read back inside <see cref="Apply"/> to prove the write came first.</summary>
    public string? SettingsPath { get; set; }

    public SettingsStatus Status { get; set; } = new(SettingsStatusKind.Active);

    /// <summary>After Apply, the status becomes this (e.g. Off when the user disabled coloring).</summary>
    public Func<SplashItSettings, SettingsStatus>? StatusAfterApply { get; set; }

    public Exception? ApplyThrows { get; set; }

    public List<SplashItSettings> Applied { get; } = [];

    /// <summary>The settings file's text at the moment Apply ran (null when it did not exist).</summary>
    public List<string?> FileAtApply { get; } = [];

    public int ResetAssignmentsCount { get; private set; }

    public void Apply(SplashItSettings settings)
    {
        Applied.Add(settings);
        FileAtApply.Add(SettingsPath is not null && File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null);
        if (ApplyThrows is not null) throw ApplyThrows;
        if (StatusAfterApply is not null) Status = StatusAfterApply(settings);
    }

    public void ResetAssignments() => ResetAssignmentsCount++;
}

/// <summary>Scripted answers for every dialog, and a record of what was asked.</summary>
public sealed class FakeDialogs : IDialogService
{
    public Queue<Rgb?> PickResults { get; } = new();

    public List<(Rgb Current, IReadOnlyList<Rgb> Palette, string Title)> PickCalls { get; } = [];

    public bool ConfirmResult { get; set; }

    public List<(string Title, string Message, string ConfirmText, bool Danger, string AutomationId)> ConfirmCalls { get; } = [];

    public List<(string Title, string Message)> Messages { get; } = [];

    public string? OpenResult { get; set; }

    public string? SaveResult { get; set; }

    public int OpenCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public void ShowMessage(string title, string message, string automationId = MessageDialog.MessageId) =>
        Messages.Add((title, message));

    public bool Confirm(string title, string message, IReadOnlyList<string> items, string confirmText, bool danger, string automationId)
    {
        ConfirmCalls.Add((title, message, confirmText, danger, automationId));
        return ConfirmResult;
    }

    public Rgb? PickColor(Rgb current, IReadOnlyList<Rgb> palette, string title)
    {
        PickCalls.Add((current, palette, title));
        return PickResults.Count > 0 ? PickResults.Dequeue() : null;
    }

    public string? OpenFile(string title, string filter)
    {
        OpenCalls++;
        return OpenResult;
    }

    public string? SaveFile(string title, string filter, string fileName)
    {
        SaveCalls++;
        return SaveResult;
    }
}

/// <summary>A temp folder deleted on dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "splashit-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
