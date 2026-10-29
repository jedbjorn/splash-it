using System.Globalization;
using System.Resources;

namespace SplashIt.Core.Resources;

/// <summary>
/// Typed accessors for CoreStrings.resx. English only in v1 (decision #8): lookups
/// use the neutral resources, and formatting is culture-invariant.
/// </summary>
internal static class CoreStrings
{
    internal static readonly ResourceManager ResourceManager =
        new("SplashIt.Core.Resources.CoreStrings", typeof(CoreStrings).Assembly);

    /// <summary>The string <paramref name="key"/>; the key itself when it is missing.</summary>
    internal static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    private static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    internal static string Field_ProjectColors => Get(nameof(Field_ProjectColors));
    internal static string Field_FamilyColors => Get(nameof(Field_FamilyColors));
    internal static string Field_ProjectFillOpacity => Get(nameof(Field_ProjectFillOpacity));
    internal static string Field_FamilyBarWidth => Get(nameof(Field_FamilyBarWidth));
    internal static string Field_FamilyBarOpacity => Get(nameof(Field_FamilyBarOpacity));

    internal static string Validation_ColorCount(object? field, object? min, object? max, object? count) =>
        Format(nameof(Validation_ColorCount), field, min, max, count);

    internal static string Validation_Range(object? field, object? min, object? max, object? value) =>
        Format(nameof(Validation_Range), field, min, max, value);

    internal static string Validation_Schema(object? version, object? current) =>
        Format(nameof(Validation_Schema), version, current);

    internal static string Import_NotJson(object? detail) => Format(nameof(Import_NotJson), detail);
    internal static string Import_NotObject => Get(nameof(Import_NotObject));
    internal static string Import_WrongFormat => Get(nameof(Import_WrongFormat));
    internal static string Import_NoSchema => Get(nameof(Import_NoSchema));

    internal static string Import_NewerSchema(object? version, object? current) =>
        Format(nameof(Import_NewerSchema), version, current);

    internal static string Import_OldSchema(object? version) => Format(nameof(Import_OldSchema), version);
    internal static string Import_Missing(object? property) => Format(nameof(Import_Missing), property);
    internal static string Import_NotWholeNumber(object? field) => Format(nameof(Import_NotWholeNumber), field);
    internal static string Import_NotColorList(object? field) => Format(nameof(Import_NotColorList), field);

    internal static string Import_BadColor(object? field, object? position, object? value) =>
        Format(nameof(Import_BadColor), field, position, value);

    internal static string Import_TooLarge(object? kilobytes) => Format(nameof(Import_TooLarge), kilobytes);
    internal static string Import_ReadFailed(object? detail) => Format(nameof(Import_ReadFailed), detail);
}
