using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph;

internal sealed class MiscSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
{
    public override void Define(IEntity entity)
    {
        base.Define(entity);
    }

    public override SymbolKind Kind => SymbolKind.Misc;
}
