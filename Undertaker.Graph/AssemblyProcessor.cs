using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.ExceptionServices;
using System.Text;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal static class AssemblyProcessor
{
    private static readonly HashSet<string> _ignorables = [
        "System.Object",
        "System.ValueType",
        "System.Void",
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
        "System.String",
        "System.Text.StringBuilder"
#endif
        ];

    public static void Merge(AssemblyGraph graph, LoadedAssembly la)
    {
        var decomp = la.Decompiler;
        var asm = graph.GetAssembly(decomp.TypeSystem.MainModule.AssemblyName);
        var sb = new StringBuilder();

        if (asm.Loaded)
        {
            asm.AddDuplicate(la.Path, decomp.TypeSystem.MainModule.AssemblyVersion);
            return;
        }

        foreach (var type in decomp.TypeSystem.MainModule.TypeDefinitions)
        {
            var typeSym = (TypeSymbol)DefineSymbol(type);
            RecordSymbolsReferencedByType(typeSym, type);

            if (type.Kind == TypeKind.Enum)
            {
                // we don't handle enum values, so pretend they don't exist
                continue;
            }

            // find the static constructor (if any)
            IMethod? cctor = null;
            foreach (var method in type.Methods)
            {
                if (method.IsConstructor && method.IsStatic)
                {
                    cctor = method;
                    break;
                }
            }

            // find the default constructor (if any)
            IMethod? ctor = null;
            foreach (var method in type.Methods)
            {
                if (method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0)
                {
                    ctor = method;
                    break;
                }
            }

            bool moreThanConstants = false;
            bool gotConstants = false;
            foreach (var method in type.Methods)
            {
                moreThanConstants = true;

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

                RecordSymbolsReferencedByMethod(sym, method, cctor);
                typeSym.AddMember(sym);
            }

            foreach (var property in type.Properties)
            {
                moreThanConstants = true;

                var sym = DefineSymbol(property);
                RecordSymbolsReferencedByProperty(sym, property, cctor);
                typeSym.AddMember(sym);
            }

            foreach (var evt in type.Events)
            {
                moreThanConstants = true;

                var sym = DefineSymbol(evt);
                RecordSymbolsReferencedByEvent(sym, evt, cctor);
                typeSym.AddMember(sym);
            }

            foreach (var field in type.Fields)
            {
                if (field.IsConst)
                {
                    // we don't handle const values, so pretend they don't exist
                    gotConstants = true;
                    continue;
                }

                moreThanConstants = true;
                var sym = DefineSymbol(field);
                RecordSymbolsReferencedByField(sym, field, cctor);
                typeSym.AddMember(sym);
            }

            if (!moreThanConstants && gotConstants)
            {
                // since we can tell whether constants are used or not, when we find types that only define constants, let's pin them
                // so they appear alive (to avoid false positives)
                typeSym.Pin();
            }
        }

        asm.Loaded = true;
        asm.Version = decomp.TypeSystem.MainModule.AssemblyVersion;

        Symbol DefineSymbol(IEntity entity) => DefineSymbolIn(entity, asm);

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

        void RecordSymbolsReferencedByType(TypeSymbol typeSym, ITypeDefinition type)
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
                    RecordReferenceToType(typeSym, tc.Type);
                }

                RecordSymbolsReferencedByAttributes(typeSym, tp.GetAttributes());
            }

            if (type.DeclaringType != null)
            {
                RecordReferenceToType(typeSym, type.DeclaringType);
            }

            RecordSymbolsReferencedByAttributes(typeSym, type.GetAttributes());

            foreach (var bt in type.GetAllBaseTypeDefinitions())
            {
                if (bt == type)
                {
                    continue; // skip self-reference
                }

                var sym = (TypeSymbol)graph.GetAssembly(bt.ParentModule!.AssemblyName).GetSymbol(graph, bt);
                RecordReferenceToType(typeSym, bt);

                if (bt.Kind == TypeKind.Interface)
                {
                    typeSym.AddInterfaceImplemented(sym);
                }
                else
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
        }

        void RecordSymbolsReferencedByMethod(Symbol methodSym, IMethod method, IMethod? cctor)
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

            // an override depends on all base methods
            if (method.IsOverride)
            {
                foreach (var bt in method.DeclaringType.GetNonInterfaceBaseTypes())
                {
                    foreach (var bm in bt.GetMethods().Where(bm => bm.Name == method.Name && bm.Parameters.Count == method.Parameters.Count))
                    {
                        if (methodSym.Name == Assembly.GetEntitySymbolName(bm))
                        {
                            RecordReferenceToMember(methodSym, bm);
                        }
                    }
                }
            }

            // a method depends on all interface methods it implements
            foreach (var it in method.DeclaringType.GetAllBaseTypeDefinitions().Where(bt => bt.Kind == TypeKind.Interface))
            {
                foreach (var im in it.GetMethods().Where(bm => bm.Name == method.Name && bm.Parameters.Count == method.Parameters.Count))
                {
                    if (methodSym.Name == Assembly.GetEntitySymbolName(im))
                    {
                        RecordReferenceToMember(methodSym, im);
                    }
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
                                RecordReferenceToType(methodSym, field.DeclaringType);
                                break;
                            }

                        case OperandType.Method:
                            {
                                var token = blobReader.ReadInt32();
                                var handle = (EntityHandle)MetadataTokens.Handle(token);
                                var m = metadataModule.ResolveMethod(handle, default);
                                RecordReferenceToMember(methodSym, m);
                                RecordReferenceToType(methodSym, m.DeclaringType);
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

                                if (handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
                                {
                                    var type = metadataModule.ResolveType(handle, default);
                                    RecordReferenceToType(methodSym, type);
                                }
                                else if (handle.Kind == HandleKind.MethodDefinition)
                                {
                                    var m = metadataModule.ResolveMethod(handle, default);
                                    RecordReferenceToMember(methodSym, m);
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

            if (cctor != null && method != cctor)
            {
                RecordReferenceToMember(methodSym, cctor);
            }
        }

        void RecordSymbolsReferencedByProperty(Symbol propertySym, IProperty property, IMethod? cctor)
        {
            RecordReferenceToType(propertySym, property.DeclaringType);
            RecordSymbolsReferencedByAttributes(propertySym, property.GetAttributes());

            if (property.Getter != null)
            {
                var sym = DefineSymbol(property.Getter);
                RecordSymbolsReferencedByMethod(sym, property.Getter, cctor);
            }

            if (property.Setter != null)
            {
                var sym = DefineSymbol(property.Setter);
                RecordSymbolsReferencedByMethod(sym, property.Setter, cctor);
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
                foreach (var ip in it.GetProperties().Where(bm => bm.Name == property.Name))
                {
                    RecordReferenceToMember(propertySym, ip);
                }
            }
        }

        void RecordSymbolsReferencedByEvent(Symbol eventSym, IEvent evt, IMethod? cctor)
        {
            RecordReferenceToType(eventSym, evt.DeclaringType);
            RecordSymbolsReferencedByAttributes(eventSym, evt.GetAttributes());

            if (evt.AddAccessor != null)
            {
                var sym = DefineSymbol(evt.AddAccessor);
                RecordSymbolsReferencedByMethod(sym, evt.AddAccessor, cctor);
            }

            if (evt.RemoveAccessor != null)
            {
                var sym = DefineSymbol(evt.RemoveAccessor);
                RecordSymbolsReferencedByMethod(sym, evt.RemoveAccessor, cctor);
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

            // a property depends on all interface properties it implements
            foreach (var it in evt.DeclaringType.GetAllBaseTypeDefinitions().Where(bt => bt.Kind == TypeKind.Interface))
            {
                foreach (var ie in it.GetProperties().Where(bm => bm.Name == evt.Name))
                {
                    RecordReferenceToMember(eventSym, ie);
                }
            }
        }

        void RecordSymbolsReferencedByField(Symbol fieldSym, IField field, IMethod? cctor)
        {
            RecordReferenceToType(fieldSym, field.DeclaringType);
            RecordReferenceToType(fieldSym, field.Type);
            RecordSymbolsReferencedByAttributes(fieldSym, field.GetAttributes());

            if (cctor != null)
            {
                RecordReferenceToMember(fieldSym, cctor);
            }
        }

        void RecordSymbolsReferencedByAttributes(Symbol sym, IEnumerable<IAttribute> attributes)
        {
            foreach (var attr in attributes)
            {
                RecordReferenceToType(sym, attr.AttributeType);

                foreach (var arg in attr.NamedArguments)
                {
                    RecordReferenceToType(sym, arg.Type);
                }

                foreach (var arg in attr.FixedArguments)
                {
                    RecordReferenceToType(sym, arg.Type);
                }
            }
        }

        void RecordReferenceToMember(Symbol fromSym, IMember toMember)
        {
            var td = toMember.DeclaringTypeDefinition;
            if (td?.ParentModule != null)
            {
                fromSym.RecordReferencedSymbol(graph.GetAssembly(td.ParentModule.AssemblyName).GetSymbol(graph, toMember));
            }
            else
            {
                fromSym.RecordReferencedSymbol(graph.UnhomedAssembly.GetSymbol(graph, toMember));
            }
        }

        void RecordReferenceToType(Symbol fromSym, IType toType)
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
                    if (toSym.TypeKind == TypeKind.Other)
                    {
                        toSym.TypeKind = t.Kind;
                        foreach (var member in td.Members)
                        {
                            if (member is IField field && field.IsConst)
                            {
                                continue;
                            }

                            toSym.AddMember(DefineSymbolIn(member, definingAsm));
                        }
                    }

                    fromSym.RecordReferencedSymbol(toSym);
                }

                foreach (var ta in t.TypeArguments)
                {
                    RecordReferenceToType(fromSym, ta);
                }

                t = t.DeclaringType;
            }
        }
    }
}
