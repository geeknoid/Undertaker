using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph;

internal sealed class FieldSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
{
    public override void Define(IEntity entity)
    {
        if (entity is not IField)
        {
            throw new ArgumentException($"Entity must be a field definition, got {entity.GetType()}", nameof(entity));
        }

        base.Define(entity);
    }

    public override SymbolKind Kind => SymbolKind.Field;
}
