using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

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

        if (m.AccessorOwner != null && !m.AccessorOwner.IsCompilerGenerated())
        {

            Hide = false;  // always expose accessors in a first class way
        }
        else if (m.IsConstructor)
        {
        }
        else
        {
            if (m.Name == "Main" && m.IsStatic)
            {
                Root = NonTestRoot = true;
            }

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
    }

    public void MarkAsTestMethod()
    {
        IsTestMethod = true;
        Root = true;
    }

    public override string ToString() => Name;
    public override SymbolKind Kind => SymbolKind.Method;
}
