using Undertaker.Graph.Collections;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool IsRootAssembly { get; } = root;
    public bool IsSystemAssembly { get; } = name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) || name == "mscorlib" || name == "System";
    public bool Loaded { get; set; }

    public IReadOnlyCollection<SymbolId> Symbols => _symbols.Values;
    public IReadOnlyCollection<DuplicateAssembly> Duplicates => _duplicates;
    public IReadOnlyCollection<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public Version? Version { get; set; }

    private readonly Dictionary<Key, SymbolId> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];
    private SmallList<DuplicateAssembly> _duplicates = [];

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

    public void RemoveSymbols(AssemblyGraph graph, IEnumerable<SymbolId> removals)
    {
        foreach (var id in removals)
        {
            var sym = graph.SymbolTable.GetSymbol(id);
            var key = new Key { Kind = sym.Kind, Name = sym.Name };
            _ = _symbols.Remove(key);
        }
    }

    public void RecordInternalsVisibleTo(Assembly other) => _ = _internalsVisibleTo.Add(other);

    public override string ToString() => Name;

    public void AddDuplicate(string path, Version version) => _duplicates.Add(new DuplicateAssembly(path, version));

    public void TrimExcess()
    {
        _symbols.TrimExcess();
        _duplicates.TrimExcess();
        _internalsVisibleTo.TrimExcess();
    }
}
