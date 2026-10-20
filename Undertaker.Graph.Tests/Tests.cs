using System.Text.Json;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Xunit.Abstractions;

namespace Undertaker.Graph.Tests;

public class Tests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("dead.json")]
    [InlineData("unreferenced.json")]
    [InlineData("alive.json")]
    [InlineData("alive-by-test.json")]
    [InlineData("needlessly-public.json")]
    [InlineData("unreferenced-assemblies.json")]
    [InlineData("needless-ivt.json")]
    [InlineData("assembly-layer-cake.json")]
    [InlineData("duplicate-assemblies.json")]
    [InlineData("diagram.mmd")]
    public void All(string report)
    {
        var graph = new AssemblyGraph();
        graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");

        var exePath = Path.Combine(AppContext.BaseDirectory, "TestExe.dll");
        var libPath = typeof(TestLibrary.AliveClass).Assembly.Location;
        using (var exe = LoadFixture(exePath))
        {
            graph.MergeAssembly(exe);
        }

        using (var lib = LoadFixture(libPath))
        {
            var record = lib.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(type =>
                type.ReflectionName == "TestLibrary.AliveRecordClass");
            var equatable = record.DirectBaseTypes.Single(type =>
                type.GetDefinition()?.ReflectionName == "System.IEquatable`1");
            Assert.NotEmpty(equatable.GetMethods());
            graph.MergeAssembly(lib);
        }

        var reporter = graph.Done(x => { });
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var actual = report switch
        {
            "dead.json" => JsonSerializer.Serialize(reporter.CollectDeadSymbols(), serializerOptions),
            "unreferenced.json" => JsonSerializer.Serialize(reporter.CollectUnreferencedSymbols(), serializerOptions),
            "alive.json" => JsonSerializer.Serialize(reporter.CollectAliveSymbols(), serializerOptions),
            "alive-by-test.json" => JsonSerializer.Serialize(reporter.CollectAliveByTestSymbols(), serializerOptions),
            "needlessly-public.json" => JsonSerializer.Serialize(reporter.CollectNeedlesslyPublicSymbols(), serializerOptions),
            "unreferenced-assemblies.json" => JsonSerializer.Serialize(reporter.CollectUnreferencedAssemblies(), serializerOptions),
            "needless-ivt.json" => JsonSerializer.Serialize(reporter.CollectNeedlessInternalsVisibleTo(), serializerOptions),
            "assembly-layer-cake.json" => JsonSerializer.Serialize(reporter.CreateAssemblyLayerCake(), serializerOptions),
            "duplicate-assemblies.json" => JsonSerializer.Serialize(reporter.CollectDuplicateAssemblies(), serializerOptions),
            "diagram.mmd" => reporter.CreateDependencyDiagram(),
            _ => throw new ArgumentOutOfRangeException(nameof(report)),
        };
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", report));
        if (!string.Equals(expected.ReplaceLineEndings(), actual.ReplaceLineEndings(), StringComparison.Ordinal))
        {
            output.WriteLine(actual);
        }

        Assert.Equal(expected.ReplaceLineEndings(), actual.ReplaceLineEndings());
    }

    private static LoadedAssembly LoadFixture(string path)
    {
        var resolver = new UniversalAssemblyResolver(path, throwOnError: false, ".NETCoreApp,Version=v10.0");
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        return new LoadedAssembly(path, resolver);
    }
}
