using SplashIt.Core.Colors;
using SplashIt.Core.Resources;

namespace SplashIt.Core.Settings;

/// <summary>
/// The user's Splash-It settings, stored in <c>%AppData%\SplashIt\settings.json</c>
/// (spec, Core). Immutable; edit with <c>with</c>. Two settings are equal when every
/// value, including each color in order, is equal.
/// <para>
/// Opacities are whole percents (10 to 100) and the bar width is whole device-independent
/// pixels (1 to 6), so the stored values match the settings window exactly.
/// </para>
/// </summary>
public sealed record SplashItSettings
{
    private readonly IReadOnlyList<Rgb> _projectColors = SplashItDefaults.ProjectColors;
    private readonly IReadOnlyList<Rgb> _familyColors = SplashItDefaults.FamilyColors;

    /// <summary>The defaults: enabled, both default color lists, 100 %, 3 px, 100 %.</summary>
    public static SplashItSettings Default { get; } = new();

    /// <summary><c>%AppData%\SplashIt\settings.json</c> for the current user.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplashIt", "settings.json");

    /// <summary>Always <see cref="SplashItDefaults.SchemaVersion"/> for settings this version builds.</summary>
    public int SchemaVersion { get; init; } = SplashItDefaults.SchemaVersion;

    /// <summary>Whether tabs are colored. True with no settings file (decision #6).</summary>
    public bool Enabled { get; init; } = SplashItDefaults.Enabled;

    /// <summary>Project fill colors, in order; 1 to 12 when valid.</summary>
    public IReadOnlyList<Rgb> ProjectColors
    {
        get => _projectColors;
        init => _projectColors = Snapshot(value);
    }

    /// <summary>Family bar colors, in order; 1 to 12 when valid.</summary>
    public IReadOnlyList<Rgb> FamilyColors
    {
        get => _familyColors;
        init => _familyColors = Snapshot(value);
    }

    /// <summary>Project fill opacity, percent (10 to 100).</summary>
    public int ProjectFillOpacity { get; init; } = SplashItDefaults.ProjectFillOpacity;

    /// <summary>Family bar width, pixels (1 to 6).</summary>
    public int FamilyBarWidth { get; init; } = SplashItDefaults.FamilyBarWidth;

    /// <summary>Family bar opacity, percent (10 to 100).</summary>
    public int FamilyBarOpacity { get; init; } = SplashItDefaults.FamilyBarOpacity;

    /// <summary>
    /// Every problem with these values, in field order; empty when they are valid.
    /// Nothing is clamped here: out-of-range values are rejected. (Clamping happens
    /// only when reading from disk, in <see cref="Read"/>.)
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != SplashItDefaults.SchemaVersion)
            errors.Add(CoreStrings.Validation_Schema(SchemaVersion, SplashItDefaults.SchemaVersion));
        CheckColorCount(CoreStrings.Field_ProjectColors, ProjectColors.Count, errors);
        CheckColorCount(CoreStrings.Field_FamilyColors, FamilyColors.Count, errors);
        CheckOpacity(CoreStrings.Field_ProjectFillOpacity, ProjectFillOpacity, errors);
        CheckBarWidth(FamilyBarWidth, errors);
        CheckOpacity(CoreStrings.Field_FamilyBarOpacity, FamilyBarOpacity, errors);
        return errors;
    }

    /// <summary>
    /// Reads the settings at <paramref name="path"/>. Never throws: a missing,
    /// unreadable or corrupt file gives <see cref="Default"/> (enabled), and
    /// <paramref name="log"/> says why. Values out of range are clamped to the
    /// nearest valid value, bad colors are dropped, and a missing or emptied list
    /// falls back to its default list, each logged. The file is opened with
    /// <c>FileShare.ReadWrite | FileShare.Delete</c>, so reading never blocks a Save.
    /// <para>
    /// A file with a newer <c>schemaVersion</c> is read for the fields this version
    /// knows and comes back as schema 1, so the next <see cref="Write"/> rewrites it as
    /// schema 1 and drops the fields this version does not know. Known and accepted for v1.
    /// </para>
    /// </summary>
    public static SplashItSettings Read(string path, Action<string>? log = null)
    {
        string json;
        try
        {
            if (!File.Exists(path))
            {
                log?.Invoke("settings: no settings file; using defaults.");
                return Default;
            }
            json = AtomicFile.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            log?.Invoke(FormattableString.Invariant($"settings: the settings file could not be read ({ex.GetType().Name}); using defaults."));
            return Default;
        }
        return FromJson(json, log);
    }

    /// <summary>Parses settings text with the same tolerant rules as <see cref="Read"/>. Never throws.</summary>
    public static SplashItSettings FromJson(string? json, Action<string>? log = null) =>
        SettingsJson.ReadTolerant(json, log);

    /// <summary>
    /// Writes these settings to <paramref name="path"/> atomically (a temp file in the
    /// same folder, then a replace), creating the folder if needed. Invalid settings
    /// are refused with <see cref="InvalidOperationException"/> and nothing is written.
    /// On Windows a replace refused because another process holds the file open is
    /// retried briefly, each retry logged to <paramref name="log"/>; if it still fails,
    /// the <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// surfaces and the previous file is kept.
    /// </summary>
    public void Write(string path, Action<string>? log = null)
    {
        var errors = Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        AtomicFile.WriteAllText(path, ToJson(), log);
    }

    /// <summary>The settings file text: indented JSON with camelCase names.</summary>
    public string ToJson() => SettingsJson.Write(this, export: false);

    public bool Equals(SplashItSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Enabled == other.Enabled
        && ProjectColors.SequenceEqual(other.ProjectColors)
        && FamilyColors.SequenceEqual(other.FamilyColors)
        && ProjectFillOpacity == other.ProjectFillOpacity
        && FamilyBarWidth == other.FamilyBarWidth
        && FamilyBarOpacity == other.FamilyBarOpacity;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Enabled);
        foreach (var c in ProjectColors) hash.Add(c);
        hash.Add(-1);
        foreach (var c in FamilyColors) hash.Add(c);
        hash.Add(ProjectFillOpacity);
        hash.Add(FamilyBarWidth);
        hash.Add(FamilyBarOpacity);
        return hash.ToHashCode();
    }

    internal static void CheckColorCount(string field, int count, List<string> errors)
    {
        if (count is < SplashItDefaults.MinColors or > SplashItDefaults.MaxColors)
            errors.Add(CoreStrings.Validation_ColorCount(field, SplashItDefaults.MinColors, SplashItDefaults.MaxColors, count));
    }

    internal static void CheckOpacity(string field, int value, List<string> errors)
    {
        if (value is < SplashItDefaults.MinOpacity or > SplashItDefaults.MaxOpacity)
            errors.Add(CoreStrings.Validation_Range(field, SplashItDefaults.MinOpacity, SplashItDefaults.MaxOpacity, value));
    }

    internal static void CheckBarWidth(int value, List<string> errors)
    {
        if (value is < SplashItDefaults.MinBarWidth or > SplashItDefaults.MaxBarWidth)
            errors.Add(CoreStrings.Validation_Range(CoreStrings.Field_FamilyBarWidth, SplashItDefaults.MinBarWidth, SplashItDefaults.MaxBarWidth, value));
    }

    private static IReadOnlyList<Rgb> Snapshot(IReadOnlyList<Rgb> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Array.AsReadOnly(value.ToArray());
    }
}
