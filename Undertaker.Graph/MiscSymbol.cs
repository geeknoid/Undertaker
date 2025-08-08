using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class MiscSymbol(Assembly assembly, string name) : Symbol(assembly, name)
{
    public override void Define(IEntity entity)
    {
        base.Define(entity);
    }

    public override SymbolKind Kind => SymbolKind.Misc;
}
