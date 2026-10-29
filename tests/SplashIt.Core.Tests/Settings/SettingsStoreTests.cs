using SplashIt.Core.Colors;
using SplashIt.Core.Settings;
using SplashIt.Core.Tests.Support;

namespace SplashIt.Core.Tests.Settings;

/// <summary>
/// AC1: settings round-trip; missing or corrupt → defaults with enabled = true;
/// out-of-range values rejected (Validate) or clamped (read from disk); atomic write.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly List<string> _log = [];

    public void Dispose() => _temp.Dispose();

    private static IReadOnlyList<Rgb> Colors(int count) =>
        Enumerable.Range(0, count).Select(i => new Rgb((byte)(i * 10), (byte)(255 - i * 10), 0x40)).ToArray();

    private static SplashItSettings Custom() => new()
    {
        Enabled = false,
        ProjectColors = [Rgb.Parse("#112233"), Rgb.Parse("#ABCDEF"), Rgb.Parse("#FF0000")],
        FamilyColors = [Rgb.Parse("#00FF00")],
        ProjectFillOpacity = 40,
        FamilyBarWidth = 6,
        FamilyBarOpacity = 10,
    };

    // ----------------------------------------------------------- round-trip

    [Fact]
    public void RoundTrip_WriteThenRead_GivesEqualSettings()
    {
        var path = _temp.File("settings.json");
        var original = Custom();

        original.Write(path);
        var read = SplashItSettings.Read(path, _log.Add);

        Assert.Equal(original, read);
        Assert.False(read.Enabled);
        Assert.Equal(["#112233", "#ABCDEF", "#FF0000"], read.ProjectColors.Select(c => c.ToHex()));
        Assert.Empty(_log); // a clean file reads without any fix-up
    }

    [Fact]
    public void RoundTrip_Defaults_GivesDefaults()
    {
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);
        Assert.Equal(SplashItSettings.Default, SplashItSettings.Read(path));
    }

    [Fact]
    public void RoundTrip_TwelveColorsEachList_IsKept()
    {
        var path = _temp.File("settings.json");
        var original = SplashItSettings.Default with { ProjectColors = Colors(12), FamilyColors = Colors(12) };
        original.Write(path);
        Assert.Equal(original, SplashItSettings.Read(path));
    }

    [Fact]
    public void Write_UsesTheDocumentedJsonShape()
    {
        var json = Custom().ToJson();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            ["schemaVersion", "enabled", "projectColors", "familyColors", "projectFillOpacity", "familyBarWidth", "familyBarOpacity"],
            names);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("#ABCDEF", doc.RootElement.GetProperty("projectColors")[1].GetString());
    }

    // ------------------------------------------------- missing and corrupt

    [Fact]
    public void Read_MissingFile_GivesDefaults_Enabled_AndLogsWhy()
    {
        var read = SplashItSettings.Read(_temp.File("nope.json"), _log.Add);

        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
        Assert.Single(_log);
        Assert.Contains("no settings file", _log[0]);
    }

    [Fact]
    public void Read_MissingFolder_GivesDefaults_Enabled()
    {
        var read = SplashItSettings.Read(Path.Combine(_temp.Path, "no", "such", "settings.json"));
        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("{\"enabled\": false,")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"settings\"")]
    [InlineData("42")]
    public void Read_CorruptFile_GivesDefaults_Enabled_AndLogsWhy(string contents)
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, contents);

        var read = SplashItSettings.Read(path, _log.Add);

        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
        Assert.Single(_log);
        Assert.Contains("using defaults", _log[0]);
    }

    [Fact]
    public void Read_BinaryGarbage_GivesDefaults_Enabled()
    {
        var path = _temp.File("settings.json");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x00, 0x13, 0x37, 0x00, 0x7B]);
        var read = SplashItSettings.Read(path, _log.Add);
        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
        Assert.NotEmpty(_log);
    }

    [Fact]
    public void Read_EmptyObject_GivesDefaults_Enabled()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, "{}");
        var read = SplashItSettings.Read(path, _log.Add);
        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
    }

    [Fact]
    public void Read_KeepsEnabledFalse_WhenOtherFieldsAreBroken()
    {
        // A user who turned coloring off stays off even if a value is damaged.
        var path = _temp.File("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "enabled": false, "familyBarWidth": "wide" }""");
        var read = SplashItSettings.Read(path, _log.Add);
        Assert.False(read.Enabled);
        Assert.Equal(SplashItDefaults.FamilyBarWidth, read.FamilyBarWidth);
    }

    [Fact]
    public void Read_NonBooleanEnabled_GivesEnabled()
    {
        var read = SplashItSettings.FromJson("""{ "schemaVersion": 1, "enabled": "no" }""", _log.Add);
        Assert.True(read.Enabled);
        Assert.Contains(_log, l => l.Contains("enabled"));
    }

    // ------------------------------------------------------ Validate rejects

    [Fact]
    public void Validate_Defaults_IsValid() => Assert.Empty(SplashItSettings.Default.Validate());

    [Fact]
    public void Validate_ZeroProjectColors_IsRejected()
    {
        var errors = (SplashItSettings.Default with { ProjectColors = [] }).Validate();
        Assert.Equal(["Project colors must have from 1 to 12 colors; there are 0."], errors);
    }

    [Fact]
    public void Validate_ThirteenProjectColors_IsRejected()
    {
        var errors = (SplashItSettings.Default with { ProjectColors = Colors(13) }).Validate();
        Assert.Equal(["Project colors must have from 1 to 12 colors; there are 13."], errors);
    }

    [Fact]
    public void Validate_ZeroFamilyColors_IsRejected()
    {
        var errors = (SplashItSettings.Default with { FamilyColors = [] }).Validate();
        Assert.Equal(["Family colors must have from 1 to 12 colors; there are 0."], errors);
    }

    [Fact]
    public void Validate_ThirteenFamilyColors_IsRejected()
    {
        var errors = (SplashItSettings.Default with { FamilyColors = Colors(13) }).Validate();
        Assert.Equal(["Family colors must have from 1 to 12 colors; there are 13."], errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Validate_OneAndTwelveColors_AreValid(int count)
    {
        var settings = SplashItSettings.Default with { ProjectColors = Colors(count), FamilyColors = Colors(count) };
        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(7)]
    [InlineData(100)]
    public void Validate_BadBarWidth_IsRejected(int width)
    {
        var errors = (SplashItSettings.Default with { FamilyBarWidth = width }).Validate();
        Assert.Equal([$"Family bar width must be from 1 to 6; it is {width}."], errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Validate_BarWidthInRange_IsValid(int width) =>
        Assert.Empty((SplashItSettings.Default with { FamilyBarWidth = width }).Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(101)]
    [InlineData(255)]
    public void Validate_BadOpacity_IsRejected(int opacity)
    {
        var fill = (SplashItSettings.Default with { ProjectFillOpacity = opacity }).Validate();
        var bar = (SplashItSettings.Default with { FamilyBarOpacity = opacity }).Validate();
        Assert.Equal([$"Project fill opacity must be from 10 to 100; it is {opacity}."], fill);
        Assert.Equal([$"Family bar opacity must be from 10 to 100; it is {opacity}."], bar);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(55)]
    [InlineData(100)]
    public void Validate_OpacityInRange_IsValid(int opacity) =>
        Assert.Empty((SplashItSettings.Default with { ProjectFillOpacity = opacity, FamilyBarOpacity = opacity }).Validate());

    [Fact]
    public void Validate_ListsEveryProblem_InFieldOrder()
    {
        var bad = new SplashItSettings
        {
            ProjectColors = Colors(13),
            FamilyColors = [],
            ProjectFillOpacity = 5,
            FamilyBarWidth = 9,
            FamilyBarOpacity = 101,
        };
        Assert.Equal(
        [
            "Project colors must have from 1 to 12 colors; there are 13.",
            "Family colors must have from 1 to 12 colors; there are 0.",
            "Project fill opacity must be from 10 to 100; it is 5.",
            "Family bar width must be from 1 to 6; it is 9.",
            "Family bar opacity must be from 10 to 100; it is 101.",
        ], bad.Validate());
    }

    [Fact]
    public void Write_InvalidSettings_IsRefused_AndLeavesTheFileUnchanged()
    {
        var path = _temp.File("settings.json");
        Custom().Write(path);
        var before = File.ReadAllText(path);

        var bad = Custom() with { ProjectColors = Colors(13), FamilyBarWidth = 0 };
        var ex = Assert.Throws<InvalidOperationException>(() => bad.Write(path));

        Assert.Contains("13", ex.Message);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    // ------------------------------------------------- clamped from disk

    [Theory]
    [InlineData("projectFillOpacity", "5", 10)]
    [InlineData("projectFillOpacity", "0", 10)]
    [InlineData("projectFillOpacity", "-40", 10)]
    [InlineData("projectFillOpacity", "150", 100)]
    [InlineData("projectFillOpacity", "55.4", 55)]
    [InlineData("projectFillOpacity", "1e9", 100)]
    [InlineData("familyBarOpacity", "9", 10)]
    [InlineData("familyBarOpacity", "101", 100)]
    [InlineData("familyBarWidth", "0", 1)]
    [InlineData("familyBarWidth", "-2", 1)]
    [InlineData("familyBarWidth", "7", 6)]
    [InlineData("familyBarWidth", "40", 6)]
    [InlineData("familyBarWidth", "2.5", 3)]
    public void Read_OutOfRangeNumber_IsClampedToTheNearestValidValue(string key, string raw, int expected)
    {
        var read = SplashItSettings.FromJson($$"""{ "schemaVersion": 1, "enabled": true, "{{key}}": {{raw}} }""", _log.Add);

        var actual = key switch
        {
            "projectFillOpacity" => read.ProjectFillOpacity,
            "familyBarOpacity" => read.FamilyBarOpacity,
            _ => read.FamilyBarWidth,
        };
        Assert.Equal(expected, actual);
        Assert.Empty(read.Validate());
        Assert.Contains(_log, l => l.Contains(key));
    }

    [Fact]
    public void Read_ThirteenColors_KeepsTheFirstTwelve()
    {
        var thirteen = Colors(13);
        var list = string.Join(",", thirteen.Select(c => "\"" + c.ToHex() + "\""));
        var json = $$"""{ "schemaVersion": 1, "projectColors": [{{list}}] }""";

        var read = SplashItSettings.FromJson(json, _log.Add);

        Assert.Equal(thirteen.Take(12), read.ProjectColors);
        Assert.Contains(_log, l => l.Contains("first 12"));
    }

    [Fact]
    public void Read_ZeroColors_FallsBackToTheDefaultList()
    {
        var read = SplashItSettings.FromJson("""{ "schemaVersion": 1, "projectColors": [], "familyColors": [] }""", _log.Add);
        Assert.Equal(SplashItDefaults.ProjectColors, read.ProjectColors);
        Assert.Equal(SplashItDefaults.FamilyColors, read.FamilyColors);
    }

    [Fact]
    public void Read_BadColorEntries_AreDropped()
    {
        var read = SplashItSettings.FromJson(
            """{ "schemaVersion": 1, "familyColors": ["#123456", "red", 7, "#12345", "#ABCDEF", null, " #111111"] }""",
            _log.Add);
        Assert.Equal(["#123456", "#ABCDEF"], read.FamilyColors.Select(c => c.ToHex()));
        Assert.Equal(5, _log.Count(l => l.Contains("dropped")));
    }

    [Fact]
    public void Read_EscapedLoneSurrogateColor_IsDropped_NotThrown()
    {
        // "\ud800" is legal JSON, but JsonElement.GetString() throws on it.
        var path = _temp.File("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "enabled": false, "projectColors": ["\ud800", "#123456"], "familyColors": ["\udfff"] }""");

        var read = SplashItSettings.Read(path, _log.Add);

        Assert.False(read.Enabled);
        Assert.Equal(["#123456"], read.ProjectColors.Select(c => c.ToHex()));
        Assert.Equal(SplashItDefaults.FamilyColors, read.FamilyColors);
        Assert.Contains(_log, l => l.Contains("projectColors entry 1") && l.Contains("dropped"));
    }

    [Fact]
    public void Read_OnlyEscapedLoneSurrogates_FallsBackToTheDefaultList()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, """{ "projectColors": ["\ud800"] }""");
        var read = SplashItSettings.Read(path, _log.Add);
        Assert.Equal(SplashItSettings.Default, read);
        Assert.True(read.Enabled);
    }

    [Fact]
    public void Read_OnlyBadColors_FallsBackToTheDefaultList()
    {
        var read = SplashItSettings.FromJson("""{ "schemaVersion": 1, "projectColors": ["nope"] }""");
        Assert.Equal(SplashItDefaults.ProjectColors, read.ProjectColors);
    }

    [Fact]
    public void Read_NewerSchema_ReadsTheKnownFields()
    {
        var read = SplashItSettings.FromJson(
            """{ "schemaVersion": 2, "enabled": false, "projectFillOpacity": 60, "titleRules": [] }""", _log.Add);
        Assert.False(read.Enabled);
        Assert.Equal(60, read.ProjectFillOpacity);
        Assert.Equal(1, read.SchemaVersion);
        Assert.Contains(_log, l => l.Contains("newer"));
    }

    // ------------------------------------------------------- atomic write

    [Fact]
    public void Write_ReplacesTheExistingFile_AndLeavesNoTempFile()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, "old contents, longer than the new file will be ......................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................................");

        Custom().Write(path);

        Assert.Equal(Custom(), SplashItSettings.Read(path));
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_CreatesTheFolder()
    {
        var path = Path.Combine(_temp.Path, "SplashIt", "settings.json");
        SplashItSettings.Default.Write(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_WhenTheTargetCannotBeReplaced_LeavesNoTempFile()
    {
        // The target path is a folder, so the final replace fails after the temp
        // file was written: the failure surfaces and the temp file is cleaned up.
        var path = _temp.File("settings.json");
        Directory.CreateDirectory(path);

        var ex = Record.Exception(() => SplashItSettings.Default.Write(path, _log.Add));
        Assert.True(ex is IOException or UnauthorizedAccessException, $"unexpected {ex}");

        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.True(Directory.Exists(path));

        // The replace was retried a bounded number of times, each retry logged.
        Assert.Equal(4, _log.Count(l => l.Contains("retrying")));
        Assert.Contains(_log, l => l.Contains("after 5 attempts"));
    }

    /// <summary>
    /// Reproduces Windows behavior on any OS: a rename over a target another handle has
    /// open is refused, and ReplaceFile may remove the target and then fail (1176).
    /// </summary>
    private sealed class WindowsLikeOps(bool moveAlwaysFails, bool replaceRemovesTargetThenFails) : AtomicFile.FileOps
    {
        public int Moves;
        public int Replaces;

        public override void Move(string source, string destination, bool overwrite)
        {
            Moves++;
            if (moveAlwaysFails) throw new IOException("simulated: move refused");
            if (overwrite && File.Exists(destination))
                throw new UnauthorizedAccessException("simulated: target held open by another handle");
            base.Move(source, destination, overwrite);
        }

        public override void Replace(string source, string destination)
        {
            Replaces++;
            if (!replaceRemovesTargetThenFails)
            {
                base.Replace(source, destination);
                return;
            }
            File.Delete(destination);
            throw new IOException("simulated: ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176)");
        }
    }

    private static IDisposable UseOps(AtomicFile.FileOps ops)
    {
        AtomicFile.OpsOverride.Value = ops;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => AtomicFile.OpsOverride.Value = null;
    }

    [Fact]
    public void Write_WhenTheMoveIsRefused_FallsBackToReplaceFile()
    {
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);
        var ops = new WindowsLikeOps(moveAlwaysFails: false, replaceRemovesTargetThenFails: false);

        using (UseOps(ops)) Custom().Write(path, _log.Add);

        Assert.Equal(Custom(), SplashItSettings.Read(path));
        Assert.Equal(1, ops.Moves);
        Assert.Equal(1, ops.Replaces);
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_WhenReplaceFileFailsAfterRemovingTheTarget_PutsTheNewFileInPlace()
    {
        // ERROR_UNABLE_TO_MOVE_REPLACEMENT: without the guard, settings.json would be gone
        // and the next Read would switch coloring back on for a user who turned it off.
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);
        var off = Custom(); // Enabled = false
        var ops = new WindowsLikeOps(moveAlwaysFails: false, replaceRemovesTargetThenFails: true);

        using (UseOps(ops)) off.Write(path, _log.Add);

        var read = SplashItSettings.Read(path);
        Assert.Equal(off, read);
        Assert.False(read.Enabled);
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
        Assert.Contains(_log, l => l.Contains("moved the new file into place"));
    }

    [Fact]
    public void Write_WhenEverySwapFailsAfterTheTargetIsGone_KeepsTheNewContents()
    {
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);
        var ops = new WindowsLikeOps(moveAlwaysFails: true, replaceRemovesTargetThenFails: true);

        Exception? ex;
        using (UseOps(ops)) ex = Record.Exception(() => Custom().Write(path, _log.Add));

        Assert.IsType<IOException>(ex);
        // Attempt 1: the move is refused, ReplaceFile removes the target and fails, the
        // rescue move fails. Attempts 2-5: a plain move into the free name, refused.
        Assert.Equal(1, ops.Replaces);
        Assert.Equal(6, ops.Moves);
        // The temp file is not deleted while settings.json is missing: the new contents survive.
        var kept = Assert.Single(Directory.GetFiles(_temp.Path));
        Assert.StartsWith(".settings.json.", Path.GetFileName(kept));
        Assert.Equal(Custom(), SplashItSettings.FromJson(File.ReadAllText(kept)));
        Assert.Contains(_log, l => l.Contains("its new contents are kept in"));
        Assert.DoesNotContain(_log, l => l.Contains("the previous file is kept"));
    }

    [Fact]
    public void Write_WhileAReaderHoldsTheFileOpenWithShareDelete_Succeeds()
    {
        // The documented way to read these files: FileShare.ReadWrite | FileShare.Delete.
        // Windows then lets the replace go through while the handle is open.
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);

        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Custom().Write(path, _log.Add);
        }

        Assert.Equal(Custom(), SplashItSettings.Read(path));
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_RetriesWhileAnotherProcessBrieflyLocksTheFile()
    {
        // A reader without FileShare.Delete (an antivirus scanner, a sync client) makes
        // Windows refuse the replace; the write retries and succeeds once it lets go.
        // Elsewhere the rename never waits on readers, so the first attempt succeeds.
        // The reader lets go when the write logs its first "retrying" (logged before the
        // wait), so the retry succeeds whatever the machine's load: no timer races the write.
        var path = _temp.File("settings.json");
        SplashItSettings.Default.Write(path);

        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Custom().Write(path, line =>
        {
            _log.Add(line);
            if (line.Contains("retrying", StringComparison.Ordinal)) held.Dispose();
        });

        Assert.Equal(Custom(), SplashItSettings.Read(path));
        Assert.Equal(["settings.json"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(_log, l => l.Contains("retrying"));
            Assert.Contains(_log, l => l.Contains("replaced settings.json on attempt"));
        }
    }

    [Fact]
    public void Read_WhileAWriterHoldsTheFileOpen_StillReads()
    {
        var path = _temp.File("settings.json");
        Custom().Write(path);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal(Custom(), SplashItSettings.Read(path, _log.Add));
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Write_ConcurrentReaderNeverSeesAPartialFile()
    {
        var path = _temp.File("settings.json");
        var a = Custom();
        var b = SplashItSettings.Default with { ProjectColors = Colors(12), FamilyColors = Colors(12), FamilyBarWidth = 1 };
        a.Write(path);

        using var stop = new CancellationTokenSource();
        var bad = 0;
        var reads = 0;
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    // Open as the real reader must, so Windows can replace the file meanwhile.
                    string text;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var streamReader = new StreamReader(stream))
                        text = streamReader.ReadToEnd();
                    Interlocked.Increment(ref reads);
                    var log = new List<string>();
                    var s = SplashItSettings.FromJson(text, log.Add);
                    if (log.Count > 0 || (s != a && s != b)) Interlocked.Increment(ref bad);
                }
                catch (IOException)
                {
                    // An open that loses a race with the rename is not a partial read.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        });

        // Start writing only once the reader is reading: under a busy test run the thread
        // pool can start the reader task after all 200 writes are done.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref reads) == 0 && waited.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(1);
        var readsBefore = Volatile.Read(ref reads);
        Assert.True(readsBefore > 0, "the reader never started");

        // Every write must succeed (on Windows too, with the reader holding the file).
        for (var i = 0; i < 200; i++) (i % 2 == 0 ? b : a).Write(path);
        var readsDuring = Volatile.Read(ref reads) - readsBefore;
        stop.Cancel();
        await reader;

        Assert.Equal(0, bad);
        Assert.True(readsDuring > 0, "no read overlapped the writes");
    }

    // --------------------------------------------------------------- misc

    [Fact]
    public void Lists_AreSnapshots_NotLiveViews()
    {
        var list = new List<Rgb> { Rgb.Parse("#010203") };
        var settings = SplashItSettings.Default with { ProjectColors = list };
        list.Add(Rgb.Parse("#040506"));
        Assert.Single(settings.ProjectColors);
    }

    [Fact]
    public void Equality_ComparesColorsByValue()
    {
        var a = SplashItSettings.Default with { ProjectColors = [Rgb.Parse("#010203")] };
        var b = SplashItSettings.Default with { ProjectColors = [Rgb.Parse("#010203")] };
        var c = SplashItSettings.Default with { ProjectColors = [Rgb.Parse("#010204")] };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void DefaultPath_IsUnderAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplashIt", "settings.json");
        Assert.Equal(expected, SplashItSettings.DefaultPath);
    }
}
