using System.Globalization;
using System.Resources;
using SplashIt.UI.Tabs;

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
    internal static string Status_Title => Get(nameof(Status_Title));
    internal static string Status_Heading(object? status) => Format(nameof(Status_Heading), status);
    internal static string Status_NotStarted => Get(nameof(Status_NotStarted));
    internal static string Status_Details(object? styledTabs) => Format(nameof(Status_Details), styledTabs);

    /// <summary>The status line text for <paramref name="status"/>.</summary>
    internal static string Status(ColorizerStatus status) => Get(status switch
    {
        ColorizerStatus.Active => "Status_Active",
        ColorizerStatus.Unavailable => "Status_Unavailable",
        ColorizerStatus.BlockedByPyRevit => "Status_BlockedByPyRevit",
        ColorizerStatus.Faulted => "Status_Faulted",
        _ => "Status_Off",
    });
}
