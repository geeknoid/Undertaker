namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of symbols in the graph.
/// </summary>
public sealed class GraphReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<GraphReportAssembly> Assemblies { get; }

    internal GraphReport(IReadOnlyList<GraphReportAssembly> assemblies)
    {
        Assemblies = assemblies;
    }
}
