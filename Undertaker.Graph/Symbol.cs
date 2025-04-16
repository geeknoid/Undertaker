using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class Symbol(Assembly assembly, string name)
{
    // set on construction
    public Assembly Assembly { get; } = assembly;
    public string Name { get; } = name;

    // set by Define
    public SymbolKind Kind { get; private set; } = SymbolKind.Placeholder;
    public bool Hidden { get; private set; }
    public Symbol? ParentType { get; private set; }
    public IReadOnlyList<Symbol> Children => _children;
    public TypeKind TypeKind { get; private set; }
    public bool IsPublic { get; private set; }

    // set by RecordReferencedSymbol
    public IReadOnlySet<Symbol> Referencers => _referencers;
    public IReadOnlyDictionary<string, Symbol> ReferencedSymbols => _referencedSymbols;

    // filled-in over time as the overall graph is populated
    public bool Root { get; internal set; }

    // set by Mark when visiting the graph
    public bool Marked { get; private set; }

    private readonly HashSet<Symbol> _referencers = [];
    private readonly Dictionary<string, Symbol> _referencedSymbols = [];
    private readonly List<Symbol> _children = [];

    public void Define(SymbolKind kind, TypeKind typeKind, bool hidden, bool isPublic, Symbol? parent)
    {
        Kind = kind;
        TypeKind = typeKind;
        Hidden = hidden;
        ParentType = parent;
        IsPublic = isPublic;

        parent?._children.Add(this);
    }

    public void RecordReferencedSymbol(Symbol sym)
    {
        _ = _referencedSymbols[sym.Name] = sym;
        _ = sym._referencers.Add(this);
    }

    public void Mark()
    {
        if (Marked)
        {
            return;
        }

        Marked = true;
        foreach (var refSym in ReferencedSymbols.Values)
        {
            refSym.Mark();
        }   
    }

    public override string ToString() => Name;
}
