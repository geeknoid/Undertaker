namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of symbols in the graph.
/// </summary>
public sealed class AliveReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<AliveReportAssembly> Assemblies { get; }

    internal AliveReport(IReadOnlyList<AliveReportAssembly> assemblies)
    {
        Assemblies = assemblies;
    }
}
