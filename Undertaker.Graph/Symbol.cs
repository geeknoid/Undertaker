using System.Reflection.Metadata;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Collections;

namespace Undertaker.Graph;

internal abstract class Symbol(Assembly assembly, string name, SymbolId id)
{
    // set on construction
    public Assembly Assembly { get; } = assembly;
    public string Name { get; } = name;
    public SymbolId Id { get; } = id;

    public abstract SymbolKind Kind { get; }

    // set by Define
    public bool Hide { get; protected set; }
    public bool IsPublic { get; private set; }

    // set by RecordReferencedSymbol
    public IReadOnlyCollection<SymbolId> Referencers => _referencers;
    public IReadOnlyCollection<SymbolId> ReferencedSymbols => _referencedSymbols;

    // set by RecordUnhomedMethodReferenced
    public IReadOnlyCollection<string> UnhomedReferencedMethods => _unhomedReferencedMethods;

    // filled-in over time as the overall graph is populated
    public SymbolId? DeclaringType { get; set; }
    public bool Root { get; protected set; }

    // set by Mark when visiting the graph
    public bool Marked { get; private set; }

    private readonly HashSet<SymbolId> _referencers = [];
    private readonly HashSet<SymbolId> _referencedSymbols = [];
    private readonly HashSet<string> _unhomedReferencedMethods = [];

    public virtual void Define(IEntity entity)
    {
        Hide = entity.IsCompilerGenerated() || entity.Name.Contains('<');
        IsPublic = entity.EffectiveAccessibility() == Accessibility.Public;

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

            // Don't bother keeping track of who is referencing system assembly symbols.
            // Nobody cares, and doing so consumes a lot of memory.
            if (!sym.Assembly.IsSystemAssembly)
            {
                _ = sym._referencers.Add(Id);
            }
        }
    }

    public void RecordUnhomedMethodReference(string methodSig) => _ = _unhomedReferencedMethods.Add(methodSig!);

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

    public override string ToString() => Name;

    public virtual void TrimExcess()
    {
        _referencers.TrimExcess();
        _referencedSymbols.TrimExcess();
        _unhomedReferencedMethods.TrimExcess();
    }
}
