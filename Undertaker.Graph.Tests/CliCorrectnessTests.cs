using System.Globalization;
using System.Text.Json;

namespace Undertaker.Graph.Tests;

[CollectionDefinition("CLI", DisableParallelization = true)]
public sealed class CliTestGroup;

[Collection("CLI")]
public sealed class CliCorrectnessTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("Undertaker-cli-").FullName;

    [Theory]
    [InlineData("missing separator")]
    [InlineData(":Type")]
    [InlineData("Assembly:")]
    [InlineData(" : Type")]
    [InlineData(".dll:Type")]
    public async Task InvalidReflectionConfigurationFailsWithLocation(string record)
    {
        var config = Path.Combine(_directory, "reflection.txt");
        File.WriteAllText(config, "\n" + record);
        var (code, _, error) = await Run("-rs", config, "-gd", Path.Combine(_directory, "dumps"));
        Assert.Equal(1, code);
        Assert.Contains(config, error, StringComparison.Ordinal);
        Assert.Contains("Line 2", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_directory, "dumps")));
    }

    [Fact]
    public async Task ExplicitGraphDumpsDoNotEnableDefaultReports()
    {
        var (code, _, error) = await Run("-gd", Path.Combine(_directory, "dumps"));
        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Equal(["dumps"], Directory.EnumerateDirectories(_directory).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task DefaultsMatchReadmeAndOutputInventory()
    {
        var (code, _, error) = await Run();
        Assert.Equal(0, code);
        Assert.Empty(error);
        string[] directories =
        [
            "dead-symbols", "alive-symbols", "alive-by-test-symbols", "unreferenced-symbols",
            "needlessly-public-symbols", "needless-internals-visible-to"
        ];
        string[] files =
        [
            "unreferenced-assemblies.txt", "unanalyzed-assemblies.txt", "duplicate-assemblies",
            "assembly-layer-cake.json", "dependency-diagram.mmd"
        ];
        Assert.Equal(directories.Order(), Directory.EnumerateDirectories(_directory).Select(Path.GetFileName).Order());
        Assert.Equal(files.Order(), Directory.EnumerateFiles(_directory).Select(Path.GetFileName).Order());
        var readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "README.md"));
        foreach (var directory in directories)
        {
            Assert.Contains($"* `{directory}/`", readme, StringComparison.Ordinal);
        }

        foreach (var file in files)
        {
            Assert.Contains($"* `{file}`", readme, StringComparison.Ordinal);
        }

        using var duplicates = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "duplicate-assemblies")));
        Assert.Equal(JsonValueKind.Array, duplicates.RootElement.ValueKind);
        Assert.Equal(0, duplicates.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DuplicateMergesFollowInputOrderAndAreNotCountedAsLoaded()
    {
        var first = Directory.CreateDirectory(Path.Combine(_directory, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_directory, "second")).FullName;
        var original = typeof(TestLibrary.AliveClass).Assembly.Location;
        var firstPath = Path.Combine(first, "renamed.dll");
        File.Copy(original, firstPath);
        using (var stream = new FileStream(firstPath, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(16 * 1024 * 1024);
        }

        File.Copy(original, Path.Combine(second, "another-name.dll"));
        var reportPath = Path.Combine(_directory, "duplicates.json");
        var (code, output, error) = await Run(first, second, "-da", reportPath, "-v");
        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("loaded 1, duplicate(s) 1, not .NET 0, error(s) 0", output, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var entry = Assert.Single(report.RootElement.EnumerateArray());
        Assert.Equal("TestLibrary", entry.GetProperty("Assembly").GetString());
        Assert.Equal(firstPath, entry.GetProperty("Path").GetString());
        Assert.Single(entry.GetProperty("Duplicates").EnumerateArray());
    }

    [Fact]
    public async Task LoadErrorsNameTheFileWithoutVerboseOutput()
    {
        var input = Directory.CreateDirectory(Path.Combine(_directory, "input")).FullName;
        var path = Path.Combine(input, "locked.dll");
        File.Copy(typeof(TestLibrary.AliveClass).Assembly.Location, path);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var (code, _, error) = await Run(input, "-gd", Path.Combine(_directory, "dumps"));
        Assert.Equal(1, code);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.Contains("Unable to load", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_directory, "dumps")));
    }

    [Theory]
    [InlineData("-ds", true)]
    [InlineData("-urs", true)]
    [InlineData("-as", false)]
    [InlineData("-abts", false)]
    [InlineData("-nps", true)]
    [InlineData("-nivt", true)]
    [InlineData("-gd", false)]
    public async Task EveryPerAssemblyWriterRetiresReportsAfterInputsAreRemoved(string option, bool supportsCsv)
    {
        var input = Directory.CreateDirectory(Path.Combine(_directory, "input")).FullName;
        foreach (var assembly in new[]
        {
            typeof(AssemblyGraph).Assembly.Location,
            typeof(TestLibrary.AliveClass).Assembly.Location,
            Path.Combine(AppContext.BaseDirectory, "TestExe.dll")
        })
        {
            File.Copy(assembly, Path.Combine(input, Path.GetFileName(assembly)));
        }

        var output = Directory.CreateDirectory(Path.Combine(_directory, "reports")).FullName;
        var unrelated = Path.Combine(output, "notes.txt");
        File.WriteAllText(unrelated, "keep");
        var first = await Run(input, option, output);
        Assert.Equal(0, first.Code);
        Assert.Empty(first.Error);
        var reports = Directory.EnumerateFiles(output)
            .Where(path => !Path.GetFileName(path).StartsWith(".undertaker-", StringComparison.Ordinal) && path != unrelated)
            .ToArray();
        Assert.NotEmpty(reports);
        if (supportsCsv)
        {
            var csv = await Run(input, option, output, "-csv");
            Assert.Equal(0, csv.Code);
            Assert.Empty(csv.Error);
            Assert.All(reports, path => Assert.False(File.Exists(path)));
            Assert.NotEmpty(Directory.EnumerateFiles(output, "*.csv"));
        }

        var second = await Run(option, output);
        Assert.Equal(0, second.Code);
        Assert.Empty(second.Error);
        Assert.Equal([unrelated], Directory.EnumerateFiles(output)
            .Where(path => !Path.GetFileName(path).StartsWith(".undertaker-", StringComparison.Ordinal)));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public async Task CorrectBuiltinStaAndAcceptVerbsNamesPreserveTheirMethods()
    {
        var input = Directory.CreateDirectory(Path.Combine(_directory, "input")).FullName;
        var assembly = typeof(BuiltinMarkerFixture).Assembly;
        File.Copy(assembly.Location, Path.Combine(input, Path.GetFileName(assembly.Location)));
        var output = Path.Combine(_directory, "alive");
        var result = await Run(input, "-as", output);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Error);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, assembly.GetName().Name + ".json")));
        foreach (var name in new[] { "StaTestMethod", "ReflectionMethod" })
        {
            var member = Assert.Single(report.RootElement.GetProperty("AliveMembers").EnumerateArray(),
                entry => entry.GetProperty("Name").GetString()!.EndsWith($".{name}()", StringComparison.Ordinal));
            if (name == "StaTestMethod")
            {
                Assert.True(member.GetProperty("Root").GetBoolean());
            }
        }
    }

    private async Task<(int Code, string Output, string Error)> Run(params string[] args)
    {
        var previousDirectory = Environment.CurrentDirectory;
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Environment.CurrentDirectory = _directory;
            Console.SetOut(output);
            Console.SetError(error);
            var code = await Undertaker.Program.Main(args);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
