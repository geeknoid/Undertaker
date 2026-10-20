using System.Text;
using System.Text.Json;
using Undertaker.Graph.IO;

namespace Undertaker.Graph.Tests;

public sealed class ReportPersistenceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("Undertaker-reports-").FullName;

    [Theory]
    [InlineData("dead-symbols")]
    [InlineData("unreferenced-symbols")]
    [InlineData("alive-symbols")]
    [InlineData("alive-by-test-symbols")]
    [InlineData("needlessly-public-symbols")]
    [InlineData("needless-internals-visible-to")]
    [InlineData("graph-dumps")]
    public void SuccessfulRerunsRetireOnlyTheirObsoleteFiles(string family)
    {
        var unrelated = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(unrelated, "keep");
        var first = new ReportDirectory(_directory, family);
        first.Write("A", ".json", stream => stream.Write("old A"u8));
        first.Write("B", ".json", stream => stream.Write("old B"u8));
        first.Complete();

        var second = new ReportDirectory(_directory, family);
        second.Write("A", ".csv", stream => stream.Write("new A"u8));
        second.Complete();
        Assert.False(File.Exists(Path.Combine(_directory, "A.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "B.json")));
        Assert.Equal("new A", File.ReadAllText(Path.Combine(_directory, "A.csv")));
        Assert.Equal("keep", File.ReadAllText(unrelated));

        new ReportDirectory(_directory, family).Complete();
        Assert.False(File.Exists(Path.Combine(_directory, "A.csv")));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public void UnrelatedFamiliesKeepTheirOwnedFiles()
    {
        var first = new ReportDirectory(_directory, "alive-symbols");
        first.Write("A", ".json", stream => stream.Write("alive"u8));
        first.Complete();
        new ReportDirectory(_directory, "dead-symbols").Complete();
        Assert.Equal("alive", File.ReadAllText(Path.Combine(_directory, "A.json")));
    }

    [Fact]
    public void ModifiedObsoleteReportIsNotDeleted()
    {
        var first = new ReportDirectory(_directory, "dead-symbols");
        first.Write("A", ".json", stream => stream.Write("old"u8));
        first.Complete();
        var path = Path.Combine(_directory, "A.json");
        File.WriteAllText(path, "user edits");
        Assert.Throws<IOException>(() => new ReportDirectory(_directory, "dead-symbols").Complete());
        Assert.Equal("user edits", File.ReadAllText(path));
    }

    [Fact]
    public void ReportsCommittedBeforeLaterFailureRemainOwnedForRetryCleanup()
    {
        var output = new ReportDirectory(_directory, "dead-symbols");
        output.Write("A", ".json", stream => stream.Write("complete A"u8));
        Assert.Throws<IOException>(() => output.Write("B", ".json", stream =>
        {
            stream.Write("partial B"u8);
            throw new IOException("injected failure");
        }));
        Assert.Equal("complete A", File.ReadAllText(Path.Combine(_directory, "A.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "B.json")));
        new ReportDirectory(_directory, "dead-symbols").Complete();
        Assert.False(File.Exists(Path.Combine(_directory, "A.json")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/name")]
    [InlineData("nested\\name")]
    [InlineData("name:other")]
    [InlineData("/absolute")]
    [InlineData(".undertaker-graph-dumps")]
    public void MetadataCannotEscapeReportDirectory(string name)
    {
        var output = new ReportDirectory(_directory, "graph-dumps");
        Assert.Throws<InvalidDataException>(() => output.Write(name, ".txt", stream => stream.WriteByte(1)));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void ManifestEntriesCannotEscapeReportDirectory()
    {
        File.WriteAllText(Path.Combine(_directory, ".undertaker-graph-dumps.json"),
            "{\"../outside.txt\":\"invalid\"}");
        Assert.Throws<InvalidDataException>(() => new ReportDirectory(_directory, "graph-dumps"));
    }

    [Fact]
    public void UnloadedMetadataIdentitiesCannotWriteOutsideDumpDirectory()
    {
        var sentinel = Path.Combine(_directory, "sentinel.txt");
        File.WriteAllText(sentinel, "keep");
        var graph = new AssemblyGraph();
        graph.GetAssembly("../sentinel");
        graph.GetAssembly(Path.Combine(_directory, "sentinel"));
        graph.GetAssembly("..\\sentinel");
        var output = Path.Combine(_directory, "dumps");
        graph.Done(_ => { }).Dump(output);
        Assert.Equal("keep", File.ReadAllText(sentinel));
        Assert.DoesNotContain(Directory.EnumerateFiles(output), path =>
            !Path.GetFileName(path).StartsWith(".undertaker-", StringComparison.Ordinal));
    }

    [Fact]
    public void CollidingNamesDoNotOverwriteAnAlreadyEmittedReport()
    {
        var output = new ReportDirectory(_directory, "dead-symbols");
        output.Write("A", ".json", stream => stream.Write("first"u8));
        Assert.Throws<InvalidDataException>(() => output.Write("A", ".json", stream => stream.Write("second"u8)));
        if (File.Exists(Path.Combine(_directory, "a.json")))
        {
            Assert.Throws<InvalidDataException>(() => output.Write("a", ".json", stream => stream.Write("second"u8)));
        }

        Assert.Equal("first", File.ReadAllText(Path.Combine(_directory, "A.json")));
    }

    [Theory]
    [InlineData("old")]
    [InlineData("new")]
    public void CaseOnlyRenameKeepsTheNewReport(string content)
    {
        var first = new ReportDirectory(_directory, "dead-symbols");
        first.Write("A", ".csv", stream => stream.Write("old"u8));
        first.Complete();
        var second = new ReportDirectory(_directory, "dead-symbols");
        second.Write("a", ".csv", stream => stream.Write(Encoding.UTF8.GetBytes(content)));
        second.Complete();

        Assert.Equal(content, File.ReadAllText(Path.Combine(_directory, "a.csv")));
        Assert.Single(Directory.EnumerateFiles(_directory, "*.csv"));
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(_directory, ".undertaker-dead-symbols.json")))!;
        Assert.Equal("a.csv", Assert.Single(manifest).Key);
    }

    [Fact]
    public void CaseInsensitiveInventoryRejectsCollidingCurrentNames()
    {
        var output = new ReportDirectory(_directory, "dead-symbols", StringComparer.OrdinalIgnoreCase);
        output.Write("A", ".json", stream => stream.Write("first"u8));

        Assert.Throws<InvalidDataException>(() => output.Write("a", ".json", stream => stream.Write("second"u8)));
        Assert.Equal("first", File.ReadAllText(Path.Combine(_directory, "A.json")));
    }

    [Fact]
    public void CaseInsensitiveRecoveryAppliesTheLatestHashToThePriorFilename()
    {
        var first = new ReportDirectory(_directory, "dead-symbols");
        first.Write("A", ".json", stream => stream.Write("old"u8));
        first.Complete();
        var second = new ReportDirectory(_directory, "dead-symbols");
        second.Write("A", ".json", stream => stream.Write("new"u8));

        // Emulate the alternate casing recorded by a case-insensitive filesystem.
        var journalPath = Path.Combine(_directory, ".undertaker-dead-symbols.jsonl");
        var entry = JsonSerializer.Deserialize<KeyValuePair<string, string>>(Assert.Single(File.ReadAllLines(journalPath)));
        File.WriteAllText(journalPath,
            JsonSerializer.Serialize(new KeyValuePair<string, string>("a.json", entry.Value)) + "\n");

        new ReportDirectory(_directory, "dead-symbols", StringComparer.OrdinalIgnoreCase).Complete();

        Assert.False(File.Exists(Path.Combine(_directory, "A.json")));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public void ManifestCannotContainCaseEquivalentFilenamesOnInsensitiveDestinations()
    {
        File.WriteAllText(Path.Combine(_directory, ".undertaker-dead-symbols.json"),
            "{\"A.json\":\"first\",\"a.json\":\"second\"}");

        Assert.Throws<InvalidDataException>(() =>
            new ReportDirectory(_directory, "dead-symbols", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void OwnershipMetadataGrowsLinearlyAndIsConsolidatedOnlyAtCompletion()
    {
        var first = new ReportDirectory(_directory, "dead-symbols");
        first.Write("prior", ".json", stream => stream.WriteByte(1));
        first.Complete();
        var manifestPath = Path.Combine(_directory, ".undertaker-dead-symbols.json");
        var priorManifest = File.ReadAllText(manifestPath);
        var journalPath = Path.Combine(_directory, ".undertaker-dead-symbols.jsonl");
        var second = new ReportDirectory(_directory, "dead-symbols");
        for (var i = 0; i < 32; i++)
        {
            second.Write($"A{i:D4}", ".json", stream => stream.WriteByte(1));
        }

        var halfLength = new FileInfo(journalPath).Length;
        for (var i = 32; i < 64; i++)
        {
            second.Write($"A{i:D4}", ".json", stream => stream.WriteByte(1));
        }

        Assert.Equal(2 * halfLength, new FileInfo(journalPath).Length);
        Assert.Equal(64, File.ReadLines(journalPath).Count());
        Assert.Equal(priorManifest, File.ReadAllText(manifestPath));
        second.Complete();
        Assert.False(File.Exists(journalPath));
        Assert.Equal(64, JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath))!.Count);
        Assert.False(File.Exists(Path.Combine(_directory, "prior.json")));
    }

    [Fact]
    public void RecoveryReplaysAllUpdatesAndPreservesModifiedReports()
    {
        var first = new ReportDirectory(_directory, "dead-symbols");
        first.Write("A", ".json", stream => stream.Write("old A"u8));
        first.Complete();
        var second = new ReportDirectory(_directory, "dead-symbols");
        second.Write("A", ".json", stream => stream.Write("new A"u8));
        second.Write("B", ".json", stream => stream.Write("new B"u8));
        File.WriteAllText(Path.Combine(_directory, "A.json"), "user edits");

        Assert.Throws<IOException>(() => new ReportDirectory(_directory, "dead-symbols").Complete());
        Assert.Equal("user edits", File.ReadAllText(Path.Combine(_directory, "A.json")));
        Assert.Equal("new B", File.ReadAllText(Path.Combine(_directory, "B.json")));
    }

    [Fact]
    public void RecoveryAfterManifestCompactionBeforeJournalRemovalIsIdempotent()
    {
        var output = new ReportDirectory(_directory, "dead-symbols");
        output.Write("A", ".json", stream => stream.Write("old"u8));
        output.Write("B", ".json", stream => stream.Write("new"u8));
        var journalPath = Path.Combine(_directory, ".undertaker-dead-symbols.jsonl");
        var retry = new ReportDirectory(_directory, "dead-symbols");
        retry.Write("B", ".json", stream => stream.Write("replacement"u8));
        var journal = File.ReadAllText(journalPath);
        retry.Complete();
        File.WriteAllText(journalPath, journal);

        var recovered = new ReportDirectory(_directory, "dead-symbols");
        recovered.Write("B", ".json", stream => stream.Write("final"u8));
        recovered.Complete();

        Assert.False(File.Exists(Path.Combine(_directory, "A.json")));
        Assert.Equal("final", File.ReadAllText(Path.Combine(_directory, "B.json")));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public void JournalEntriesCannotEscapeReportDirectory()
    {
        File.WriteAllText(Path.Combine(_directory, ".undertaker-dead-symbols.jsonl"),
            "{\"Key\":\"../outside.txt\",\"Value\":\"invalid\"}\n");

        Assert.Throws<InvalidDataException>(() => new ReportDirectory(_directory, "dead-symbols"));
    }

    [Fact]
    public void WriteFailurePreservesPriorReportAndRemovesTemporaryFile()
    {
        var path = PriorReport();
        Assert.Throws<IOException>(() => ReportFile.Write(path, stream =>
        {
            stream.Write("partial"u8);
            throw new IOException("injected write failure");
        }));
        AssertPriorReport(path);
    }

    [Fact]
    public void SerializationFailurePreservesPriorReportAndRemovesTemporaryFile()
    {
        var path = PriorReport();
        var unsupported = new object[] { 1, (Action)(() => { }) };
        Assert.Throws<NotSupportedException>(() => ReportFile.WriteJson(path, unsupported, JsonSerializerOptions.Default));
        AssertPriorReport(path);
    }

    [Fact]
    public void FlushFailurePreservesPriorReportAndRemovesTemporaryFile()
    {
        var path = PriorReport();
        Assert.Throws<ObjectDisposedException>(() => ReportFile.Write(path, stream =>
        {
            stream.Write("partial"u8);
            stream.Dispose();
        }));
        AssertPriorReport(path);
    }

    [Fact]
    public void TextWriterFailurePreservesPriorReport()
    {
        var path = PriorReport();
        Assert.Throws<IOException>(() => ReportFile.WriteText(path, writer =>
        {
            writer.Write("partial");
            throw new IOException("injected text failure");
        }));
        AssertPriorReport(path);
    }

    [Fact]
    public void SuccessfulWriteCommitsClosedCompleteFile()
    {
        var path = PriorReport();
        ReportFile.WriteText(path, writer => writer.Write("complete \u00e9"));
        Assert.Equal("complete \u00e9", File.ReadAllText(path, Encoding.UTF8));
        Assert.Single(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void ReplacingAReportPreservesItsUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = PriorReport();
        var permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, permissions);
        ReportFile.WriteText(path, writer => writer.Write("replacement"));
        Assert.Equal(permissions, File.GetUnixFileMode(path));
        Assert.Equal("replacement", File.ReadAllText(path));
    }

    private string PriorReport()
    {
        var path = Path.Combine(_directory, "report.json");
        File.WriteAllText(path, "prior valid report");
        return path;
    }

    private void AssertPriorReport(string path)
    {
        Assert.Equal("prior valid report", File.ReadAllText(path));
        Assert.Single(Directory.EnumerateFiles(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
