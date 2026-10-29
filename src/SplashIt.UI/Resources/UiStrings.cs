using System.Globalization;
using System.Resources;

namespace SplashIt.UI.Resources;

/// <summary>
/// Typed accessors for UiStrings.resx, public so XAML can bind them with
/// <c>{x:Static res:UiStrings.Key}</c>. English only in v1 (decision #8): lookups use the
/// neutral resources and formatting is culture-invariant. This replaces gantt-it's
/// localization plumbing (TrExtension, RevitLanguage), which Splash-It leaves out.
/// </summary>
public static class UiStrings
{
    internal static readonly ResourceManager ResourceManager =
        new("SplashIt.UI.Resources.UiStrings", typeof(UiStrings).Assembly);

    /// <summary>The string <paramref name="key"/>; the key itself when it is missing.</summary>
    public static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    private static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    public static string Common_OK => Get(nameof(Common_OK));

    public static string Common_Cancel => Get(nameof(Common_Cancel));

    public static string Dialog_Items => Get(nameof(Dialog_Items));

    public static string Edit_Cut => Get(nameof(Edit_Cut));

    public static string Edit_Copy => Get(nameof(Edit_Copy));

    public static string Edit_Paste => Get(nameof(Edit_Paste));

    public static string Color_DialogTitle => Get(nameof(Color_DialogTitle));

    public static string Color_Palette => Get(nameof(Color_Palette));

    public static string Color_Recent => Get(nameof(Color_Recent));

    public static string Color_Hex => Get(nameof(Color_Hex));

    public static string Color_HexName => Get(nameof(Color_HexName));

    public static string Color_CurrentTip => Get(nameof(Color_CurrentTip));

    public static string Color_Apply => Get(nameof(Color_Apply));

    public static string Color_PickTip => Get(nameof(Color_PickTip));

    public static string Color_PickName => Get(nameof(Color_PickName));

    public static string Color_More => Get(nameof(Color_More));

    public static string Color_MoreName => Get(nameof(Color_MoreName));

    public static string Color_HexError => Get(nameof(Color_HexError));

    public static string Color_PickFailed(object? arg0) => Format(nameof(Color_PickFailed), arg0);

    public static string Pick_WindowTitle => Get(nameof(Pick_WindowTitle));

    public static string Pick_Hint => Get(nameof(Pick_Hint));

    public static string Pick_ListFailed => Get(nameof(Pick_ListFailed));

    public static string Pick_AreaTooLarge(object? arg0, object? arg1) => Format(nameof(Pick_AreaTooLarge), arg0, arg1);

    public static string Pick_NoAccess => Get(nameof(Pick_NoAccess));

    public static string Pick_CopyRefused => Get(nameof(Pick_CopyRefused));

    public static string Pick_PointerFailed => Get(nameof(Pick_PointerFailed));

    public static string Pick_NoMonitors => Get(nameof(Pick_NoMonitors));

    public static string Settings_WindowTitle => Get(nameof(Settings_WindowTitle));

    public static string Settings_Heading => Get(nameof(Settings_Heading));

    public static string Settings_Subheading => Get(nameof(Settings_Subheading));

    public static string Status_Active => Get(nameof(Status_Active));

    public static string Status_Off => Get(nameof(Status_Off));

    public static string Status_Unavailable(object? arg0) => Format(nameof(Status_Unavailable), arg0);

    public static string Status_UnavailableNoReason => Get(nameof(Status_UnavailableNoReason));

    public static string Status_BlockedByPyRevit => Get(nameof(Status_BlockedByPyRevit));

    public static string Status_Faulted => Get(nameof(Status_Faulted));

    public static string Settings_Enabled => Get(nameof(Settings_Enabled));

    public static string Settings_EnabledTip => Get(nameof(Settings_EnabledTip));

    public static string Settings_ProjectColors => Get(nameof(Settings_ProjectColors));

    public static string Settings_ProjectColorsHint => Get(nameof(Settings_ProjectColorsHint));

    public static string Settings_FamilyColors => Get(nameof(Settings_FamilyColors));

    public static string Settings_FamilyColorsHint => Get(nameof(Settings_FamilyColorsHint));

    public static string Settings_ProjectColorN(object? arg0) => Format(nameof(Settings_ProjectColorN), arg0);

    public static string Settings_FamilyColorN(object? arg0) => Format(nameof(Settings_FamilyColorN), arg0);

