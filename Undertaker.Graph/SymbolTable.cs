namespace Undertaker.Graph;

internal readonly struct SymbolId(int index)
{
    public int Index { get; } = index;
}

internal sealed class SymbolTable
{
    private readonly List<Symbol> _symbols = [];

    public SymbolId AddSymbol(Assembly container, string name, SymbolKind symbolKind)
    {
        var id = new SymbolId(_symbols.Count);

        Symbol sym = symbolKind switch
        {
            SymbolKind.Method => new MethodSymbol(container, name, id),
            SymbolKind.Type => new TypeSymbol(container, name, id),
            SymbolKind.Field => new FieldSymbol(container, name, id),
            SymbolKind.Event => new EventSymbol(container, name, id),
            _ => new MiscSymbol(container, name, id)
        };

        _symbols.Add(sym);
        return id;
    }

    public Symbol GetSymbol(SymbolId id) => _symbols[id.Index];

    public void TrimExcess()
    {
        foreach (var sym in _symbols)
        {
            sym.TrimExcess();
        }
    }
}
