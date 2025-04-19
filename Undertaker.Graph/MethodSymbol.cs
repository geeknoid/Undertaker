using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal sealed class MethodSymbol(Assembly assembly, string name) : Symbol(assembly, name, SymbolKind.Method)
{
    public bool IsVirtualOrOverride { get; private set; }

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

        IsVirtualOrOverride = m.IsVirtual || m.IsOverride;
    }
}
