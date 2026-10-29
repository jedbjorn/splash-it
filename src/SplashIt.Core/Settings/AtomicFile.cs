using System.Text;

namespace SplashIt.Core.Settings;

/// <summary>
/// Writes a file so a reader never sees it half-written: the text goes to a temp
/// file in the same folder, is flushed to disk, then replaces the target in one
/// step (<see cref="File.Replace(string, string, string?)"/> when the target exists,
/// else <see cref="File.Move(string, string, bool)"/>). A failure at any point leaves
/// the previous file untouched and no temp file behind.
/// <para>
/// On Windows the replace is refused while another process (a reader without
/// <see cref="FileShare.Delete"/>, an antivirus scanner, a sync client) holds the target
/// open, so it is retried a few times, briefly, before the error surfaces. Readers of
/// files written here should open them with <c>FileShare.ReadWrite | FileShare.Delete</c>.
/// </para>
/// </summary>
internal static class AtomicFile
{
    /// <summary>How many times the final replace is tried.</summary>
    internal const int ReplaceAttempts = 5;

    /// <summary>The wait before the first retry; it doubles each time (about 0.3 s in all).</summary>
    internal const int FirstRetryDelayMs = 20;

    public static void WriteAllText(string path, string contents, Action<string>? log = null)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"'{path}' has no parent folder.", nameof(path));
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            ReplaceWithRetry(temp, full, log);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Reads a file written by <see cref="WriteAllText"/> without blocking a concurrent
    /// replace: it opens with <c>FileShare.ReadWrite | FileShare.Delete</c>, and retries a
    /// sharing failure briefly (a writer on Windows may hold the name for an instant).
    /// Throws what the last attempt threw.
    /// </summary>
    public static string ReadAllText(string path)
    {
        var delay = FirstRetryDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                                       && ex is not FileNotFoundException and not DirectoryNotFoundException
                                       && attempt < ReplaceAttempts)
            {
                Thread.Sleep(delay);
                delay *= 2;
            }
        }
    }

    private static void ReplaceWithRetry(string temp, string target, Action<string>? log)
    {
        var delay = FirstRetryDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(target)) File.Replace(temp, target, destinationBackupFileName: null);
                else File.Move(temp, target, overwrite: true);
                if (attempt > 1)
                    log?.Invoke(FormattableString.Invariant($"settings: replaced {Path.GetFileName(target)} on attempt {attempt}."));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= ReplaceAttempts)
                {
                    log?.Invoke(FormattableString.Invariant(
                        $"settings: replacing {Path.GetFileName(target)} failed ({ex.GetType().Name}) after {ReplaceAttempts} attempts; the previous file is kept."));
                    throw;
                }
                log?.Invoke(FormattableString.Invariant(
                    $"settings: replacing {Path.GetFileName(target)} failed ({ex.GetType().Name}), attempt {attempt} of {ReplaceAttempts}; retrying in {delay} ms."));
                Thread.Sleep(delay);
                delay *= 2;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover dot-prefixed temp file is harmless.
        }
    }
}
