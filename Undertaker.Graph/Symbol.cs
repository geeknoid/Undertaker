using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph;

internal abstract class Symbol(Assembly assembly, string name, SymbolId id)
{
    public abstract SymbolKind Kind { get; }
    public bool IsPublic => Access == Accessibility.Public;
    public IReadOnlyCollection<SymbolId> Referencers => _referencers;
    public IReadOnlyCollection<SymbolId> ReferencedSymbols => _referencedSymbols;

    // set on construction
    public Assembly Assembly { get; private set; } = assembly;
    public SkinnyString Name { get; } = new SkinnyString(name);
    public SymbolId Id { get; } = id;

    public bool Hide { get; set; }
    public bool ReflectionTarget { get; set; }
    public bool Root { get; set; }
    public bool Marked { get; private set; }
    public Accessibility Access { get; set; }

    public SymbolId? DeclaringType { get; set; }

    private readonly HashSet<SymbolId> _referencers = [];
    private readonly HashSet<SymbolId> _referencedSymbols = [];

    public virtual void Define(IEntity entity)
    {
        Hide = entity.IsCompilerGenerated() || Name.Contains('<');
        Access = entity.EffectiveAccessibility();

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

    public override string ToString() => Name.ToString();

    public virtual void TrimExcess()
    {
        _referencers.TrimExcess();
        _referencedSymbols.TrimExcess();
    }
}
