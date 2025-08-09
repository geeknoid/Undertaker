namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of symbols defined in a specific assembly.
/// </summary>
public sealed class AliveReportAssembly
{
    /// <summary>
    /// Name of the aasembly where the symbol is defined.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of types within the assembly.
    /// </summary>
    public IReadOnlyList<AliveReportSymbol> AliveTypes { get; }

    /// <summary>
    /// The set of type members (methods, fields, etc) within the assemnbly.
    /// </summary>
    public IReadOnlyList<AliveReportSymbol> AliveMembers { get; }

    internal AliveReportAssembly(string assembly, IReadOnlyList<AliveReportSymbol> types, IReadOnlyList<AliveReportSymbol> members)
    {
        Assembly = assembly;
        AliveTypes = types;
        AliveMembers = members;
    }
}
