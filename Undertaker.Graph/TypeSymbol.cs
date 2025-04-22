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
    private readonly List<TypeSymbol> _interfacesImplemented = [];
    private readonly List<TypeSymbol> _baseTypes = [];
    private readonly List<TypeSymbol> _derivedTypes = [];

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
        _interfacesImplemented.Add(interfaceType);
        interfaceType._derivedTypes.Add(this);
    }

    public void AddBaseType(TypeSymbol baseType)
    {
        _baseTypes.Add(baseType);
        baseType._derivedTypes.Add(this);
    }

    public void Trim()
    {
        _children.TrimExcess();
        _interfacesImplemented.TrimExcess();
        _baseTypes.TrimExcess();
        _derivedTypes.TrimExcess();
    }
}
