// NOT YET USED

namespace Undertaker.Graph;

internal readonly struct SymbolId(int index)
{
    public int Index { get; } = index;
}

internal sealed class SymbolTable
{
    private readonly List<Symbol> _symbols = [];

    public SymbolId AddSymbol(Symbol symbol)
    {
        _symbols.Add(symbol);
        return new SymbolId(_symbols.Count - 1);
    }

    public Symbol GetSymbol(SymbolId id) => _symbols[id.Index];
}
