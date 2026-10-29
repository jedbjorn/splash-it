// Adapted from gantt-it src/GanttIt.Revit/UI/Dialogs/DialogService.cs (gantt-it commit aa83f35, MIT, same owner).
// Changes: namespaces; the owner is set after construction (the settings window is built
// with its view-model, which needs this service); gantt-it's ShowNoLegend and ShowSettings
// are left out; PickColor takes a title; OpenFile and SaveFile added (WPF's
// Microsoft.Win32 dialogs, no WinForms).

using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SplashIt.Core.Colors;
using SplashIt.UI.Swatches;

namespace SplashIt.UI.Dialogs;

/// <summary>Opens Splash-It's modal dialogs, each owned by the settings window.</summary>
public sealed class DialogService : IDialogService
{
    public DialogService(Window? owner = null) => Owner = owner;

    /// <summary>The window that owns the dialogs (the settings window).</summary>
    public Window? Owner { get; set; }

    public void ShowMessage(string title, string message, string automationId = MessageDialog.MessageId) =>
        Show(new MessageDialogModel(title, title, message, []), automationId);

    public bool Confirm(string title, string message, IReadOnlyList<string> items, string confirmText, bool danger, string automationId) =>
        Show(new MessageDialogModel(title, title, message, items, confirmText, HasCancel: true, Danger: danger), automationId);

    public Rgb? PickColor(Rgb current, IReadOnlyList<Rgb> palette, string title)
    {
        var workArea = Owner is null ? SystemParameters.WorkArea : MonitorWorkArea.Of(Owner);
        var dialog = new ColorPickerDialog(current, palette, title, PointerOnScreen(), workArea) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, OverwritePrompt = true, AddExtension = true, DefaultExt = ".json" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    private bool Show(MessageDialogModel model, string automationId)
    {
        var dialog = new MessageDialog(model, automationId) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    /// <summary>The pointer position in screen DIPs, so the picker opens beside the chip that was clicked.</summary>
    private Point? PointerOnScreen()
    {
        if (Owner is null || PresentationSource.FromVisual(Owner) is not { CompositionTarget: { } target }) return null;
        var device = Owner.PointToScreen(Mouse.GetPosition(Owner));
        return target.TransformFromDevice.Transform(device);
    }
}
