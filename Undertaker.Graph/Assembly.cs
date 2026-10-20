using System.Text;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Collections;
using Undertaker.Graph.Misc;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph;

internal sealed class Assembly(string name, bool root)
{
    public string Name { get; } = name;
    public bool IsRootAssembly { get; } = root;
    public bool IsSystemAssembly { get; } =
        name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
        || name == "mscorlib"
        || name == "System";

    public bool Loaded { get; set; }
    public IReadOnlyCollection<SymbolId> Symbols => _symbols.Values;
    public IReadOnlyCollection<DuplicateAssembly> Duplicates => _duplicates;
    public IReadOnlyCollection<Assembly> InternalsVisibleTo => _internalsVisibleTo;
    public Version? Version { get; set; }
    public string? Path { get; set; }
    public IReadOnlyCollection<Assembly> RealDependencies { get; private set; } = [];

    private readonly Dictionary<Key, SymbolId> _symbols = [];
    private readonly HashSet<Assembly> _internalsVisibleTo = [];
    private SmallList<DuplicateAssembly> _duplicates = [];

    private struct Key
    {
        public SkinnyString Name;
        public SymbolKind Kind;
    }

    public Symbol GetSymbol(AssemblyGraph graph, IEntity entity)
    {
        var key = new Key { Name = new(GetEntitySymbolName(entity)), Kind = GetEntitySymbolKind(entity) };

        if (!_symbols.TryGetValue(key, out var id))
        {
            id = graph.SymbolTable.CreateSymbol(this, key.Name, key.Kind);
            _symbols.Add(key, id);

            var sym = graph.SymbolTable.GetSymbol(id);
            sym.Define(entity);

            if (graph.IsReflectionSymbol(Name, sym.Name))
            {
                sym.SetReflectionTarget();
            }

            return sym;
        }

        return graph.SymbolTable.GetSymbol(id);
    }

    public Symbol GetSymbol(AssemblyGraph graph, string name, SymbolKind kind)
    {
        var key = new Key { Name = new(name), Kind = kind };
        if (!_symbols.TryGetValue(key, out var id))
        {
            id = graph.SymbolTable.CreateSymbol(this, key.Name, key.Kind);
            _symbols.Add(key, id);

            var sym = graph.SymbolTable.GetSymbol(id);

            // note, we're not calling sym.Define here
            return sym;
        }

        return graph.SymbolTable.GetSymbol(id);
    }

    public Symbol? FindSymbol(AssemblyGraph graph, string name, SymbolKind symbolKind)
    {
        var key = new Key { Name = new(name), Kind = symbolKind };
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
    public void SnapshotDependencies(AssemblyGraph graph)
    {
        RealDependencies = Symbols.Select(graph.SymbolTable.GetSymbol)
            .SelectMany(sym => sym.ReferencedSymbols)
            .Select(graph.SymbolTable.GetSymbol)
            .Select(sym => sym.Assembly)
            .Where(asm => asm != this)
            .Distinct()
            .ToArray();
    }
    public override string ToString() => Name;
    public void AddDuplicate(string path, Version version) => _duplicates.Add(new DuplicateAssembly(path, version));

    public void TrimExcess()
    {
        _symbols.TrimExcess();
        _duplicates.TrimExcess();
        _internalsVisibleTo.TrimExcess();
    }

    public static string GetEntitySymbolName(IEntity entity)
    {
        if (entity is IMethod method)
        {
            if (method.ReducedFrom != null)
            {
                method = method.ReducedFrom;
            }

            if (method.MemberDefinition is IMethod def)
            {
                method = def;
            }

            var sb = new StringBuilder()
                .Append(method.ReflectionName);
            if (method.TypeParameters.Count != 0)
            {
                sb.Append("``").Append(method.TypeParameters.Count);
            }
            sb.Append('(');

            bool first = true;
            foreach (var p in method.Parameters)
            {
                if (!first)
                {
                    _ = sb.Append(", ");
                }
                else
                {
                    first = false;
                }

                _ = sb.Append(p.Type.ReflectionName);
            }

            return sb.Append(')').ToString();
        }

        if (entity is IProperty property && property.Parameters.Count != 0)
        {
            property = (IProperty)property.MemberDefinition;
            return property.ReflectionName + "(" + string.Join(", ", property.Parameters.Select(p => p.Type.ReflectionName)) + ")";
        }

        return entity.ReflectionName;
    }

    private static SymbolKind GetEntitySymbolKind(IEntity entity)
    {
        return entity.SymbolKind switch
        {
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Method => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Constructor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Destructor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Accessor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Operator => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.TypeDefinition => SymbolKind.Type,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Field => SymbolKind.Field,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Event => SymbolKind.Event,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Property => SymbolKind.Property,
            _ => SymbolKind.Misc,
        };
    }
}
