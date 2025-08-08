using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class FieldSymbol(Assembly assembly, string name) : Symbol(assembly, name)
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
