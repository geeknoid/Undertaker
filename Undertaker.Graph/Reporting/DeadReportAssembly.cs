namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of dead symbols defined in a specific assembly.
/// </summary>
public sealed class DeadReportAssembly
{
    /// <summary>
    /// Name of the aasembly where the symbol is defined.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of dead types within the assembly.
    /// </summary>
    public IReadOnlyList<string> DeadTypes { get; }

    /// <summary>
    /// The set of dead type members (methods, fields, etc) within the assemnbly.
    /// </summary>
    public IReadOnlyList<string> DeadMembers { get; }

    internal DeadReportAssembly(string assembly, IReadOnlyList<string> types, IReadOnlyList<string> members)
    {
        Assembly = assembly;
        DeadTypes = types;
        DeadMembers = members;
    }
}
