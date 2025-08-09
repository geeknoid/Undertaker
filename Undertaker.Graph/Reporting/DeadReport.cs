namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of dead symbols in the graph.
/// </summary>
public sealed class DeadReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<DeadReportAssembly> Assemblies { get; }

    internal DeadReport(IReadOnlyList<DeadReportAssembly> assemblies)
    {
        Assemblies = assemblies;
    }
}
