using System.Text.Json.Serialization;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed class AliveReportSymbol
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public SkinnyString Name { get; }

    /// <summary>
    /// The name of the other symbols that have a dependence on this symbol.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<SkinnyString>? ReferencedBy { get; }

    /// <summary>
    /// Gets a value indicating whether the symbol is considered a root.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Root { get; }

    internal AliveReportSymbol(SkinnyString symbol, IReadOnlyList<SkinnyString> dependents, bool root)
    {
        Name = symbol;

        if (dependents.Count > 0)
        { 
            ReferencedBy = dependents;
        }

        Root = root;
    }
}
