namespace Undertaker.Graph.Reporting;

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
    public IEnumerable<DuplicateAssembly> Duplicates { get; }

    internal DuplicateAssemnblyReport(string assemblyName, Version version, IEnumerable<DuplicateAssembly> duplicates)
    {
        Assembly = assemblyName;
        Version = version;
        Duplicates = duplicates;
    }
}

public readonly struct DuplicateAssembly(string path, Version version)
{
    public string Path { get; } = path;
    public Version Version { get; } = version;
}
