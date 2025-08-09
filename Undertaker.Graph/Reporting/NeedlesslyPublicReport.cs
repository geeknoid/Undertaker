namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of needlessly public symbols in the graph.
/// </summary>
public sealed class NeedlesslyPublicReport
{
    /// <summary>
    /// The set of assemblies where the symbols are defined.
    /// </summary>
    public IReadOnlyList<NeedlesslyPublicReportAssembly> Assemblies { get; }

    internal NeedlesslyPublicReport(IReadOnlyList<NeedlesslyPublicReportAssembly> assemblies)
    {
        Assemblies = assemblies;
    }
}
