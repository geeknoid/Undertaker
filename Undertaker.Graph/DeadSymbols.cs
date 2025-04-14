namespace Undertaker.Graph;

/// <summary>
/// Captures the set of symbols from an assembly not reachable from the various roots.
/// </summary>
public sealed class DeadSymbols
{
    /// <summary>
    /// Name of the aasembly.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of types not reachable in the assembly.
    /// </summary>
    public IReadOnlyList<string> DeadTypes { get; }

    /// <summary>
    /// The set of type members (methods, fields, etc) reachable in the assemnbly.
    /// </summary>
    public IReadOnlyList<string> DeadMembers { get; }

    internal DeadSymbols(string assembly, IReadOnlyList<string> deadTypes, IReadOnlyList<string> deadMembers)
    {
        Assembly = assembly;
        DeadTypes = deadTypes;
        DeadMembers = deadMembers;
    }
}
