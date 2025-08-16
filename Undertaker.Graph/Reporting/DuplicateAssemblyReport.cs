using System.Diagnostics.CodeAnalysis;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Captures the set of duplicates found for a loaded assembly.
/// </summary>
[SuppressMessage("Design", "CA1036:Override methods on comparable types", Justification = "Superfluous")]
public class DuplicateAssemblyReport : IComparable<DuplicateAssemblyReport>
{
    /// <summary>
    /// The name of the assembly.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// The version of the assembly.
    /// </summary>
    public Version Version { get; }

    /// <summary>
    /// Path of the assembly.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The set of other assembly files & version which were not loaded
    /// ies which have access to the internal symbols of the
    /// assembly, but don't need this access.
    /// </summary>
    public IEnumerable<DuplicateAssembly> Duplicates { get; }

    internal DuplicateAssemblyReport(string assemblyName, Version version, string path, IEnumerable<DuplicateAssembly> duplicates)
    {
        Assembly = assemblyName;
        Version = version;
        Path = path;
        Duplicates = duplicates;
    }

    public int CompareTo(DuplicateAssemblyReport? other)
    {
        if (other is null)
        {
            return 1; // this instance is greater than null
        }

        int nameComparison = string.Compare(Assembly, other.Assembly, StringComparison.OrdinalIgnoreCase);
        return nameComparison != 0
            ? nameComparison
            : Version.CompareTo(other.Version);
    }

    public override bool Equals(object? obj)
    {
        return obj is DuplicateAssemblyReport other
            && string.Equals(Assembly, other.Assembly, StringComparison.OrdinalIgnoreCase)
            && Version.Equals(other.Version);
    }

    public override int GetHashCode() => HashCode.Combine(Assembly, Version);
}

public readonly struct DuplicateAssembly(string path, Version version)
{
    public string Path { get; } = path;
    public Version Version { get; } = version;
}
