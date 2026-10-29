using System.Globalization;
using System.Text;

namespace SplashIt.Core.Logging;

/// <summary>A log line's severity. The file shows it as <c>INF</c>, <c>WRN</c> or <c>ERR</c>.</summary>
public enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// Where Splash-It writes what it does. Callers log counts, kinds and type names only:
/// never a tab title, document title or file path (spec, Data Tenancy).
/// Implementations never throw.
/// </summary>
public interface ILog
{
    void Write(LogLevel level, string message);
}

public static class LogExtensions
{
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warn(this ILog log, string message) => log.Write(LogLevel.Warn, message);

    public static void Error(this ILog log, string message) => log.Write(LogLevel.Error, message);

    /// <summary>An <see cref="Action{T}"/> writing info lines, for the Core APIs that take one.</summary>
    public static Action<string> AsAction(this ILog log) => log.Info;
}

/// <summary>A log that drops everything.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    private NullLog()
    {
    }

    public void Write(LogLevel level, string message)
    {
    }
}

/// <summary>
/// The add-in's file log: one file per day, <c>splashit-&lt;yyyyMMdd&gt;.log</c>, in
/// <c>%LocalAppData%\SplashIt\logs</c>, keeping <see cref="KeepDays"/> days (today and the
/// six before it). Built-in .NET only (spec, Identity and layout: no Serilog).
/// <para>
/// Each line is <c>yyyy-MM-dd HH:mm:ss.fff LVL message</c>, local time, culture-invariant.
/// Line breaks in a message become spaces, so every entry is one line. Each write opens,
/// appends and closes the file with <c>FileShare.ReadWrite | FileShare.Delete</c>, so the
/// file can be read or deleted while Revit runs. Writing never throws: a failed write is
/// dropped. Thread-safe.
/// </para>
/// </summary>
public sealed class FileLog : ILog
{
    /// <summary>Days of logs kept, today included.</summary>
    public const int KeepDays = 7;

    private const string Prefix = "splashit-";
    private const string Extension = ".log";

    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private DateTime _prunedFor = DateTime.MinValue;

    /// <param name="folder">The log folder; created on the first write.</param>
    /// <param name="clock">Local time; <see cref="DateTime.Now"/> when null. Tests pass their own.</param>
    public FileLog(string folder, Func<DateTime>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary><c>%LocalAppData%\SplashIt\logs</c> for the current user.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplashIt", "logs");

    public string Folder { get; }

    /// <summary>The file for <paramref name="day"/>: <c>&lt;Folder&gt;\splashit-yyyyMMdd.log</c>.</summary>
    public string PathFor(DateTime day) =>
        Path.Combine(Folder, Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);

    public void Write(LogLevel level, string message)
    {
        try
        {
            var now = _clock();
            var line = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                       + " " + Code(level) + " " + OneLine(message) + Environment.NewLine;
            lock (_gate)
            {
                Directory.CreateDirectory(Folder);
                if (_prunedFor != now.Date)
                {
                    Prune(now.Date);
                    _prunedFor = now.Date;
                }
                using var stream = new FileStream(PathFor(now), FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception)
        {
            // A log must never take the add-in down; the line is dropped.
        }
    }

    /// <summary>
    /// Deletes <c>splashit-yyyyMMdd.log</c> files dated before the last <see cref="KeepDays"/>
    /// days (counting <paramref name="today"/>). Other files are never touched. Never throws.
    /// </summary>
    public void Prune(DateTime today)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var oldestKept = today.Date.AddDays(-(KeepDays - 1));
            foreach (var path in Directory.EnumerateFiles(Folder, Prefix + "*" + Extension))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length != Prefix.Length + 8) continue;
                if (!DateTime.TryParseExact(name.AsSpan(Prefix.Length), "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var day)) continue;
                if (day >= oldestKept) continue;
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    // Held open elsewhere: try again on the next day's first write.
                }
            }
        }
        catch (Exception)
        {
            // Pruning is best effort.
        }
    }

    private static string Code(LogLevel level) => level switch
    {
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "INF",
    };

    private static string OneLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        if (message.IndexOfAny(['\r', '\n']) < 0) return message;
        return message.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
    }
}
