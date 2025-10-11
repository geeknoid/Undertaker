using System.Text.Json;

namespace Undertaker.Graph.Tests;

public class Tests
{
#if DEBUG
    [Fact]
    public void All()
    {
        var graph = new AssemblyGraph();
        graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");

#if DEBUG
        var exePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../../../TestExe/bin/debug/net9.0/TestExe.dll"));
        var libPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../../../TestExe/bin/debug/net9.0/TestLibrary.dll"));
#else
        var exePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../../../TestExe/bin/release/net9.0/TestExe.dll"));
        var libPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../../../TestExe/bin/release/net9.0/TestLibrary.dll"));
#endif
        using (var exe = new LoadedAssembly(exePath))
        {
            graph.MergeAssembly(exe);
        }

        using (var lib = new LoadedAssembly(libPath))
        {
            graph.MergeAssembly(lib);
        }

        var reporter = graph.Done(x => { });
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var deadReport = JsonSerializer.Serialize(reporter.CollectDeadSymbols(), serializerOptions);
        var unreferencedReport = JsonSerializer.Serialize(reporter.CollectUnreferencedSymbols(), serializerOptions);
        var aliveReport = JsonSerializer.Serialize(reporter.CollectAliveSymbols(), serializerOptions);
        var aliveByTestReport = JsonSerializer.Serialize(reporter.CollectAliveByTestSymbols(), serializerOptions);
        var needlesslyPublicReport = JsonSerializer.Serialize(reporter.CollectNeedlesslyPublicSymbols(), serializerOptions);
        var unreferencedAssembliesReport = JsonSerializer.Serialize(reporter.CollectUnreferencedAssemblies(), serializerOptions);
        var needlessIVTReport = JsonSerializer.Serialize(reporter.CollectNeedlessInternalsVisibleTo(), serializerOptions);
        var assemblyLayerCake = JsonSerializer.Serialize(reporter.CreateAssemblyLayerCake(), serializerOptions);
        var duplicateAssemblies = JsonSerializer.Serialize(reporter.CollectDuplicateAssemblies(), serializerOptions);
        var diagram = reporter.CreateDependencyDiagram();

#if false
        // write the golden files
        File.WriteAllText("../../../Golden/dead.json", deadReport);
        File.WriteAllText("../../../Golden/unreferenced.json", unreferencedReport);
        File.WriteAllText("../../../Golden/alive.json", aliveReport);
        File.WriteAllText("../../../Golden/alive-by-test.json", aliveByTestReport);
        File.WriteAllText("../../../Golden/needlessly-public.json", needlesslyPublicReport);
        File.WriteAllText("../../../Golden/unreferenced-assemblies.json", unreferencedAssembliesReport);
        File.WriteAllText("../../../Golden/needless-ivt.json", needlessIVTReport);
        File.WriteAllText("../../../Golden/assembly-layer-cake.json", assemblyLayerCake);
        File.WriteAllText("../../../Golden/duplicate-assemblies.json", duplicateAssemblies);
        File.WriteAllText("../../../Golden/diagram.mmd", diagram);
        reporter.Dump("../../../Golden/dumps");
#else
        var goldenDeadReport = File.ReadAllText("../../../Golden/dead.json");
        var goldenUnreferencedReport = File.ReadAllText("../../../Golden/unreferenced.json");
        var goldenAliveReport = File.ReadAllText("../../../Golden/alive.json");
        var goldenAliveByTestReport = File.ReadAllText("../../../Golden/alive-by-test.json");
        var goldenNeedlesslyPublicReport = File.ReadAllText("../../../Golden/needlessly-public.json");
        var goldenUnreferencedReport = File.ReadAllText("../../../Golden/unreferenced-assemblies.json");
        var goldenNeedlessIVTReport = File.ReadAllText("../../../Golden/needless-ivt.json");
        var goldenAssemblyLayerCake = File.ReadAllText("../../../Golden/assembly-layer-cake.json");
        var goldenDuplicateAssemblies = File.ReadAllText("../../../Golden/duplicate-assemblies.json");
        var goldenDiagram = File.ReadAllText("../../../Golden/diagram.mmd");

        goldenDeadReport = goldenDeadReport.ReplaceLineEndings();
        goldenUnreferencedReport = goldenUnreferencedReport.ReplaceLineEndings();
        goldenAliveReport = goldenAliveReport.ReplaceLineEndings();
        goldenAliveByTestReport = goldenAliveByTestReport.ReplaceLineEndings();
        goldenNeedlesslyPublicReport = goldenNeedlesslyPublicReport.ReplaceLineEndings();
        goldenUnreferencedReport = goldenUnreferencedReport.ReplaceLineEndings();
        goldenNeedlessIVTReport = goldenNeedlessIVTReport.ReplaceLineEndings();
        goldenAssemblyLayerCake = goldenAssemblyLayerCake.ReplaceLineEndings();
        goldenDiagram = goldenDiagram.ReplaceLineEndings();

        Assert.Equal(goldenDeadReport, deadReport);
        Assert.Equal(goldenUnreferencedReport, unreferencedReport);
        Assert.Equal(goldenAliveReport, aliveReport);
        Assert.Equal(goldenAliveByTestReport, aliveByTestReport);
        Assert.Equal(goldenNeedlesslyPublicReport, needlesslyPublicReport);
        Assert.Equal(goldenUnreferencedReport, unreferencedAssembliesReport);
        Assert.Equal(goldenNeedlessIVTReport, needlessIVTReport);
        Assert.Equal(goldenAssemblyLayerCake, assemblyLayerCake);
        Assert.Equal(goldenDuplicateAssemblies, duplicateAssemblies);
        Assert.Equal(goldenDiagram, diagram);
#endif
    }
#endif
}
