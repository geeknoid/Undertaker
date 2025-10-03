namespace Undertaker.Graph;

internal readonly struct SymbolId(int index) : IComparable<SymbolId>
{
    public int Index { get; } = index;
    public int CompareTo(SymbolId other) => Index.CompareTo(other.Index);
    public override int GetHashCode() => Index.GetHashCode();
    public override bool Equals(object? obj) => obj is SymbolId other && Index == other.Index;
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
            SymbolKind.Property => new PropertySymbol(container, name, id),
            _ => new MiscSymbol(container, name, id)
        };

        _symbols.Add(sym);
        return id;
    }

    public Symbol GetSymbol(SymbolId id) => _symbols[id.Index];

    /// <summary>
    /// Makes an id point to a different existing symbol.
    /// </summary>
    public void Redirect(SymbolId id, Symbol sym) => _symbols[id.Index] = sym;

    public void TrimExcess()
    {
        foreach (var sym in _symbols)
        {
            sym.TrimExcess();
        }

        _symbols.TrimExcess();
    }
}