    public static string Settings_ChipName(object? arg0, object? arg1) => Format(nameof(Settings_ChipName), arg0, arg1);

    public static string Settings_ChipTip(object? arg0, object? arg1) => Format(nameof(Settings_ChipTip), arg0, arg1);

    public static string Settings_AddColor => Get(nameof(Settings_AddColor));

    public static string Settings_RemoveColor => Get(nameof(Settings_RemoveColor));

    public static string Settings_MoveLeft => Get(nameof(Settings_MoveLeft));

    public static string Settings_MoveRight => Get(nameof(Settings_MoveRight));

    public static string Settings_ResetColors => Get(nameof(Settings_ResetColors));

    public static string Settings_ResetColorsTip => Get(nameof(Settings_ResetColorsTip));

    public static string Settings_ColorCount(object? arg0, object? arg1) => Format(nameof(Settings_ColorCount), arg0, arg1);

    public static string Settings_FillOpacity => Get(nameof(Settings_FillOpacity));

    public static string Settings_UnselectedOpacity => Get(nameof(Settings_UnselectedOpacity));

    public static string Settings_UnselectedOpacityTip => Get(nameof(Settings_UnselectedOpacityTip));

    public static string Settings_BarWidth => Get(nameof(Settings_BarWidth));

    public static string Settings_BarOpacity => Get(nameof(Settings_BarOpacity));

    public static string Settings_Percent(object? arg0) => Format(nameof(Settings_Percent), arg0);

    public static string Settings_Pixels(object? arg0) => Format(nameof(Settings_Pixels), arg0);

    public static string Settings_Preview => Get(nameof(Settings_Preview));

    public static string Settings_PreviewHint => Get(nameof(Settings_PreviewHint));

    public static string Preview_Tower1 => Get(nameof(Preview_Tower1));

    public static string Preview_Tower2 => Get(nameof(Preview_Tower2));

    public static string Preview_Annex => Get(nameof(Preview_Annex));

    public static string Preview_Family => Get(nameof(Preview_Family));

    public static string Preview_Template => Get(nameof(Preview_Template));

    public static string Preview_Off => Get(nameof(Preview_Off));

    public static string Settings_Export => Get(nameof(Settings_Export));

    public static string Settings_ExportTip => Get(nameof(Settings_ExportTip));

    public static string Settings_Import => Get(nameof(Settings_Import));

    public static string Settings_ImportTip => Get(nameof(Settings_ImportTip));

    public static string Settings_ResetAssignments => Get(nameof(Settings_ResetAssignments));

    public static string Settings_ResetAssignmentsTip => Get(nameof(Settings_ResetAssignmentsTip));

    public static string Settings_Save => Get(nameof(Settings_Save));

    public static string Settings_Close => Get(nameof(Settings_Close));

    public static string Settings_Imported(object? arg0) => Format(nameof(Settings_Imported), arg0);

    public static string Settings_ImportFailed(object? arg0) => Format(nameof(Settings_ImportFailed), arg0);

    public static string Settings_Invalid => Get(nameof(Settings_Invalid));

    public static string Settings_SaveFailed(object? arg0) => Format(nameof(Settings_SaveFailed), arg0);

    public static string Settings_Saved => Get(nameof(Settings_Saved));

    public static string Settings_ApplyFailed(object? arg0) => Format(nameof(Settings_ApplyFailed), arg0);

    public static string Settings_Exported(object? arg0) => Format(nameof(Settings_Exported), arg0);

    public static string Settings_ExportFailed(object? arg0) => Format(nameof(Settings_ExportFailed), arg0);

    public static string Settings_ExportInvalid => Get(nameof(Settings_ExportInvalid));

    public static string Settings_AssignmentsReset => Get(nameof(Settings_AssignmentsReset));

    public static string Settings_ResetAssignmentsFailed(object? arg0) => Format(nameof(Settings_ResetAssignmentsFailed), arg0);

    public static string Settings_DiscardTitle => Get(nameof(Settings_DiscardTitle));

    public static string Settings_DiscardMessage => Get(nameof(Settings_DiscardMessage));

    public static string Settings_Discard => Get(nameof(Settings_Discard));

    public static string Settings_FileFilter => Get(nameof(Settings_FileFilter));

    public static string Settings_ExportTitle => Get(nameof(Settings_ExportTitle));

    public static string Settings_ImportTitle => Get(nameof(Settings_ImportTitle));

    public static string Settings_ExportFileName => Get(nameof(Settings_ExportFileName));
}
