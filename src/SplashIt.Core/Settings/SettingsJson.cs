using System.Text.Encodings.Web;
using System.Text.Json;
using SplashIt.Core.Colors;
using SplashIt.Core.Resources;

namespace SplashIt.Core.Settings;

/// <summary>
/// The JSON shape shared by settings.json and the export file, with two readers:
/// <see cref="ReadTolerant"/> for the user's own settings file (clamp, drop and
/// default, logging each fix; never fails) and <see cref="ReadStrict"/> for an
/// import (reject, listing every problem).
/// </summary>
internal static class SettingsJson
{
    public const string FormatKey = "format";
    public const string SchemaVersionKey = "schemaVersion";
    public const string EnabledKey = "enabled";
    public const string ProjectColorsKey = "projectColors";
    public const string FamilyColorsKey = "familyColors";
    public const string ProjectFillOpacityKey = "projectFillOpacity";
    public const string FamilyBarWidthKey = "familyBarWidth";
    public const string FamilyBarOpacityKey = "familyBarOpacity";

    /// <summary>The <c>format</c> value that marks an export file.</summary>
    public const string ExportFormat = "splash-it-settings";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Settings as JSON. An export starts with <c>format</c> and never carries
    /// <c>enabled</c>, so importing someone's colors never switches the add-in on or off.
    /// </summary>
    public static string Write(SplashItSettings settings, bool export)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            if (export) w.WriteString(FormatKey, ExportFormat);
            w.WriteNumber(SchemaVersionKey, settings.SchemaVersion);
            if (!export) w.WriteBoolean(EnabledKey, settings.Enabled);
            WriteColors(w, ProjectColorsKey, settings.ProjectColors);
            WriteColors(w, FamilyColorsKey, settings.FamilyColors);
            w.WriteNumber(ProjectFillOpacityKey, settings.ProjectFillOpacity);
            w.WriteNumber(FamilyBarWidthKey, settings.FamilyBarWidth);
            w.WriteNumber(FamilyBarOpacityKey, settings.FamilyBarOpacity);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteColors(Utf8JsonWriter w, string key, IReadOnlyList<Rgb> colors)
    {
        w.WriteStartArray(key);
        foreach (var c in colors) w.WriteStringValue(c.ToHex());
        w.WriteEndArray();
    }

    // ---------------------------------------------------------------- tolerant

    /// <summary>
    /// Reads the user's settings file. Not JSON, or not a JSON object, gives the
    /// defaults. Otherwise each field is read on its own: missing → its default;
    /// wrong type → its default; a number out of range → clamped to the nearest
    /// valid value (a fraction is rounded first); a bad color entry → dropped; more
    /// than 12 colors → the first 12; no usable color → the default list. A newer
    /// schemaVersion is read for the fields this version knows. Never throws.
    /// <para>
    /// Known and accepted for v1: the result is schema 1, so the next Save rewrites a
    /// newer-schema file as schema 1 and drops any fields this version does not know
    /// (docs/DEVELOPING.md, Settings file).
    /// </para>
    /// </summary>
    public static SplashItSettings ReadTolerant(string? json, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            log?.Invoke("settings: the settings file is empty; using defaults.");
            return SplashItSettings.Default;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            Log(log, $"settings: the settings file is not valid JSON ({ex.Message}); using defaults.");
            return SplashItSettings.Default;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Log(log, $"settings: the settings file holds a {root.ValueKind}, not an object; using defaults.");
                return SplashItSettings.Default;
            }

            if (!TryWholeNumber(root, SchemaVersionKey, out var schema))
                log?.Invoke("settings: no whole-number schemaVersion; reading as schema 1.");
            else if (schema > SplashItDefaults.SchemaVersion)
                Log(log, $"settings: schemaVersion {schema} is newer than {SplashItDefaults.SchemaVersion}; reading the known fields.");
            else if (schema < SplashItDefaults.SchemaVersion)
                Log(log, $"settings: schemaVersion {schema} is not supported; reading as schema 1.");

