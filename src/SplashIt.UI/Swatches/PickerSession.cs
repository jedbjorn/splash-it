using SplashIt.Core.Colors;

namespace SplashIt.UI.Swatches;

/// <summary>
/// Picker state kept for the Revit session, as gantt-it keeps it on its session object
/// (gantt-it src/GanttIt.Revit/UI/GanttItSession.cs, commit aa83f35): the recent-colors row
/// survives closing and reopening the settings window, and is lost when Revit closes.
/// </summary>
public static class PickerSession
{
    /// <summary>The picker's recent colors, most recent first (12 at most).</summary>
    public static RecentColors RecentColors { get; } = new();
}
