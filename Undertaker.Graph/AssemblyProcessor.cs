using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace Undertaker.Graph;

internal static class AssemblyProcessor
{
    private static readonly HashSet<string> _ignorables = [
        "System.Object",
        "System.ValueType",
        "System.Void",
        "System.String",
        "System.Text.StringBuilder"
#if false
        "System.Int32",
        "System.Int64",
        "System.Int8",
        "System.Int16",
        "System.UInt32",
        "System.UInt16",
        "System.UInt8",
        "System.UInt64",
        "System.Boolean",
        "System.Guid",
#endif
        ];

    public static bool Merge(AssemblyGraph graph, LoadedAssembly la)
    {
        var decomp = la.Decompiler;
        var asm = graph.GetAssembly(decomp.TypeSystem.MainModule.AssemblyName);
        var sb = new StringBuilder();

        if (asm.Loaded)
        {
            asm.AddDuplicate(la.Path, decomp.TypeSystem.MainModule.AssemblyVersion);
            return false;
        }

        foreach (var type in decomp.TypeSystem.MainModule.TypeDefinitions)
        {
            // find the static constructor (if any) and default constructor (if any)
            IMethod? cctor = null;
            IMethod? ctor = null;
            foreach (var method in type.Methods)
            {
                if (method.IsConstructor)
                {
                    if (method.IsStatic)
                    {
                        cctor = method;
                    }
                    else if (method.Parameters.Count == 0)
                    {
                        ctor = method;
                    }
                }
            }

            var typeSym = (TypeSymbol)DefineSymbol(type);
            typeSym.MembersPopulated = true;
            RecordSymbolsReferencedByType(typeSym, type, cctor);
            RecordDispatchImplementations(type);

            if (type.Kind == TypeKind.Enum)
            {
                // we don't handle enum values, so pretend they don't exist
                typeSym.SetDeclaresConstants();
                continue;
            }

            foreach (var method in type.Methods)
            {
                var sym = (MethodSymbol)DefineSymbol(method);
                foreach (var a in method.GetAttributes())
                {
                    if (graph.IsTestMethodAttribute(a.AttributeType.ReflectionName))
                    {
                        sym.MarkAsTestMethod();

                        if (ctor != null)
                        {
                            // test methods keep the class constructor alive
                            RecordReferenceToMember(sym, ctor);
                        }

                        break;
                    }
                }

                RecordSymbolsReferencedByMethod(sym, method);
                RecordReflectionTarget(sym, method);
                typeSym.AddMember(sym);
            }

            foreach (var property in type.Properties)
            {
                var sym = DefineSymbol(property);
                RecordSymbolsReferencedByProperty(sym, property);
                RecordReflectionTarget(sym, property);
                typeSym.AddMember(sym);
            }

            foreach (var evt in type.Events)
            {
                var sym = DefineSymbol(evt);
                RecordSymbolsReferencedByEvent(sym, evt);
                RecordReflectionTarget(sym, evt);
                typeSym.AddMember(sym);
            }

            foreach (var field in type.Fields)
            {
                if (field.IsConst)
                {
                    if (field.Accessibility != Accessibility.Private)
                    {
                        typeSym.SetDeclaresConstants();
                    }

                    // we don't handle const values, so pretend they don't exist
                    continue;
                }

                var sym = DefineSymbol(field);
                RecordSymbolsReferencedByField(sym, field);
                RecordReflectionTarget(sym, field);
                typeSym.AddMember(sym);
            }
        }

        asm.Loaded = true;
        asm.Version = decomp.TypeSystem.MainModule.AssemblyVersion;
        asm.Path = la.Path;

        return true;

        Symbol DefineSymbol(IEntity entity) => DefineSymbolIn(entity, asm);

        Symbol GetMemberSymbol(IMember member)
        {
            var definition = member.MemberDefinition;
            var module = definition.DeclaringTypeDefinition?.ParentModule;
            return module == null
                ? graph.UnhomedAssembly.GetSymbol(graph, definition)
                : DefineSymbolIn(definition, graph.GetAssembly(module.AssemblyName));
        }

        void RecordDispatchImplementations(ITypeDefinition type)
        {
            foreach (var method in type.Methods.Concat(type.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers)).Where(m => m.IsOverride))
            {
                var declarations = InheritanceHelper.GetBaseMembers(method, false).OfType<IMethod>()
                    .Where(m => m.IsVirtual || m.IsAbstract || m.IsOverride).ToArray();
                foreach (var declaration in declarations)
                {
                    graph.RecordDispatchLink(GetMemberSymbol(declaration), GetMemberSymbol(method));
                }
                if (declarations.Length == 0)
                {
                    var objectType = type.GetNonInterfaceBaseTypes().FirstOrDefault(t => t.ReflectionName == "System.Object");
                    if (objectType?.GetDefinition() is { ParentModule: not MetadataModule } objectDefinition
                        && (method.Name is "ToString" or "GetHashCode" && method.Parameters.Count == 0
                            || method.Name == "Equals" && method.Parameters.Count == 1
                                && method.Parameters[0].Type.Equals(objectType)))
                    {
                        var objectAssembly = graph.GetAssembly(objectDefinition.ParentModule!.AssemblyName);
                        var declaration = objectAssembly.GetSymbol(graph, objectDefinition.ReflectionName + "." + method.Name
                            + "(" + string.Join(", ", method.Parameters.Select(p => p.Type.ReflectionName)) + ")", SymbolKind.Method);
                        ((TypeSymbol)objectAssembly.GetSymbol(graph, objectDefinition)).AddMember(declaration);
                        graph.RecordDispatchLink(declaration, GetMemberSymbol(method));
                    }
                }
            }

            foreach (var iface in type.GetAllBaseTypes().Where(t => t.Kind == TypeKind.Interface))
            {
                var implementingType = type.GetNonInterfaceBaseTypes().Reverse().FirstOrDefault(t =>
                    t.DirectBaseTypes.Any(bt => bt.Kind == TypeKind.Interface && bt.GetAllBaseTypes().Contains(iface)));
                if (implementingType == null)
                {
                    continue;
                }
                var candidates = implementingType.GetNonInterfaceBaseTypes().Reverse()
                    .SelectMany(t => t.GetMethods(options: GetMemberOptions.IgnoreInheritedMembers)
                        .Concat(t.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers)))
                    .ToArray();
                if (!iface.GetMethods().Any() && iface.GetDefinition()?.ParentModule is not MetadataModule)
                {
                    RecordKnownRuntimeInterface(iface, candidates);
                }
                foreach (var declaration in iface.GetMethods(options: GetMemberOptions.IgnoreInheritedMembers)
                    .Concat(iface.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers)))
                {
                    IMethod? implementation = null;
                    foreach (var declaringType in candidates.GroupBy(candidate => candidate.DeclaringType))
                    {
                        implementation = declaringType.FirstOrDefault(candidate =>
                            candidate.ExplicitlyImplementedInterfaceMembers.Any(m => m.Equals(declaration)))
                            ?? declaringType.FirstOrDefault(candidate =>
                            !candidate.IsExplicitInterfaceImplementation
                            && candidate.Accessibility == Accessibility.Public
                            && candidate.IsStatic == declaration.IsStatic
                            && SignatureComparer.Ordinal.Equals(candidate, declaration));
                        if (implementation != null)
                        {
                            break;
                        }
                    }

                    if (implementation != null)
                    {
                        graph.RecordDispatchLink(GetMemberSymbol(declaration), GetMemberSymbol(implementation));
                    }
                }
            }
        }

        void RecordKnownRuntimeInterface(IType iface, IEnumerable<IMethod> candidates)
        {
            // MinimalCorlib supplies framework type identities but no members.
            var mappedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates.GroupBy(m => m.DeclaringType)
                .SelectMany(level => level.OrderByDescending(m => m.IsExplicitInterfaceImplementation)))
            {
                var explicitDeclaration = candidate.ExplicitlyImplementedInterfaceMembers
                    .OfType<IMethod>().FirstOrDefault(m => m.DeclaringType.Equals(iface));
                var name = explicitDeclaration?.Name ?? candidate.Name;
                if ((explicitDeclaration == null && candidate.Accessibility != Accessibility.Public)
                    || candidate.IsStatic || candidate.TypeParameters.Count != 0)
                {
                    continue;
                }
                var parameters = candidate.Parameters;
                var matches = iface.GetDefinition()!.ReflectionName switch
                {
                    "System.IDisposable" => name == "Dispose" && parameters.Count == 0,
                    "System.Runtime.CompilerServices.IAsyncStateMachine" =>
                        name == "MoveNext" && parameters.Count == 0
                        || name == "SetStateMachine" && parameters.Count == 1 && parameters[0].Type.Equals(iface),
                    "System.Collections.IEnumerable" or "System.Collections.Generic.IEnumerable`1" =>
                        name == "GetEnumerator" && parameters.Count == 0,
                    "System.Collections.ICollection" =>
                        name == "get_Count" && parameters.Count == 0
                        || name == "CopyTo" && parameters.Count == 2
                            && parameters[0].Type.ReflectionName == "System.Array"
                            && parameters[1].Type.ReflectionName == "System.Int32",
                    "System.Collections.Generic.ICollection`1" =>
                        name == "get_Count" && parameters.Count == 0
                        || name == "CopyTo" && parameters.Count == 2
                            && parameters[0].Type is ArrayType array && array.Dimensions == 1
                            && array.ElementType.Equals(iface.TypeArguments[0])
                            && parameters[1].Type.ReflectionName == "System.Int32",
                    _ => false,
                };
                if (!matches || !mappedNames.Add(name))
                {
                    continue;
                }
                var definition = iface.GetDefinition()!;
                var declaration = graph.GetAssembly(definition.ParentModule!.AssemblyName).GetSymbol(graph,
                    definition.ReflectionName + "." + name + "("
                    + string.Join(", ", parameters.Select(p => p.Type.ReflectionName)) + ")", SymbolKind.Method);
                ((TypeSymbol)graph.GetAssembly(definition.ParentModule.AssemblyName).GetSymbol(graph, definition)).AddMember(declaration);
                graph.RecordDispatchLink(declaration, GetMemberSymbol(candidate));
            }
        }

        Symbol DefineSymbolIn(IEntity entity, Assembly a)
        {
            var sym = a.GetSymbol(graph, entity);

            var parent = entity.DeclaringTypeDefinition;
            if (parent?.ParentModule != null && sym.DeclaringType == null)
            {
                var dt = (TypeSymbol)graph.GetAssembly(parent.ParentModule.AssemblyName).GetSymbol(graph, parent);
                dt.AddMember(sym);
                sym.DeclaringType = dt.Id;
            }

            return sym;
        }

        void RecordReflectionTarget(Symbol sym, IEntity entity)
        {
            foreach (var a in entity.GetAttributes())
            {
                if (graph.IsReflectionMarkerAttribute(a.AttributeType.ReflectionName))
                {
                    sym.SetReflectionTarget();
                    break;
                }
            }
        }

        void RecordSymbolsReferencedByType(TypeSymbol typeSym, ITypeDefinition type, IMethod? cctor)
        {
            foreach (var bt in type.DirectBaseTypes)
            {
                RecordReferenceToType(typeSym, bt);
            }

            foreach (var ta in type.TypeArguments)
            {
                RecordReferenceToType(typeSym, ta);
            }

            foreach (var tp in type.TypeParameters)
            {
                foreach (var tc in tp.TypeConstraints)
                {
                    RecordSymbolsReferencedByAttributes(typeSym, tc.Attributes);
                    RecordReferenceToType(typeSym, tc.Type);
                }

                RecordSymbolsReferencedByAttributes(typeSym, tp.GetAttributes());
            }

            if (type.DeclaringType != null)
            {
                RecordReferenceToType(typeSym, type.DeclaringType);
            }

            RecordSymbolsReferencedByAttributes(typeSym, type.GetAttributes());
            RecordReflectionTarget(typeSym, type);

            foreach (var bt in type.GetAllBaseTypeDefinitions())
            {
                if (bt == type)
                {
                    continue; // skip self-reference
                }

                var sym = (TypeSymbol)graph.GetAssembly(bt.ParentModule!.AssemblyName).GetSymbol(graph, bt);
                RecordReferenceToType(typeSym, bt);

                if (typeSym.ReflectionTarget)
                {
                    sym.SetReflectionTarget();
                }

                if (bt.Kind == TypeKind.Interface)
                {
                    typeSym.AddInterfaceImplemented(sym);
                }
                else if (!(sym.Assembly.IsSystemAssembly && _ignorables.Contains(bt.ReflectionName)))
                {
                    typeSym.AddBaseType(sym);
                }
            }

            if (type.Name == "<Module>")
            {
                RecordSymbolsReferencedByAttributes(typeSym, decomp.TypeSystem.MainModule.GetModuleAttributes());
                RecordSymbolsReferencedByAttributes(typeSym, decomp.TypeSystem.MainModule.GetAssemblyAttributes());

                foreach (var attr in decomp.TypeSystem.MainModule.GetAssemblyAttributes())
                {
                    if (attr.AttributeType.ReflectionName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                    {
                        var assemblyName = attr.FixedArguments[0].Value;
                        if (assemblyName is string name)
                        {
                            var comma = name.IndexOf(',');
                            if (comma > 0)
                            {
                                name = name[..comma];
                            }

                            asm.RecordInternalsVisibleTo(graph.GetAssembly(name));
                        }
                    }
                }
            }

            if (cctor != null)
            {
                RecordReferenceToMember(typeSym, cctor);
            }
        }

        void RecordSymbolsReferencedByMethod(Symbol methodSym, IMethod method)
        {
            RecordReferenceToType(methodSym, method.DeclaringType);

            foreach (var ta in method.TypeArguments)
            {
                RecordReferenceToType(methodSym, ta);
            }

            foreach (var tp in method.TypeParameters)
            {
                foreach (var tc in tp.TypeConstraints)
                {
                    RecordSymbolsReferencedByAttributes(methodSym, tc.Attributes);
                    RecordReferenceToType(methodSym, tc.Type);
                }

                RecordSymbolsReferencedByAttributes(methodSym, tp.GetAttributes());
            }

            foreach (var parameter in method.Parameters)
            {
                RecordReferenceToType(methodSym, parameter.Type);
                RecordSymbolsReferencedByAttributes(methodSym, parameter.GetAttributes());
            }

            RecordReferenceToType(methodSym, method.ReturnType);
            RecordSymbolsReferencedByAttributes(methodSym, method.GetAttributes());
            RecordSymbolsReferencedByAttributes(methodSym, method.GetReturnTypeAttributes());

            if (method.AccessorOwner != null)
            {
                RecordReferenceToMember(methodSym, method.AccessorOwner);
            }

            foreach (var im in InheritanceHelper.GetBaseMembers(method, true))
            {
                if (im.DeclaringType.Kind == TypeKind.Interface || method.IsOverride)
                {
                    RecordReferenceToMember(methodSym, im);
                }
            }

            if (method.HasBody)
            {
                var metadataModule = method.DeclaringType.GetDefinition()!.ParentModule! as MetadataModule;
                var metadataReader = metadataModule!.MetadataFile.Metadata;
                var methodDefinition = metadataReader.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
                var methodBodyBlock = metadataModule.MetadataFile.GetMethodBody(methodDefinition.RelativeVirtualAddress);

                var blobReader = methodBodyBlock.GetILReader();
                while (blobReader.RemainingBytes > 0)
                {
                    var opCode = blobReader.DecodeOpCode();
                    switch (opCode.GetOperandType())
                    {
                        case OperandType.Field:
                            {
                                var token = blobReader.ReadInt32();
                                var handle = MetadataTokens.EntityHandle(token);
                                var field = (IField)metadataModule.ResolveEntity(handle);
                                RecordReferenceToMember(methodSym, field);
                                break;
                            }

                        case OperandType.Method:
                            {
                                var token = blobReader.ReadInt32();
                                var handle = (EntityHandle)MetadataTokens.Handle(token);
                                var m = metadataModule.ResolveMethod(handle, default);

                                RecordReferenceToMember(methodSym, m);

                                var count = 0;
                                foreach (var ta in m.TypeArguments)
                                {
                                    bool reflectionTarget = false;
                                    foreach (var attr in m.TypeParameters[count].GetAttributes())
                                    {
                                        if (attr.AttributeType.ReflectionName == "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute")
                                        {
                                            reflectionTarget = true;
                                            break;
                                        }
                                    }

                                    RecordReferenceToType(methodSym, ta, reflectionTarget);
                                    count++;
                                }

                                break;
                            }

                        case OperandType.Type:
                            {
                                var token = blobReader.ReadInt32();
                                var handle = (EntityHandle)MetadataTokens.Handle(token);
                                var type = metadataModule.ResolveType(handle, default);
                                RecordReferenceToType(methodSym, type);
                                break;
                            }

                        case OperandType.Tok:
                            {
                                var token = blobReader.ReadInt32();
                                var handle = (EntityHandle)MetadataTokens.Handle(token);

                                if (handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification)
                                {
                                    var type = metadataModule.ResolveType(handle, default);
                                    RecordReferenceToType(methodSym, type);
                                }
                                else if (handle.Kind is HandleKind.MethodDefinition or HandleKind.MethodSpecification)
                                {
                                    var m = metadataModule.ResolveMethod(handle, default);
                                    RecordReferenceToMember(methodSym, m);
                                }
                                else if (handle.Kind is HandleKind.FieldDefinition or HandleKind.MemberReference)
                                {
                                    if (metadataModule.ResolveEntity(handle) is IMember member)
                                    {
                                        RecordReferenceToMember(methodSym, member);
                                    }
                                }

                                break;
                            }

                        default:
                            blobReader.SkipOperand(opCode);
                            break;
                    }
                }

                var localSignature = methodBodyBlock.LocalSignature;
                if (!localSignature.IsNil)
                {
                    foreach (var type in decomp.TypeSystem.MainModule.DecodeLocalSignature(localSignature, default))
                    {
                        RecordReferenceToType(methodSym, type);
                    }
                }

                foreach (var er in methodBodyBlock.ExceptionRegions)
                {
                    var type = metadataModule.ResolveType(er.CatchType, default);
                    RecordReferenceToType(methodSym, type);
                }
            }
        }

        void RecordSymbolsReferencedByProperty(Symbol propertySym, IProperty property)
        {
            RecordReferenceToType(propertySym, property.DeclaringType);
            RecordSymbolsReferencedByAttributes(propertySym, property.GetAttributes());
            RecordReferenceToType(propertySym, property.ReturnType);

            if (property.Getter != null)
            {
                var sym = DefineSymbol(property.Getter);
                graph.RecordReflectionAccessor(propertySym, sym);
                RecordSymbolsReferencedByMethod(sym, property.Getter);
            }

            if (property.Setter != null)
            {
                var sym = DefineSymbol(property.Setter);
                graph.RecordReflectionAccessor(propertySym, sym);
                RecordSymbolsReferencedByMethod(sym, property.Setter);
            }

            foreach (var pp in property.Parameters)
            {
                RecordReferenceToType(propertySym, pp.Type);
                RecordSymbolsReferencedByAttributes(propertySym, pp.GetAttributes());
            }

            // an override depends on all base properties
            if (property.IsOverride)
            {
                foreach (var bt in property.DeclaringType.GetNonInterfaceBaseTypes())
                {
                    foreach (var bm in bt.GetProperties().Where(bm => bm.Name == property.Name))
                    {
                        RecordReferenceToMember(propertySym, bm);
                    }
                }
            }

            // a property depends on all interface properties it implements
            foreach (var it in property.DeclaringType.GetAllBaseTypeDefinitions().Where(bt => bt.Kind == TypeKind.Interface))
            {
                foreach (var ip in it.GetProperties().Where(bm => bm.Name == property.Name && bm.Parameters.Count == property.Parameters.Count))
                {
                    RecordReferenceToMember(propertySym, ip);
                }
            }

            foreach (var im in property.ExplicitlyImplementedInterfaceMembers)
            {
                RecordReferenceToMember(propertySym, im);
            }
        }

        void RecordSymbolsReferencedByEvent(Symbol eventSym, IEvent evt)
        {
            RecordReferenceToType(eventSym, evt.DeclaringType);
            RecordSymbolsReferencedByAttributes(eventSym, evt.GetAttributes());
            RecordReferenceToType(eventSym, evt.ReturnType);

            if (evt.AddAccessor != null)
            {
                var sym = DefineSymbol(evt.AddAccessor);
                graph.RecordReflectionAccessor(eventSym, sym);
                RecordSymbolsReferencedByMethod(sym, evt.AddAccessor);
            }

            if (evt.RemoveAccessor != null)
            {
                var sym = DefineSymbol(evt.RemoveAccessor);
                graph.RecordReflectionAccessor(eventSym, sym);
                RecordSymbolsReferencedByMethod(sym, evt.RemoveAccessor);
            }

            // an override depends on all base events
            if (evt.IsOverride)
            {
                foreach (var bt in evt.DeclaringType.GetNonInterfaceBaseTypes())
                {
                    foreach (var bm in bt.GetEvents().Where(bm => bm.Name == evt.Name))
                    {
                        RecordReferenceToMember(eventSym, bm);
                    }
                }
            }

            // an event depends on all interface events it implements
            foreach (var it in evt.DeclaringType.GetAllBaseTypeDefinitions().Where(bt => bt.Kind == TypeKind.Interface))
            {
                foreach (var ie in it.GetEvents().Where(bm => bm.Name == evt.Name))
                {
                    RecordReferenceToMember(eventSym, ie);
                }
            }

            foreach (var im in evt.ExplicitlyImplementedInterfaceMembers)
            {
                RecordReferenceToMember(eventSym, im);
            }
        }

        void RecordSymbolsReferencedByField(Symbol fieldSym, IField field)
        {
            RecordReferenceToType(fieldSym, field.DeclaringType);
            RecordReferenceToType(fieldSym, field.Type);
            RecordSymbolsReferencedByAttributes(fieldSym, field.GetAttributes());
        }

        void RecordSymbolsReferencedByAttributes(Symbol sym, IEnumerable<IAttribute> attributes)
        {
            foreach (var attr in attributes)
            {
                RecordReferenceToType(sym, attr.AttributeType);
                if (attr.Constructor != null)
                {
                    RecordReferenceToMember(sym, attr.Constructor);
                }

                foreach (var arg in attr.NamedArguments)
                {
                    RecordReferenceToType(sym, arg.Type);
                    RecordAttributeValue(sym, arg.Value);
                    var member = arg.Kind == CustomAttributeNamedArgumentKind.Field
                        ? attr.AttributeType.GetFields(f => f.Name == arg.Name).Cast<IMember>().FirstOrDefault()
                        : attr.AttributeType.GetProperties(p => p.Name == arg.Name).Cast<IMember>().FirstOrDefault();
                    if (member != null)
                    {
                        RecordReferenceToMember(sym, member);
                        if (member is IProperty { Setter: not null } property)
                        {
                            RecordReferenceToMember(sym, property.Setter);
                        }
                    }
                }

                foreach (var arg in attr.FixedArguments)
                {
                    RecordReferenceToType(sym, arg.Type);
                    RecordAttributeValue(sym, arg.Value);
                }
            }
        }

        void RecordAttributeValue(Symbol sym, object? value)
        {
            if (value is IType type)
            {
                RecordReferenceToType(sym, type);
            }
            else if (value is IEnumerable<CustomAttributeTypedArgument<IType>> elements)
            {
                foreach (var element in elements)
                {
                    RecordReferenceToType(sym, element.Type);
                    RecordAttributeValue(sym, element.Value);
                }
            }
        }

        void RecordReferenceToMember(Symbol fromSym, IMember toMember)
        {
            fromSym.RecordReferencedSymbol(GetMemberSymbol(toMember));
            foreach (var argument in toMember.DeclaringType.TypeArguments)
            {
                RecordReferenceToType(fromSym, argument);
            }
        }

        void RecordReferenceToType(Symbol fromSym, IType toType, bool reflectionTarget = false)
        {
            var t = toType;
            while (t != null)
            {
                var td = t.GetDefinition();
                if (td?.ParentModule != null)
                {
                    var definingAsm = graph.GetAssembly(td.ParentModule.AssemblyName);

                    if (definingAsm.IsSystemAssembly && _ignorables.Contains(t.ReflectionName))
                    {
                        // Don't record references to some system types, they are too common and not useful.
                        return;
                    }

                    var toSym = (TypeSymbol)definingAsm.GetSymbol(graph, td);
                    if (!toSym.MembersPopulated)
                    {
                        toSym.MembersPopulated = true;
                        foreach (var member in td.Members)
                        {
                            if (td.Kind == TypeKind.Enum || member is IField { IsConst: true })
                            {
                                continue;
                            }

                            toSym.AddMember(DefineSymbolIn(member, definingAsm));
                        }
                    }

                    fromSym.RecordReferencedSymbol(toSym);

                    if (reflectionTarget)
                    {
                        toSym.SetReflectionTarget();
                    }
                }

                foreach (var ta in t.TypeArguments)
                {
                    RecordReferenceToType(fromSym, ta);
                }

                if (t is TypeWithElementType elementType)
                {
                    RecordReferenceToType(fromSym, elementType.ElementType, reflectionTarget);
                }

                t = t.DeclaringType;
            }
        }
    }
}
