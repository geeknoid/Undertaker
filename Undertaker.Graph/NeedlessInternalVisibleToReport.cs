namespace Undertaker.Graph;

/// <summary>
/// Captures the set of unecessary InternalsVisibleTo attributes in an assembly.
/// </summary>
public class NeedlessInternalsVisibleToReport
{
    /// <summary>
    /// The name of the assembly.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The set of other assemblies which have access to the internal symbols of the
    /// assembly, but don't need this access.
    /// </summary>
    public IReadOnlyList<string> OtherAssemblies { get; }

    internal NeedlessInternalsVisibleToReport(string assemblyName, IReadOnlyList<string> otherAssemblies)
    {
        Assembly = assemblyName;
        OtherAssemblies = otherAssemblies;
    }
}
