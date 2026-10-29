// Adapted from gantt-it src/GanttIt.Revit/UI/Dialogs/IDialogService.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: namespaces; gantt-it's ShowNoLegend and ShowSettings are left out; PickColor takes
// a title; OpenFile and SaveFile added for Export and Import (spec #1, Settings window).

using SplashIt.Core.Colors;

namespace SplashIt.UI.Dialogs;

/// <summary>The modal dialogs the view-models open, so they never touch windows directly.</summary>
public interface IDialogService
{
    /// <summary>An informational or error message with one OK button.</summary>
    void ShowMessage(string title, string message, string automationId = MessageDialog.MessageId);

    /// <summary>A confirmation listing <paramref name="items"/>. True when the user confirms.</summary>
    bool Confirm(string title, string message, IReadOnlyList<string> items, string confirmText, bool danger, string automationId);

    /// <summary>The color picker, titled <paramref name="title"/>. Null when cancelled.</summary>
    Rgb? PickColor(Rgb current, IReadOnlyList<Rgb> palette, string title);

    /// <summary>The Windows Open dialog. The chosen path, or null when cancelled.</summary>
    string? OpenFile(string title, string filter);

    /// <summary>The Windows Save As dialog, starting with <paramref name="fileName"/>. The chosen path, or null when cancelled.</summary>
    string? SaveFile(string title, string filter, string fileName);
}
