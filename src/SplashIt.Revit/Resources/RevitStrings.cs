using System.Globalization;
using System.Resources;

namespace SplashIt.Revit.Resources;

/// <summary>
/// Typed accessors for RevitStrings.resx. English only in v1 (decision #8): lookups use the
/// neutral resources, and formatting is culture-invariant.
/// </summary>
internal static class RevitStrings
{
    internal static readonly ResourceManager ResourceManager =
        new("SplashIt.Revit.Resources.RevitStrings", typeof(RevitStrings).Assembly);

    /// <summary>The string <paramref name="key"/>; the key itself when it is missing.</summary>
    internal static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    private static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    internal static string Ribbon_ButtonText => Get(nameof(Ribbon_ButtonText));
    internal static string Ribbon_Tooltip => Get(nameof(Ribbon_Tooltip));
    internal static string Command_CouldNotOpen(object? detail) => Format(nameof(Command_CouldNotOpen), detail);
    internal static string GroupTabs_ButtonText => Get(nameof(GroupTabs_ButtonText));
    internal static string GroupTabs_Tooltip => Get(nameof(GroupTabs_Tooltip));
    internal static string GroupTabs_Unavailable => Get(nameof(GroupTabs_Unavailable));
    internal static string GroupTabs_UnavailableDetail => Get(nameof(GroupTabs_UnavailableDetail));
    internal static string GroupTabs_Stopped => Get(nameof(GroupTabs_Stopped));
    internal static string GroupTabs_StoppedDetail => Get(nameof(GroupTabs_StoppedDetail));
    internal static string GroupTabs_StoppedSelectionDetail => Get(nameof(GroupTabs_StoppedSelectionDetail));
    internal static string GroupTabs_Error => Get(nameof(GroupTabs_Error));
    internal static string GroupTabs_ErrorDetail => Get(nameof(GroupTabs_ErrorDetail));
}
