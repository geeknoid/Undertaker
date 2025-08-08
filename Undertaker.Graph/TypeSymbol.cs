using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class TypeSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
{
    public TypeKind TypeKind { get; set; }
    public IReadOnlyCollection<SymbolId> Members => _members;
    public IReadOnlyCollection<SymbolId> InterfacesImplemented => _interfacesImplemented;
    public IReadOnlyCollection<SymbolId> BaseTypes => _baseTypes;
    public IReadOnlyCollection<SymbolId> DerivedTypes => _derivedTypes;

    private readonly HashSet<SymbolId> _members = [];
    private readonly List<SymbolId> _interfacesImplemented = [];
    private readonly List<SymbolId> _baseTypes = [];
    private readonly List<SymbolId> _derivedTypes = [];

    public override void Define(IEntity entity)
    {
        if (entity is not ITypeDefinition typeDef)
        {
            throw new ArgumentException($"Entity must be a type definition, got {entity.GetType()}", nameof(entity));
        }

        base.Define(entity);

        TypeKind = typeDef.Kind;
    }

    public void AddMember(Symbol member)
    {
        _ = _members.Add(member.Id);
        member.DeclaringType = Id;
    }

    public void AddInterfaceImplemented(TypeSymbol interfaceType)
    {
        _interfacesImplemented.Add(interfaceType.Id);
        interfaceType._derivedTypes.Add(Id);
    }

    public void AddBaseType(TypeSymbol baseType)
    {
        _baseTypes.Add(baseType.Id);
        baseType._derivedTypes.Add(Id);
    }

    public override void Trim()
    {
        _members.TrimExcess();
        _interfacesImplemented.TrimExcess();
        _baseTypes.TrimExcess();
        _derivedTypes.TrimExcess();

        base.Trim();
    }

    public override SymbolKind Kind => SymbolKind.Type;
}