            return new SplashItSettings
            {
                Enabled = TolerantBool(root, EnabledKey, SplashItDefaults.Enabled, log),
                ProjectColors = TolerantColors(root, ProjectColorsKey, SplashItDefaults.ProjectColors, log),
                FamilyColors = TolerantColors(root, FamilyColorsKey, SplashItDefaults.FamilyColors, log),
                ProjectFillOpacity = TolerantInt(root, ProjectFillOpacityKey,
                    SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity, SplashItDefaults.ProjectFillOpacity, log),
                FamilyBarWidth = TolerantInt(root, FamilyBarWidthKey,
                    SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth, SplashItDefaults.FamilyBarWidth, log),
                FamilyBarOpacity = TolerantInt(root, FamilyBarOpacityKey,
                    SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity, SplashItDefaults.FamilyBarOpacity, log),
            };
        }
    }

    private static bool TolerantBool(JsonElement root, string key, bool fallback, Action<string>? log)
    {
        if (!root.TryGetProperty(key, out var e))
        {
            Log(log, $"settings: no {key}; using {fallback.ToString().ToLowerInvariant()}.");
            return fallback;
        }
        if (e.ValueKind == JsonValueKind.True) return true;
        if (e.ValueKind == JsonValueKind.False) return false;
        Log(log, $"settings: {key} is not true or false; using {fallback.ToString().ToLowerInvariant()}.");
        return fallback;
    }

    private static int TolerantInt(JsonElement root, string key, int min, int max, int fallback, Action<string>? log)
    {
        if (!root.TryGetProperty(key, out var e))
        {
            Log(log, $"settings: no {key}; using {fallback}.");
            return fallback;
        }
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out var d) || !double.IsFinite(d))
        {
            Log(log, $"settings: {key} is not a number; using {fallback}.");
            return fallback;
        }
        var rounded = Math.Round(d, MidpointRounding.AwayFromZero);
        var clamped = (int)Math.Clamp(rounded, min, max);
        if (clamped != d) Log(log, $"settings: {key} {d} is outside {min} to {max} or not whole; using {clamped}.");
        return clamped;
    }

    private static IReadOnlyList<Rgb> TolerantColors(JsonElement root, string key, IReadOnlyList<Rgb> fallback, Action<string>? log)
    {
        if (!root.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Array)
        {
            Log(log, $"settings: {key} is missing or not a list; using the default list.");
            return fallback;
        }

        var colors = new List<Rgb>();
        var position = 0;
        foreach (var item in e.EnumerateArray())
        {
            position++;
            if (TryGetString(item, out var text) && ColorMath.TryParseHex(text, out var c))
                colors.Add(c);
            else
                Log(log, $"settings: {key} entry {position} is not a #RRGGBB color; dropped.");
        }

        if (colors.Count > SplashItDefaults.MaxColors)
        {
            Log(log, $"settings: {key} has {colors.Count} colors; keeping the first {SplashItDefaults.MaxColors}.");
            colors.RemoveRange(SplashItDefaults.MaxColors, colors.Count - SplashItDefaults.MaxColors);
        }
        if (colors.Count < SplashItDefaults.MinColors)
        {
            Log(log, $"settings: {key} has no usable color; using the default list.");
            return fallback;
        }
        return colors;
    }

    // ------------------------------------------------------------------ strict

    /// <summary>Parses import text; null with one error when it is not a JSON object.</summary>
    public static JsonDocument? ParseStrict(string? json, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add(CoreStrings.Import_Empty);
            return null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            errors.Add(CoreStrings.Import_NotJson(ex.Message));
            return null;
        }
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            errors.Add(CoreStrings.Import_NotObject);
            return null;
        }
        return doc;
    }

    /// <summary>
    /// Reads an export: checks <c>format</c> and <c>schemaVersion</c> and validates every
    /// field, adding every problem to <paramref name="errors"/>. Returns the settings
    /// (with <paramref name="enabled"/>; any <c>enabled</c> in the file is ignored) only
    /// when there is no problem. Unknown properties are ignored.
    /// </summary>
    public static SplashItSettings? ReadStrict(JsonElement root, bool enabled, List<string> errors)
    {
        var start = errors.Count;

        if (!root.TryGetProperty(FormatKey, out var format)
            || !TryGetString(format, out var formatText)
            || formatText != ExportFormat)
            errors.Add(CoreStrings.Import_WrongFormat);

        if (!TryWholeNumber(root, SchemaVersionKey, out var schema))
            errors.Add(CoreStrings.Import_NoSchema);
        else if (schema > SplashItDefaults.SchemaVersion)
            errors.Add(CoreStrings.Import_NewerSchema(schema, SplashItDefaults.SchemaVersion));
        else if (schema < SplashItDefaults.SchemaVersion)
            errors.Add(CoreStrings.Import_OldSchema(schema));

        var project = StrictColors(root, ProjectColorsKey, CoreStrings.Field_ProjectColors, errors);
        var family = StrictColors(root, FamilyColorsKey, CoreStrings.Field_FamilyColors, errors);
        var fill = StrictInt(root, ProjectFillOpacityKey, CoreStrings.Field_ProjectFillOpacity,
            SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity, errors);
        var width = StrictInt(root, FamilyBarWidthKey, CoreStrings.Field_FamilyBarWidth,
            SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth, errors);
        var bar = StrictInt(root, FamilyBarOpacityKey, CoreStrings.Field_FamilyBarOpacity,
            SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity, errors);

        if (errors.Count > start) return null;
        return new SplashItSettings
        {
            Enabled = enabled,
            ProjectColors = project!,
            FamilyColors = family!,
            ProjectFillOpacity = fill!.Value,
            FamilyBarWidth = width!.Value,
            FamilyBarOpacity = bar!.Value,
        };
    }

    private static IReadOnlyList<Rgb>? StrictColors(JsonElement root, string key, string field, List<string> errors)
    {
        if (!root.TryGetProperty(key, out var e))
        {
            errors.Add(CoreStrings.Import_Missing(key));
            return null;
        }
        if (e.ValueKind != JsonValueKind.Array)
        {
            errors.Add(CoreStrings.Import_NotColorList(field));
            return null;
        }

        var ok = true;
        var colors = new List<Rgb>();
        var position = 0;
        foreach (var item in e.EnumerateArray())
        {
            position++;
            if (TryGetString(item, out var text) && ColorMath.TryParseHex(text, out var c))
            {
                colors.Add(c);
            }
            else
            {
                errors.Add(CoreStrings.Import_BadColor(field, position, Shorten(item.GetRawText())));
                ok = false;
            }
        }

        var countErrors = new List<string>();
        SplashItSettings.CheckColorCount(field, position, countErrors);
        errors.AddRange(countErrors);
        return ok && countErrors.Count == 0 ? colors : null;
    }

    private static int? StrictInt(JsonElement root, string key, string field, int min, int max, List<string> errors)
    {
        if (!root.TryGetProperty(key, out var e))
        {
            errors.Add(CoreStrings.Import_Missing(key));
            return null;
        }
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out var d) || !double.IsFinite(d) || d != Math.Floor(d))
        {
            errors.Add(CoreStrings.Import_NotWholeNumber(field));
            return null;
        }
        if (d < min || d > max)
        {
            errors.Add(CoreStrings.Validation_Range(field, min, max, Shorten(e.GetRawText())));
            return null;
        }
        return (int)d;
    }

    /// <summary>A whole-number property that fits an int; false when missing, not a number, fractional or too large.</summary>
    private static bool TryWholeNumber(JsonElement root, string key, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Number) return false;
        if (!e.TryGetDouble(out var d) || !double.IsFinite(d) || d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
            return false;
        value = (int)d;
        return true;
    }

    /// <summary>
    /// The value of a JSON string, without throwing. False when <paramref name="e"/> is
    /// not a string, or when it cannot be decoded: an escaped unpaired surrogate such as
    /// <c>"\ud800"</c> is legal JSON that <see cref="JsonDocument"/> accepts, but
    /// <see cref="JsonElement.GetString"/> throws <see cref="InvalidOperationException"/> on it.
    /// Callers treat false as a bad value.
    /// </summary>
    internal static bool TryGetString(JsonElement e, out string? value)
    {
        value = null;
        if (e.ValueKind != JsonValueKind.String) return false;
        try
        {
            value = e.GetString();
            return value is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Logs with numbers formatted culture-invariantly, so logs read the same on every machine.</summary>
    private static void Log(Action<string>? log, FormattableString message) =>
        log?.Invoke(FormattableString.Invariant(message));

    /// <summary>Raw JSON text for an error message, cut to a readable length.</summary>
    private static string Shorten(string raw) => raw.Length <= 40 ? raw : raw[..37] + "...";
}
