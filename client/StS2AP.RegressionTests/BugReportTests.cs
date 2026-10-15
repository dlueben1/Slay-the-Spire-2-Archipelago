using System.IO.Compression;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class BugReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StS2AP-report-tests-" + Guid.NewGuid().ToString("N"));

    public BugReportTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void NewestReportAndBothReplicaWarningsAreExportedWithoutChangingSources()
    {
        string older = CreateBundle("old", "old report");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string newest = CreateBundle("new", "new report");
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        byte[] originalBytes = File.ReadAllBytes(newest);
        string gameLog = Path.Combine(_directory, "godot.log");
        File.WriteAllText(gameLog, "full game log\nINFO: earlier context\n");
        using var liveWriter = new FileStream(gameLog, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        var result = ApBugReportFiles.Create(_directory);
        using var archive = ZipFile.OpenRead(result.Path);
        Assert.Equal(originalBytes, File.ReadAllBytes(newest));
        Assert.Equal(File.GetLastWriteTimeUtc(newest), result.DivergenceTimeUtc);
        Assert.Equal("new report", Read(archive, "divergence/state-divergence-report.txt"));
        Assert.Contains("earlier context", Read(archive, "game-logs/godot.log"));
        string warnings = Read(archive, "warnings-errors.txt");
        Assert.Contains("local-debug-log.records.json", warnings);
        Assert.Contains("remote-debug-log.records.json", warnings);
        Assert.Contains("failure\n   at Example.Stack()", warnings);
        Assert.DoesNotContain("ordinary info", warnings);
        Assert.Contains("ordinary info", Read(archive, "divergence/local-debug-log.records.json"));
        Assert.Contains(Path.GetFileName(newest), Read(archive, "README.txt"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(result.Path)!, "*.partial"));
    }

    [Fact]
    public void MissingReportDoesNotPublishAnEmptyZip()
    {
        Assert.Throws<FileNotFoundException>(() => ApBugReportFiles.Create(_directory));
        Assert.False(Directory.Exists(Path.Combine(_directory, "ArchipelagoReports")));
    }

    [Fact]
    public void BrokenOptionalLogJsonIsPreservedAndExplained()
    {
        string source = CreateBundle("broken-log", "valid state report");
        using (var original = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            original.GetEntry("remote-debug-log.records.json")!.Delete();
            Write(original, "remote-debug-log.records.json", "not json");
        }
        using var exported = ZipFile.OpenRead(ApBugReportFiles.Create(_directory).Path);
        Assert.Equal("not json", Read(exported, "divergence/remote-debug-log.records.json"));
        Assert.Contains("Warning extraction unavailable for remote", Read(exported, "README.txt"));
        Assert.Contains("failure", Read(exported, "warnings-errors.txt"));
    }

    [Fact]
    public void BrokenZipDoesNotLeaveAnExportOrPartialFile()
    {
        File.WriteAllText(Path.Combine(_directory, "ritsulib_state_divergence_broken.zip"), "not a ZIP");
        Assert.Throws<InvalidDataException>(() => ApBugReportFiles.Create(_directory));
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "ArchipelagoReports")));
    }

    private string CreateBundle(string name, string report)
    {
        string path = Path.Combine(_directory, $"ritsulib_state_divergence_{name}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "state-divergence-report.txt", report);
        Write(archive, "metadata.json", "{\"ritsuLibVersion\":\"test\"}");
        const string logs = """
            [{"timestamp":"2026-02-01T00:00:00Z","severityNumber":9,"severityText":"INFO","body":"ordinary info"},
             {"timestamp":"2026-02-01T00:01:00Z","severityNumber":13,"severityText":"WARN","body":"warning"},
             {"timestamp":"2026-02-01T00:02:00Z","severityNumber":17,"severityText":"ERROR","body":"failure\n   at Example.Stack()"}]
            """;
        Write(archive, "local-debug-log.records.json", logs);
        Write(archive, "remote-debug-log.records.json", logs);
        return path;
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
