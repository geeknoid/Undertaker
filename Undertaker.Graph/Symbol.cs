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
    public IReadOnlyCollection<Symbol> Referencers => _referencers;
    public IReadOnlyCollection<Symbol> ReferencedSymbols => _referencedSymbols;

    // set by RecordUnhomedMethodReferenced
    public IReadOnlyCollection<string> UnhomedReferencedMethods => _unhomedReferencedMethods;

    // filled-in over time as the overall graph is populated
    public TypeSymbol? DeclaringType { get; set; }
    public bool Root { get; protected set; }

    // set by Mark when visiting the graph
    public bool Marked { get; private set; }

    private readonly HashSet<Symbol> _referencers = [];
    private readonly HashSet<Symbol> _referencedSymbols = [];
    private readonly HashSet<string> _unhomedReferencedMethods = [];

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
        if (sym != this)
        {
            _ = _referencedSymbols.Add(sym);
            _ = sym._referencers.Add(this);
        }
    }

    public void RecordUnhomedMethodReference(string methodSig)
    {
        _ = _unhomedReferencedMethods.Add(methodSig);
    }

    public void Mark()
    {
        if (Marked)
        {
            return;
        }

        Marked = true;
        foreach (var refSym in ReferencedSymbols)
        {
            refSym.Mark();
        }
    }

    public override string ToString() => Name;
}
