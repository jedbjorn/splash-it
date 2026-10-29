using SplashIt.Core.Logging;
using SplashIt.Core.Settings;
using SplashIt.UI.Settings;

namespace SplashIt.UI.Tabs;

/// <summary>
/// The settings window's <see cref="ISettingsHost"/> over the tab colorizer (review m8): the
/// status line reads <see cref="DocumentTabColorizer.Status"/>, Save calls
/// <see cref="DocumentTabColorizer.Apply"/>, and Reset assignments calls
/// <see cref="DocumentTabColorizer.ResetAssignments"/>. When the colorizer never started (its
/// creation failed at <c>ApplicationInitialized</c>), the window shows "Stopped after an error"
/// and Save still writes the file; it applies at the next Revit start.
/// </summary>
public sealed class ColorizerSettingsHost : ISettingsHost
{
    private readonly Func<DocumentTabColorizer?> _colorizer;
    private readonly ILog _log;

    /// <param name="colorizer">The running colorizer, or null when it did not start.</param>
    public ColorizerSettingsHost(Func<DocumentTabColorizer?> colorizer, ILog? log = null)
    {
        _colorizer = colorizer ?? throw new ArgumentNullException(nameof(colorizer));
        _log = log ?? NullLog.Instance;
    }

    public SettingsStatus Status => _colorizer() is { } colorizer
        ? new SettingsStatus(ToSettingsStatus(colorizer.Status), colorizer.StatusDetail)
        : new SettingsStatus(SettingsStatusKind.Faulted);

    public void Apply(SplashItSettings settings)
    {
        if (_colorizer() is { } colorizer) colorizer.Apply(settings);
        else _log.Warn("settings: saved, but the colorizer is not running; the settings apply at the next start.");
    }

    public void ResetAssignments() => _colorizer()?.ResetAssignments();

    /// <summary>
    /// The window's status for a colorizer status, matched by name (the two enums list the
    /// same states in a different order, so a cast would be wrong).
    /// </summary>
    public static SettingsStatusKind ToSettingsStatus(ColorizerStatus status) => status switch
    {
        ColorizerStatus.Active => SettingsStatusKind.Active,
        ColorizerStatus.Off => SettingsStatusKind.Off,
        ColorizerStatus.Unavailable => SettingsStatusKind.Unavailable,
        ColorizerStatus.BlockedByPyRevit => SettingsStatusKind.BlockedByPyRevit,
        ColorizerStatus.Faulted => SettingsStatusKind.Faulted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
