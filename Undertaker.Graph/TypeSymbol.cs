using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class TypeSymbol(Assembly assembly, string name) : Symbol(assembly, name, SymbolKind.Type)
{
    public TypeKind TypeKind { get; private set; }
    public IReadOnlyList<Symbol> Children => _children;

    private readonly List<Symbol> _children = [];

    public override void Define(IEntity entity)
    {
        if (entity is not ITypeDefinition typeDef)
        {
            throw new ArgumentException($"Entity must be a type definition, got {entity.GetType()}", nameof(entity));
        }

        base.Define(entity);

        TypeKind = typeDef.Kind;
    }

    public void AddChild(Symbol child)
    {
        _children.Add(child);
        child.ParentType = this;
    }
}
