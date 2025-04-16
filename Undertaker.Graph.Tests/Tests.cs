using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler;
using System.Text.Json;

namespace Undertaker.Graph.Tests;

public class Tests
{
    [Fact]
    public void All()
    {
        var graph = new AssemblyGraph();

        graph.LoadAssembly("../../../../TestExe/bin/debug/net9.0/TestExe.dll");
        graph.LoadAssembly("../../../../TestExe/bin/debug/net9.0/TestLibrary.dll");

        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var deadReport = JsonSerializer.Serialize(graph.CollectDeadReport(), serializerOptions);
        var aliveReport = JsonSerializer.Serialize(graph.CollectAliveReport(), serializerOptions);
        var needlesslyPublicReport = JsonSerializer.Serialize(graph.CollectNeedlesslyPublicReport(), serializerOptions);
        var assemblyLayerCake = JsonSerializer.Serialize(graph.CreateLayerCake(), serializerOptions);
        var graphDump = graph.ToString();

#if false
        // write the golden files
        File.WriteAllText("../../../Golden/dead.json", deadReport);
        File.WriteAllText("../../../Golden/alive.json", aliveReport);
        File.WriteAllText("../../../Golden/needlessly-public.json", needlesslyPublicReport);
        File.WriteAllText("../../../Golden/assembly-layer-cake.json", assemblyLayerCake);
        File.WriteAllText("../../../Golden/graph.txt", graphDump);
#else
        var goldenDeadReport = File.ReadAllText("../../../Golden/dead.json");
        var goldenAliveReport = File.ReadAllText("../../../Golden/alive.json");
        var goldenNeedlesslyPublicReport = File.ReadAllText("../../../Golden/needlessly-public.json");
        var goldenAssemblyLayerCake = File.ReadAllText("../../../Golden/assembly-layer-cake.json");
        var goldenGraphDump = File.ReadAllText("../../../Golden/graph.txt");

        Assert.Equal(goldenDeadReport, deadReport);
        Assert.Equal(goldenAliveReport, aliveReport);
        Assert.Equal(goldenNeedlesslyPublicReport, needlesslyPublicReport);
        Assert.Equal(goldenAssemblyLayerCake, assemblyLayerCake);
        Assert.Equal(goldenGraphDump, graphDump);
#endif
    }
}
