using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal abstract class Symbol(Assembly assembly, string name, SymbolKind symbolKind)
{
    // set on construction
    public Assembly Assembly { get; } = assembly;
    public string Name { get; } = name;
    public SymbolKind Kind { get; } = symbolKind;

    // set by Define
    public bool Hide { get; protected set; }
    public bool IsPublic { get; private set; }

    // set by RecordReferencedSymbol
    public IReadOnlySet<Symbol> Referencers => _referencers;
    public IReadOnlyDictionary<string, Symbol> ReferencedSymbols => _referencedSymbols;

    // filled-in over time as the overall graph is populated
    public TypeSymbol? ParentType { get; set; }
    public bool Root { get; set; }

    // set by Mark when visiting the graph
    public bool Marked { get; private set; }

    private readonly HashSet<Symbol> _referencers = [];
    private readonly Dictionary<string, Symbol> _referencedSymbols = [];

    public virtual void Define(IEntity entity)
    {
        Hide = entity.IsCompilerGenerated() || entity.Name.Contains('<');
        IsPublic = entity.EffectiveAccessibility() == Accessibility.Public;

        if (Assembly.Root)
        {
            if (entity.EffectiveAccessibility() is Accessibility.Public or Accessibility.Protected)
            {
                Root = true;
            }
        }
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
