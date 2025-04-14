namespace Undertaker.Graph;

/// <summary>
/// Information about an alive symbol.
/// </summary>
public sealed class SymbolReferences
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The name of the other symbols that have a dependence on this symbol.
    /// </summary>
    public IReadOnlyList<string> AliveBecause { get; }

    /// <summary>
    /// Gets a value indicating whether the symbol is considered a root.
    /// </summary>
    public bool Root { get; }

    internal SymbolReferences(string symbol, IReadOnlyList<string> aliveBecause, bool root)
    {
        Symbol = symbol;
        AliveBecause = aliveBecause;
        Root = root;
    }
}
