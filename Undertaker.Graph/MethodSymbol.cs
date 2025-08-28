using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class MethodSymbol(Assembly assembly, string name, SymbolId id) : Symbol(assembly, name, id)
{
    public bool IsVirtualOrOverrideOrAbstract { get; private set; }
    public bool IsOverride { get; private set; }
    public bool IsTestMethod { get; private set; }
    private int _parameterCount;

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

            if (m.DeclaringTypeDefinition?.GetDelegateInvokeMethod() != null)
            {
                if (m.Name is "BeginInvoke" or "EndInvoke")
                {
                    Hide = true;
                }
            }
        }

        IsVirtualOrOverrideOrAbstract = m.IsVirtual || m.IsOverride || m.IsAbstract;
        IsOverride = m.IsOverride;
        _parameterCount = m.Parameters.Count;
    }

    public void MarkAsTestMethod()
    {
        IsTestMethod = true;
        Root = true;
    }

    private (int index, int count) FindName()
    {
        // find the first ( in the name string
        // from there, backup to find the previous . or the begining of the string
        var index = Name.IndexOf('(');
        if (index < 0)
        {
            var lastDotIndex = Name.LastIndexOf('.');
            var start = (lastDotIndex < 0 ? 0 : lastDotIndex + 1);
            return (start, index - start);
        }
        else
        {
            var lastDotIndex = Name.LastIndexOf('.', index);
            var start = lastDotIndex < 0 ? 0 : lastDotIndex + 1;
            return (start, index - start);
        }
    }

    /// <summary>
    /// See if this method has a similar signature to another method.
    /// </summary>
    /// <remarks>
    /// "Similar" means the same name and parameter count.
    /// </remarks>
    public bool SimilarSignature(MethodSymbol other)
    {
        if (_parameterCount != other._parameterCount)
        {
            return false;
        }

        var (thisIndex, thisCount) = FindName();
        var (otherIndex, otherCount) = other.FindName();

        return thisCount == otherCount
            && string.CompareOrdinal(Name, thisIndex, other.Name, otherIndex, thisCount) == 0;
    }

    public override string ToString() => Name;
    public override SymbolKind Kind => SymbolKind.Method;
}
