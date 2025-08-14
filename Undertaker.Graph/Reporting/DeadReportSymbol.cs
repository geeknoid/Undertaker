using System.Text.Json.Serialization;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed class DeadReportSymbol
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The kind of symbol, e.g. "class", "method", "property".
    /// </summary>
    public string Kind { get; }

    internal DeadReportSymbol(string symbol, string kind)
    {
        Name = symbol;
        Kind = kind;
    }
}
