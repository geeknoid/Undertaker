namespace Undertaker.Graph;

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed class GraphReportSymbol
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The name of the other symbols that have a dependence on this symbol.
    /// </summary>
    public IReadOnlyList<string> Dependents { get; }

    /// <summary>
    /// Gets a value indicating whether the symbol is considered a root.
    /// </summary>
    public bool Root { get; }

    internal GraphReportSymbol(string symbol, IReadOnlyList<string> dependents, bool root)
    {
        Symbol = symbol;
        Dependents = dependents;
        Root = root;
    }
}
