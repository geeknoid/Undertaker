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
    /// Path of the assembly.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The set of other assembly files & version which were not loaded
    /// ies which have access to the internal symbols of the
    /// assembly, but don't need this access.
    /// </summary>
    public IEnumerable<DuplicateAssembly> Duplicates { get; }

    internal DuplicateAssemblyReport(string assemblyName, string path, IEnumerable<DuplicateAssembly> duplicates)
    {
        Assembly = assemblyName;
        Path = path;
        Duplicates = duplicates;
    }

    public int CompareTo(DuplicateAssemblyReport? other)
    {
        if (other is null)
        {
            return 1; // this instance is greater than null
        }

        return string.Compare(Assembly, other.Assembly, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return obj is DuplicateAssemblyReport other
            && string.Equals(Assembly, other.Assembly, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => Path.GetHashCode();
}

public readonly struct DuplicateAssembly(string path)
{
    public string Path { get; } = path;
}
