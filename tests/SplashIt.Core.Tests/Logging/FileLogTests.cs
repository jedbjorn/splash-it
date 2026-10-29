using SplashIt.Core.Logging;
using SplashIt.Core.Tests.Support;

namespace SplashIt.Core.Tests.Logging;

public class FileLogTests
{
    private static readonly DateTime Day = new(2026, 10, 29, 14, 5, 6, 789);

    [Fact]
    public void Write_AppendsOneLinePerEntry_ToTodaysFile()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Path, "logs");
        var log = new FileLog(folder, () => Day);

        log.Info("colorizer: started in 3 ms");
        log.Warn("two\r\nlines\nhere");
        log.Error("boom");

        var path = Path.Combine(folder, "splashit-20261029.log");
        Assert.Equal(path, log.PathFor(Day));
        var lines = File.ReadAllLines(path);
        Assert.Equal(
        [
            "2026-10-29 14:05:06.789 INF colorizer: started in 3 ms",
            "2026-10-29 14:05:06.789 WRN two lines here",
            "2026-10-29 14:05:06.789 ERR boom",
        ], lines);
    }

    [Fact]
    public void Write_KeepsSevenDays_AndLeavesOtherFilesAlone()
    {
        using var temp = new TempFolder();
        foreach (var name in new[]
                 {
                     "splashit-20261020.log", "splashit-20261022.log", "splashit-20261023.log",
                     "splashit-20261028.log", "splashit-notadate.log", "other.log", "splashit-20261020.txt",
                 })
            File.WriteAllText(temp.File(name), "x");

        new FileLog(temp.Path, () => Day).Info("hello");

        var left = Directory.GetFiles(temp.Path).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
        [
            "other.log",
            "splashit-20261020.txt",
            "splashit-20261023.log", // 29 Oct minus 6 days: the oldest kept
            "splashit-20261028.log",
            "splashit-20261029.log",
            "splashit-notadate.log",
        ], left);
    }

    [Fact]
    public void Write_RollsOverAtMidnight_AndPrunesForTheNewDay()
    {
        using var temp = new TempFolder();
        var now = new DateTime(2026, 10, 29, 23, 59, 59);
        var log = new FileLog(temp.Path, () => now);
        File.WriteAllText(temp.File("splashit-20261023.log"), "x");

        log.Info("before");
        Assert.True(File.Exists(temp.File("splashit-20261023.log")));

        now = new DateTime(2026, 10, 30, 0, 0, 1);
        log.Info("after");

        Assert.False(File.Exists(temp.File("splashit-20261023.log")));
        Assert.Equal(["2026-10-29 23:59:59.000 INF before"], File.ReadAllLines(temp.File("splashit-20261029.log")));
        Assert.Equal(["2026-10-30 00:00:01.000 INF after"], File.ReadAllLines(temp.File("splashit-20261030.log")));
    }

    [Fact]
    public void Write_NeverThrows_WhenTheFolderCannotBeCreated()
    {
        using var temp = new TempFolder();
        var blocker = temp.File("blocker");
        File.WriteAllText(blocker, "a file where the folder should be");
        var log = new FileLog(Path.Combine(blocker, "logs"), () => Day);

        log.Error("dropped");
    }

    [Fact]
    public void Write_FromManyThreads_KeepsEveryLineWhole()
    {
        using var temp = new TempFolder();
        var log = new FileLog(temp.Path, () => Day);

        Parallel.For(0, 200, i => log.Info($"line {i:D3}"));

        var lines = File.ReadAllLines(log.PathFor(Day));
        Assert.Equal(200, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^2026-10-29 14:05:06\.789 INF line \d{3}$", l));
    }

    [Fact]
    public void NullLog_DropsEverything()
    {
        NullLog.Instance.Error("nothing");
        NullLog.Instance.AsAction()("nothing");
    }
}
