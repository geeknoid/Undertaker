using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class MiscSymbol(Assembly assembly, string name) : Symbol(assembly, name, SymbolKind.Misc)
{
    public override void Define(IEntity entity)
    {
        base.Define(entity);
    }
}
