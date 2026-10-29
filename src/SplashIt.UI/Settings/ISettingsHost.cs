using SplashIt.Core.Settings;

namespace SplashIt.UI.Settings;

/// <summary>The status line's five states (spec #1, Settings window).</summary>
public enum SettingsStatusKind
{
    /// <summary>Coloring is on and the tabs are styled.</summary>
    Active,

    /// <summary>Coloring is turned off in the settings.</summary>
    Off,

    /// <summary>The document tabs could not be found on this Revit build; tabs are left alone.</summary>
    Unavailable,

    /// <summary>pyRevit's tab colorizer was running, so Splash-It did not start.</summary>
    BlockedByPyRevit,

    /// <summary>The colorizer stopped itself after repeated faults and restored the tabs.</summary>
    Faulted,
}

/// <summary>The colorizer's state as the settings window shows it, with the reason for <see cref="SettingsStatusKind.Unavailable"/>.</summary>
public sealed record SettingsStatus(SettingsStatusKind Kind, string? Reason = null);

/// <summary>
/// The Revit-facing calls the settings window makes (spec #1, Settings window). The Revit side
/// (Lane B's command) implements it over the colorizer; tests use a fake. Every call runs on
/// Revit's UI thread, while the modal window is open.
/// </summary>
public interface ISettingsHost
{
    /// <summary>The colorizer's current state, read when the window opens and again after each Save and Reset assignments.</summary>
    SettingsStatus Status { get; }

    /// <summary>
    /// Restyles the open tabs with <paramref name="settings"/>, which were just written to disk:
    /// starts or stops the colorizer to match <see cref="SplashItSettings.Enabled"/>. Called once per valid Save.
    /// </summary>
    void Apply(SplashItSettings settings);

    /// <summary>Reassigns every open file's color from the start of its list, in tab order.</summary>
    void ResetAssignments();
}
