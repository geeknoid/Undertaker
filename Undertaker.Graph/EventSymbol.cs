using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class EventSymbol(Assembly assembly, string name) : Symbol(assembly, name)
{
    public override void Define(IEntity entity)
    {
        if (entity is not IEvent)
        {
            throw new ArgumentException($"Entity must be an event definition, got {entity.GetType()}", nameof(entity));
        }

        base.Define(entity);
    }

    public override SymbolKind Kind => SymbolKind.Event;
}
