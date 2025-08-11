using System.Text.Json.Serialization;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed class AliveReportSymbol
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The name of the other symbols that have a dependence on this symbol.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> ReferencedBy { get; }

    /// <summary>
    /// Gets a value indicating whether the symbol is considered a root.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Root { get; }

    internal AliveReportSymbol(string symbol, IReadOnlyList<string> dependents, bool root)
    {
        Name = symbol;
        ReferencedBy = dependents;
        Root = root;
    }
}
