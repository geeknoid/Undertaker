namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool Root { get; } = root;
    public IReadOnlyCollection<Symbol> Symbols => _symbols.Values;
    public IReadOnlyCollection<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public bool Loaded { get; set; }

    private readonly Dictionary<Key, Symbol> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];

    private struct Key
    {
        public string Name;
        public SymbolKind Kind;
    }

    public Symbol GetSymbol(string name, SymbolKind symbolKind)
    {
        var key = new Key { Name = name, Kind = symbolKind };
        if (!_symbols.TryGetValue(key, out var sym))
        {
            sym = symbolKind switch
            {
                SymbolKind.Method => new MethodSymbol(this, name),
                SymbolKind.Type => new TypeSymbol(this, name),
                SymbolKind.Field => new FieldSymbol(this, name),
                SymbolKind.Event => new EventSymbol(this, name),
                _ => new MiscSymbol(this, name)
            };

            _symbols.Add(key, sym);
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
