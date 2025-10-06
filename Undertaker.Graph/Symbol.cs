using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph;

internal abstract class Symbol(Assembly assembly, string name, SymbolId id)
{
    // set on construction
    public Assembly Assembly { get; private set; } = assembly;
    public SkinnyString Name { get; } = new SkinnyString(name);
    public SymbolId Id { get; } = id;

    public abstract SymbolKind Kind { get; }

    // set by Define
    public bool Hide { get; protected set; }
    public bool IsPublic { get; private set; }

    // Accessibility of the symbol.
    public Accessibility Access { get; private set; }

    // set by RecordReferencedSymbol
    public IReadOnlyCollection<SymbolId> Referencers => _referencers;
    public IReadOnlyCollection<SymbolId> ReferencedSymbols => _referencedSymbols;

    // filled-in over time as the overall graph is populated
    public SymbolId? DeclaringType { get; set; }
    public bool Root { get; protected set; }

    // set by Mark when visiting the graph
    public bool Marked { get; private set; }

    // set by SetReflectionTarget
    public bool ReflectionTarget { get; private set; }

    private readonly HashSet<SymbolId> _referencers = [];
    private readonly HashSet<SymbolId> _referencedSymbols = [];

    public virtual void Define(IEntity entity)
    {
        Hide = entity.IsCompilerGenerated() || Name.Contains('<');
        Access = entity.EffectiveAccessibility();
        IsPublic = Access == Accessibility.Public;

        if (Assembly.IsRootAssembly)
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
            _ = _referencedSymbols.Add(sym.Id);
            _ = sym._referencers.Add(Id);
        }
    }

    public void ReplaceMethodReference(AssemblyGraph graph, Symbol replacement)
    {
        foreach (var referencer in _referencers)
        {
            var r = graph.SymbolTable.GetSymbol(referencer);
            _ = r._referencedSymbols.Remove(Id);
            _ = r._referencedSymbols.Add(replacement.Id);
        }

        graph.SymbolTable.Redirect(Id, replacement);
    }

    public void Mark(AssemblyGraph graph)
    {
        if (Marked)
        {
            return;
        }

        Marked = true;
        foreach (var refSym in ReferencedSymbols.Select(graph.SymbolTable.GetSymbol))
        {
            refSym.Mark(graph);
        }
    }

    public void SetReflectionTarget() => ReflectionTarget = true;

    public override string ToString() => Name.ToString();

    public virtual void TrimExcess()
    {
        _referencers.TrimExcess();
        _referencedSymbols.TrimExcess();
    }
}
