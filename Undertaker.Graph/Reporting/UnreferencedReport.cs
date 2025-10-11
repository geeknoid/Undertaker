namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of unreferenced symbols in the graph.
/// </summary>
public sealed class UnreferencedReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<UnreferencedReportAssembly> Assemblies { get; }

    internal UnreferencedReport(IReadOnlyList<UnreferencedReportAssembly> assemblies)
    {
        Assemblies = assemblies;
    }
}
