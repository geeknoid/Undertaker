using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph;

internal sealed class EventSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
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
