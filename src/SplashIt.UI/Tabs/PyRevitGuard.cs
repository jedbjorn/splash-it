using System.Reflection;
using SplashIt.Core.Logging;

namespace SplashIt.UI.Tabs;

/// <summary>
/// The pyRevit guard (spec, Revit: pyRevit guard): tells, from the loaded assemblies and by
/// reflection, whether pyRevit's document-tab colorizer is running.
/// <para>
/// <b>Clean room (spec, Clean Room).</b> This uses only public knowledge of names: pyRevit's
/// assemblies are named <c>pyRevit…</c> / <c>pyRevitLabs…</c>, and its tab-coloring support
/// lives in a type whose name contains <c>DocumentTab</c>. No pyRevit source was read or
/// used. The member that says "running" is not known in advance, so the guard reads every
/// static <see cref="bool"/> property or field on such types whose name suggests an on state
/// (<see cref="StateWords"/>), and treats any true one as running.
/// </para>
/// <list type="bullet">
/// <item>No pyRevit assembly → not running.</item>
/// <item>pyRevit loaded, but no such type or no readable state → not running (Splash-It
/// starts), logged once so AC11 can see it.</item>
/// <item>Any matching state true → running; the detail names the assembly, type and member.</item>
/// </list>
/// Never throws.
/// </summary>
public sealed class PyRevitGuard : IForeignColorizerGuard
{
    /// <summary>Assembly-name prefixes taken as pyRevit's (case-insensitive).</summary>
    public static readonly IReadOnlyList<string> AssemblyPrefixes = ["pyRevit"]; // covers pyRevitLabs.* (case-insensitive)

    /// <summary>Type-name fragment of pyRevit's tab-coloring support.</summary>
    public const string TypeFragment = "DocumentTab";

    /// <summary>Name fragments of a static bool that says the colorizer is on.</summary>
    public static readonly IReadOnlyList<string> StateWords =
        ["Active", "Running", "Tracking", "Enabled", "Updating", "Coloring", "Colorizing", "Started"];

    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Func<IEnumerable<Assembly>> _assemblies;
    private readonly ILog _log;
    private string _lastLog = "";

    /// <param name="assemblies">The assemblies to inspect; the current AppDomain's when null.</param>
    public PyRevitGuard(Func<IEnumerable<Assembly>>? assemblies = null, ILog? log = null)
    {
        _assemblies = assemblies ?? (() => AppDomain.CurrentDomain.GetAssemblies());
        _log = log ?? NullLog.Instance;
    }

    public bool IsRunning(out string? detail)
    {
        detail = null;
        try
        {
            var pyRevitLoaded = false;
            var typesSeen = 0;
            foreach (var assembly in _assemblies())
            {
                var name = assembly.GetName().Name ?? "";
                if (!AssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                pyRevitLoaded = true;

                foreach (var type in TypesOf(assembly))
                {
                    if (type is null || !type.Name.Contains(TypeFragment, StringComparison.OrdinalIgnoreCase)) continue;
                    typesSeen++;
                    if (ReadOn(type) is not { } member) continue;
                    detail = $"pyRevit's tab colorizer is running ({name}: {type.Name}.{member}).";
                    LogOnce("pyRevit guard: " + detail);
                    return true;
                }
            }

            LogOnce(pyRevitLoaded
                ? $"pyRevit guard: pyRevit is loaded; {typesSeen} tab types found, none reports a running colorizer."
                : "pyRevit guard: pyRevit is not loaded.");
            return false;
        }
        catch (Exception ex)
        {
            LogOnce("pyRevit guard: the check failed (" + ex.GetType().Name + "); treating pyRevit as not coloring.");
            return false;
        }
    }

    /// <summary>The name of a static bool state member on <paramref name="type"/> that is true; else null.</summary>
    private static string? ReadOn(Type type)
    {
        // Fields first: reading a field runs no code of pyRevit's. A property getter is only
        // read when no field answers (review n2).
        foreach (var field in type.GetFields(StaticMembers))
        {
            if (field.FieldType != typeof(bool) || !IsStateName(field.Name)) continue;
            if (TryRead(() => field.GetValue(null))) return field.Name;
        }
        foreach (var property in type.GetProperties(StaticMembers))
        {
            if (property.PropertyType != typeof(bool) || property.GetIndexParameters().Length > 0 || !IsStateName(property.Name)) continue;
            if (property.GetGetMethod(nonPublic: true) is null) continue;
            if (TryRead(() => property.GetValue(null))) return property.Name;
        }
        return null;
    }

    private static bool TryRead(Func<object?> read)
    {
        try
        {
            return read() is true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsStateName(string name) =>
        StateWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Type?> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void LogOnce(string message)
    {
        if (message == _lastLog) return;
        _lastLog = message;
        _log.Info(message);
    }
}
