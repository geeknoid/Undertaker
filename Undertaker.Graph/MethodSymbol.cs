using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class MethodSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
{
    public bool IsVirtualOrOverrideOrAbstract { get; private set; }
    public bool IsOverride { get; private set; }
    public bool IsTestMethod { get; private set; }

    public override void Define(IEntity entity)
    {
        if (entity is not IMethod m)
        {
            throw new ArgumentException($"Entity must be a method definition, got {entity.GetType()}", nameof(entity));
        }

        base.Define(entity);

        if (m.AccessorOwner != null)
        {
            Hide = false;  // always expose accessors in a first class way
        }
        else if (m.IsConstructor)
        {
        }
        else
        {
            Root = m.Name == "Main" && m.IsStatic;

            if (m.DeclaringTypeDefinition.GetDelegateInvokeMethod() != null)
            {
                if (m.Name is "BeginInvoke" or "EndInvoke")
                {
                    Hide = true;
                }
            }
        }

        IsVirtualOrOverrideOrAbstract = m.IsVirtual || m.IsOverride || m.IsAbstract;
        IsOverride = m.IsOverride;
    }

    public void MarkAsTestMethod()
    {
        IsTestMethod = true;
        Root = true;
    }

    private int FindSignatureStartIndex()
    {
        // find the first ( in the name string
        // from there, backup to find the previous . or the begining of the string
        // and then return the substring from that point to the end of the string
        var index = Name.IndexOf('(');
        if (index < 0)
        {
            return 0; // no parameters, just return the name
        }

        var lastDotIndex = Name.LastIndexOf('.', index);
        return lastDotIndex < 0 ? 0 : lastDotIndex + 1;
    }

    public string GetSignature()
    {
        return Name.Substring(FindSignatureStartIndex());
    }

    public override string ToString()
    {
        return $"{Name}";
    }

    public override SymbolKind Kind => SymbolKind.Method;
}
