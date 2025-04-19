using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class TypeSymbol(Assembly assembly, string name) : Symbol(assembly, name, SymbolKind.Type)
{
    public TypeKind TypeKind { get; private set; }
    public IReadOnlyCollection<Symbol> Children => _children;
    public IReadOnlyCollection<TypeSymbol> InterfacesImplemented => _interfacesImplemented;
    public IReadOnlyCollection<TypeSymbol> BaseTypes => _baseTypes;
    public IReadOnlyCollection<TypeSymbol> DerivedTypes => _derivedTypes;

    private readonly List<Symbol> _children = [];
    private readonly HashSet<TypeSymbol> _interfacesImplemented = [];
    private readonly HashSet<TypeSymbol> _baseTypes = [];
    private readonly HashSet<TypeSymbol> _derivedTypes = [];

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

    public void AddInterfaceImplemented(TypeSymbol interfaceType)
    {
        _ = _interfacesImplemented.Add(interfaceType);
        _ = interfaceType._derivedTypes.Add(this);
    }

    public void AddBaseType(TypeSymbol baseType)
    {
        _ = _baseTypes.Add(baseType);
        _ = baseType._derivedTypes.Add(this);
    }
}
