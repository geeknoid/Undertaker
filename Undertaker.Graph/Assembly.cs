namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool Root { get; } = root;
    public bool Loaded { get; set; }
    public IReadOnlyCollection<Symbol> Symbols => _symbols.Values;
    public IReadOnlyCollection<DuplicateAssembly> Duplicates => _duplicates;
    public IReadOnlyCollection<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public Version? Version { get; set; }

    private readonly Dictionary<Key, Symbol> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];
    private readonly List<DuplicateAssembly> _duplicates = [];

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

    public Symbol? FindSymbol(string name, SymbolKind symbolKind)
    {
        var key = new Key { Name = name, Kind = symbolKind };
        if (!_symbols.TryGetValue(key, out var sym))
        {
            return null;
        }

        return sym;
    }

    public void RecordInternalsVisibleTo(Assembly other)
    {
        _ = _internalsVisibleTo.Add(other);
    }

    public override string ToString() => Name;

    public void AddDuplicate(string path, Version version)
    {
        _duplicates.Add(new DuplicateAssembly(path, version));
    }

    public void Trim()
    {
        _symbols.TrimExcess();
        _duplicates.TrimExcess();
        _internalsVisibleTo.TrimExcess();

        foreach (var sym in _symbols)
        {
            sym.Value.Trim();
        }
    }
}
