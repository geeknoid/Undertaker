namespace Undertaker.Graph;

/// <summary>
/// Captures the set of symbols in the graph reachable from the various roots.
/// </summary>
public sealed class AliveReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<AliveSymbols> Assemblies { get; }

    internal AliveReport(IReadOnlyList<AliveSymbols> assemblies)
    {
        Assemblies = assemblies;
    }
}
