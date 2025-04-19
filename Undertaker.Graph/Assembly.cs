namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool Root { get; } = root;
    public IReadOnlyDictionary<string, Symbol> Symbols => _symbols;
    public IReadOnlySet<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public bool Loaded { get; set; }

    private readonly Dictionary<string, Symbol> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];

    public Symbol GetSymbol(string name, SymbolKind symbolKind)
    {
        if (!_symbols.TryGetValue(name, out var sym))
        {
            sym = symbolKind switch
            {
                SymbolKind.Method => new MethodSymbol(this, name),
                SymbolKind.Type => new TypeSymbol(this, name),
                SymbolKind.Field => new FieldSymbol(this, name),
                _ => new MiscSymbol(this, name)
            };

            _symbols.Add(name, sym);
        }
        else if (sym.Kind != symbolKind)
        {
            throw new InvalidDataException($"Expected symbol {name} in assembly {Name} to be of kind {symbolKind}, but it is of kind {sym.Kind}");
        }

        return sym;
    }

    public void RecordInternalsVisibleTo(Assembly other)
    {
        _ = _internalsVisibleTo.Add(other);
    }

    public override int GetHashCode() => Name.GetHashCode();
    public override bool Equals(object? obj) => obj is Assembly asm && Name.Equals(asm.Name, StringComparison.Ordinal);
    public override string ToString() => Name;
}
