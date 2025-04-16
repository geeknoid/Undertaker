namespace Undertaker.Graph;

/// <summary>
/// Captures a set of symbols defined in a specific assembly.
/// </summary>
public sealed class GraphReportAssembly
{
    /// <summary>
    /// Name of the aasembly where the symbol is defined.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of types within the assembly.
    /// </summary>
    public IReadOnlyList<GraphReportSymbol> Types { get; }

    /// <summary>
    /// The set of type members (methods, fields, etc) within the assemnbly.
    /// </summary>
    public IReadOnlyList<GraphReportSymbol> Members { get; }

    internal GraphReportAssembly(string assembly, IReadOnlyList<GraphReportSymbol> types, IReadOnlyList<GraphReportSymbol> members)
    {
        Assembly = assembly;
        Types = types;
        Members = members;
    }
}
