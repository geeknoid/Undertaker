namespace Undertaker.Graph;

/// <summary>
/// Captures the set of symbols from an assembly reachable from the various roots.
/// </summary>
public sealed class AliveSymbols
{
    /// <summary>
    /// Name of the aasembly.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of types reachable in the assembly.
    /// </summary>
    public IReadOnlyList<SymbolReferences> AliveTypes { get; }

    /// <summary>
    /// The set of type members (methods, fields, etc) reachable in the assemnbly.
    /// </summary>
    public IReadOnlyList<SymbolReferences> AliveMembers { get; }

    internal AliveSymbols(string assembly, IReadOnlyList<SymbolReferences> aliveTypes, IReadOnlyList<SymbolReferences> aliveMembers)
    {
        Assembly = assembly;
        AliveTypes = aliveTypes;
        AliveMembers = aliveMembers;
    }
}
