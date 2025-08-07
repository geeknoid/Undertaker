namespace Undertaker.Graph;

/// <summary>
/// Captures the set of duplicates found for a loaded assembly.
/// </summary>
public class DuplicateAssemnblyReport
{
    /// <summary>
    /// The name of the assembly.
    /// </summary>
    public string Assembly { get; }

    public Version Version { get; }

    /// <summary>
    /// The set of other assembly files & version which were not loaded
    /// ies which have access to the internal symbols of the
    /// assembly, but don't need this access.
    /// </summary>
    public IReadOnlyList<(string, Version)> OtherAssemblies { get; }

    internal DuplicateAssemnblyReport(string assemblyName, Version version, IReadOnlyList<(string, Version)> otherAssemblies)
    {
        Assembly = assemblyName;
        Version = version;
        OtherAssemblies = otherAssemblies;
    }
}
