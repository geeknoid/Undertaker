namespace Undertaker.Graph;

/// <summary>
/// Returns statistics about the graph.
/// </summary>
public sealed class Stats
{
    public int AssembliesLoaded { get; internal set; }
    public int AssembliesLoadErrors { get; internal set; }
    public int DeadSymbolsDetected { get; internal set; }
    public int AliveSymbolsDetected { get; internal set; }
    public int DeclaredRootAssemblies { get; internal set; }
    public int DiscoveredMainEntrypoints { get; internal set; }
}
