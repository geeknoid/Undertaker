namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of unreferenced symbols defined in a specific assembly.
/// </summary>
public sealed class UnreferencedReportAssembly
{
    /// <summary>
    /// Name of the aasembly where the symbol is defined.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of dead types within the assembly.
    /// </summary>
    public IReadOnlyList<UnreferencedReportSymbol> UnreferencedTypes { get; }

    /// <summary>
    /// The set of dead type members (methods, fields, etc) within the assemnbly.
    /// </summary>
    public IReadOnlyList<UnreferencedReportSymbol> UnreferencedMembers { get; }

    internal UnreferencedReportAssembly(string assembly, IReadOnlyList<UnreferencedReportSymbol> types, IReadOnlyList<UnreferencedReportSymbol> members)
    {
        Assembly = assembly;
        UnreferencedTypes = types;
        UnreferencedMembers = members;
    }
}
