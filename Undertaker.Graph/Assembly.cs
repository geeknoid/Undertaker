using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class Assembly(string name, Version version, bool root)
{
    public string Name { get; } = name;
    public Version Version { get; } = version;
    public bool Root { get; } = root;
    public IReadOnlyDictionary<string, Symbol> Symbols => _symbols;
    public bool Loaded { get; set; }

    private readonly Dictionary<string, Symbol> _symbols = [];

    public Symbol GetSymbol(string name)
    {
        if (!_symbols.TryGetValue(name, out var sym))
        {
            sym = new Symbol(this, name);
            _symbols.Add(name, sym);
        }

        return sym;
    }

    public Symbol DefineSymbol(IEntity entity)
    {
        var kind = SymbolKind.Type;
        var typeKind = TypeKind.Unknown;
        var name = entity.FullName;
        var hidden = entity.IsCompilerGenerated() || entity.Name.Contains('<');

        if (entity is IMethod m)
        {
            if (m.AccessorOwner != null)
            {
                kind = SymbolKind.Accessor;
                hidden = false;  // always expose accessors in a first class way
            }
            else if (m.IsConstructor)
            {
                kind = SymbolKind.Ctor;
            }
            else
            {
                kind = SymbolKind.Method;

                if (m.DeclaringTypeDefinition.GetDelegateInvokeMethod() != null)
                {
                    if (m.Name is "BeginInvoke" or "EndInvoke")
                    {
                        hidden = true;
                    }
                }
            }

            name = AssemblyLoader.GetEntitySymbolName(m);
        }
        else if (entity is IProperty)
        {
            kind = SymbolKind.Accessor;
        }
        else if (entity is IField)
        {
            kind = SymbolKind.Field;
        }
        else if (entity is ITypeDefinition td)
        {
            typeKind = td.Kind;
        }
        else
        {
            throw new ArgumentException($"Unknown entity type {entity.GetType()}");
        }

        var sym = GetSymbol(name);
        
        Symbol? parent = null;
        if (entity.DeclaringType != null)
        {
            parent = GetSymbol(entity.DeclaringType.FullName);
        }

        sym.Define(kind, typeKind, hidden, entity.EffectiveAccessibility() == Accessibility.Public, parent);

        if (sym.Assembly.Root)
        {
            if (entity.EffectiveAccessibility() is Accessibility.Public or Accessibility.Protected)
            {
                sym.Root = true;
            }
        }

        return sym;
    }

    public override int GetHashCode() => Name.GetHashCode();
    public override bool Equals(object? obj) => obj is Assembly asm && Name.Equals(asm.Name, StringComparison.Ordinal);
    public override string ToString() => Name;
}
