using Undertaker.Graph.Reporting;

namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool Root { get; } = root;
    public bool Loaded { get; set; }
    public IReadOnlyCollection<SymbolId> Symbols => _symbols.Values;
    public IReadOnlyCollection<DuplicateAssembly> Duplicates => _duplicates;
    public IReadOnlyCollection<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public Version? Version { get; set; }

    private readonly Dictionary<Key, SymbolId> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];
    private readonly List<DuplicateAssembly> _duplicates = [];

    private struct Key
    {
        public string Name;
        public SymbolKind Kind;
    }

    public Symbol GetSymbol(AssemblyGraph graph, string name, SymbolKind symbolKind)
    {
        var key = new Key { Name = name, Kind = symbolKind };
        if (!_symbols.TryGetValue(key, out var id))
        {
            id = graph.SymbolTable.AddSymbol(this, name, symbolKind);
            _symbols.Add(key, id);
        }

        return graph.SymbolTable.GetSymbol(id);
    }

    public Symbol? FindSymbol(AssemblyGraph graph, string name, SymbolKind symbolKind)
    {
        var key = new Key { Name = name, Kind = symbolKind };
        return !_symbols.TryGetValue(key, out var id) ? null : graph.SymbolTable.GetSymbol(id);
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
    }

    public bool IsSystemAssembly
    {
        get
        {
            return Name.StartsWith("System.", StringComparison.Ordinal) ||
                   Name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
        }
    }
}
