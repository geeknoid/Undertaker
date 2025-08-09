using System.Text.Json;

namespace Undertaker.Graph.Tests;

public class Tests
{
    [Fact]
    public void All()
    {
        var graph = new AssemblyGraph();
        graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");

        using (var exe = new LoadedAssembly("../../../../TestExe/bin/debug/net9.0/TestExe.dll"))
        {
            graph.MergeAssembly(exe);
        }

        using (var lib = new LoadedAssembly("../../../../TestExe/bin/debug/net9.0/TestLibrary.dll"))
        {
            graph.MergeAssembly(lib);
        }

        var reporter = graph.Done(x => { });
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var deadReport = JsonSerializer.Serialize(reporter.CollectDeadSymbols(), serializerOptions);
        var aliveReport = JsonSerializer.Serialize(reporter.CollectAliveSymbols(), serializerOptions);
        var aliveByTestReport = JsonSerializer.Serialize(reporter.CollectAliveByTestSymbols(), serializerOptions);
        var needlesslyPublicReport = JsonSerializer.Serialize(reporter.CollectNeedlesslyPublicSymbols(), serializerOptions);
        var unreferencedReport = JsonSerializer.Serialize(reporter.CollectUnreferencedAssemblies(), serializerOptions);
        var needlessIVTReport = JsonSerializer.Serialize(reporter.CollectNeedlessInternalsVisibleTo(), serializerOptions);
        var assemblyLayerCake = JsonSerializer.Serialize(reporter.CreateAssemblyLayerCake(), serializerOptions);
        var duplicateAssemblies = JsonSerializer.Serialize(reporter.CollectDuplicateAssemblies(), serializerOptions);
        var diagram = reporter.CreateDependencyDiagram();

        var sw = new StringWriter();
        reporter.Dump(sw);
        var graphDump = sw.ToString();

#if false
        // write the golden files
        File.WriteAllText("../../../Golden/dead.json", deadReport);
        File.WriteAllText("../../../Golden/alive.json", aliveReport);
        File.WriteAllText("../../../Golden/alive-by-test.json", aliveByTestReport);
        File.WriteAllText("../../../Golden/needlessly-public.json", needlesslyPublicReport);
        File.WriteAllText("../../../Golden/unreferenced.json", unreferencedReport);
        File.WriteAllText("../../../Golden/needless-ivt.json", needlessIVTReport);
        File.WriteAllText("../../../Golden/assembly-layer-cake.json", assemblyLayerCake);
        File.WriteAllText("../../../Golden/duplicate-assemblies.json", duplicateAssemblies);
        File.WriteAllText("../../../Golden/diagram.mmd", diagram);
        File.WriteAllText("../../../Golden/graph.txt", graphDump);
#else
        var goldenDeadReport = File.ReadAllText("../../../Golden/dead.json");
        var goldenAliveReport = File.ReadAllText("../../../Golden/alive.json");
        var goldenAliveByTestReport = File.ReadAllText("../../../Golden/alive-by-test.json");
        var goldenNeedlesslyPublicReport = File.ReadAllText("../../../Golden/needlessly-public.json");
        var goldenUnreferencedReport = File.ReadAllText("../../../Golden/unreferenced.json");
        var goldenNeedlessIVTReport = File.ReadAllText("../../../Golden/needless-ivt.json");
        var goldenAssemblyLayerCake = File.ReadAllText("../../../Golden/assembly-layer-cake.json");
        var goldenDuplicateAssemblies = File.ReadAllText("../../../Golden/duplicate-assemblies.json");
        var goldenDiagram = File.ReadAllText("../../../Golden/diagram.mmd");
        var goldenGraphDump = File.ReadAllText("../../../Golden/graph.txt");

        Assert.Equal(goldenDeadReport, deadReport);
        Assert.Equal(goldenAliveReport, aliveReport);
        Assert.Equal(goldenAliveByTestReport, aliveByTestReport);
        Assert.Equal(goldenNeedlesslyPublicReport, needlesslyPublicReport);
        Assert.Equal(goldenUnreferencedReport, unreferencedReport);
        Assert.Equal(goldenNeedlessIVTReport, needlessIVTReport);
        Assert.Equal(goldenAssemblyLayerCake, assemblyLayerCake);
        Assert.Equal(goldenDuplicateAssemblies, duplicateAssemblies);
        Assert.Equal(goldenDiagram, diagram);
        Assert.Equal(goldenGraphDump, graphDump);
#endif
    }
}
