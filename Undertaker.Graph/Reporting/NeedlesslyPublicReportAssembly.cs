namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures a set of needlessly public symbols defined in a specific assembly.
/// </summary>
public sealed class NeedlesslyPublicReportAssembly
{
    /// <summary>
    /// Name of the aasembly where the symbol is defined.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of needlessly public types within the assembly.
    /// </summary>
    public IReadOnlyList<string> NeedlesslyPublicTypes { get; }

    /// <summary>
    /// The set of needlessly public type members (methods, fields, etc) within the assemnbly.
    /// </summary>
    public IReadOnlyList<string> NeedlesslyPublicMembers { get; }

    internal NeedlesslyPublicReportAssembly(string assembly, IReadOnlyList<string> types, IReadOnlyList<string> members)
    {
        Assembly = assembly;
        NeedlesslyPublicTypes = types;
        NeedlesslyPublicMembers = members;
    }
}
