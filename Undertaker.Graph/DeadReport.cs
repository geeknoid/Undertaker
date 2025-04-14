namespace Undertaker.Graph;

/// <summary>
/// Captures the set of symbols in the graph not reachable from the various roots.
/// </summary>
public sealed class DeadReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<DeadSymbols> Assemblies { get; }

    internal DeadReport(IReadOnlyList<DeadSymbols> assemblies)
    {
        Assemblies = assemblies;
    }
}
