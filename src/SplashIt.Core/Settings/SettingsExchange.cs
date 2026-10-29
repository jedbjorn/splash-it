using SplashIt.Core.Resources;

namespace SplashIt.Core.Settings;

/// <summary>The outcome of an import: settings to show unsaved, or every problem found.</summary>
public sealed record ImportResult(SplashItSettings? Settings, IReadOnlyList<string> Errors)
{
    /// <summary>True when <see cref="Settings"/> holds the imported values and there are no errors.</summary>
    public bool Succeeded => Settings is not null && Errors.Count == 0;
}

/// <summary>
/// Settings export and import (spec, Core; decision #14). An export is
/// <c>{ "format": "splash-it-settings", "schemaVersion": 1, ...settings }</c>
/// without <c>enabled</c>, so importing someone else's colors never switches the
/// add-in on or off. Import accepts only that format, rejects a newer
/// <c>schemaVersion</c> with a clear message, validates every field, and reports
/// all errors rather than the first. Nothing is clamped on import.
/// </summary>
public static class SettingsExchange
{
    /// <summary>The <c>format</c> value of an export file.</summary>
    public const string Format = SettingsJson.ExportFormat;

    /// <summary>Exports larger than this are refused unread (a real export is under 1 KB).</summary>
    public const int MaxImportBytes = 64 * 1024;

    /// <summary>
    /// The export text for <paramref name="settings"/>. Invalid settings are refused
    /// with <see cref="InvalidOperationException"/>.
    /// </summary>
    public static string Export(SplashItSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        return SettingsJson.Write(settings, export: true);
    }

    /// <summary>Writes the export of <paramref name="settings"/> to <paramref name="path"/> atomically.</summary>
    public static void ExportFile(SplashItSettings settings, string path) =>
        AtomicFile.WriteAllText(path, Export(settings));

    /// <summary>
    /// Reads an export. On success the result's settings carry the file's colors and
    /// numbers with <paramref name="current"/>'s <c>Enabled</c>; otherwise its errors
    /// list every problem. Never throws for bad input.
    /// </summary>
    public static ImportResult Import(string? json, SplashItSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var errors = new List<string>();
        using var doc = SettingsJson.ParseStrict(json, errors);
        if (doc is null) return new ImportResult(null, errors);
        var settings = SettingsJson.ReadStrict(doc.RootElement, current.Enabled, errors);
        return new ImportResult(errors.Count == 0 ? settings : null, errors);
    }

    /// <summary>Reads the export at <paramref name="path"/>; see <see cref="Import"/>. Never throws for bad input.</summary>
    public static ImportResult ImportFile(string path, SplashItSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        string json;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return Failed(CoreStrings.Import_ReadFailed(new FileNotFoundException().Message));
            if (info.Length > MaxImportBytes)
                return Failed(CoreStrings.Import_TooLarge(MaxImportBytes / 1024));
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            return Failed(CoreStrings.Import_ReadFailed(ex.Message));
        }
        return Import(json, current);
    }

    private static ImportResult Failed(string error) => new(null, [error]);
}
