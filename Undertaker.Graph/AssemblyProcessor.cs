using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal static class AssemblyProcessor
{
    public static void Merge(LoadedAssembly la, Func<string, Assembly> getAssembly, Func<string, bool> isTestMethodAttribute)
    {
        var decomp = la.Decompiler;
        var asm = getAssembly(decomp.TypeSystem.MainModule.AssemblyName);
        var sb = new StringBuilder();

        foreach (var type in decomp.TypeSystem.MainModule.TypeDefinitions)
        {
            var typeSym = (TypeSymbol)DefineSymbol(type);

            RecordSymbolsReferencedByType(typeSym, type);

            if (type.Kind == TypeKind.Enum)
            {
                // we don't handle enum values, so pretend they don't exist
                continue;
            }

            foreach (var method in type.Methods)
            {
                var sym = (MethodSymbol) DefineSymbol(method);
                foreach (var a in method.GetAttributes())
                {
                    if (isTestMethodAttribute(a.AttributeType.ReflectionName))
                    {
                        sym.MarkAsTestMethod();
                        break;
                    }
                }

                RecordSymbolsReferencedByMethod(sym, method);
            }

            foreach (var property in type.Properties)
            {
                RecordSymbolsReferencedByProperty(property);
            }

            foreach (var evt in type.Events)
            {
                RecordSymbolsReferencedByEvent(evt);
            }

            foreach (var field in type.Fields)
            {
                if (field.IsConst)
                {
                    // we don't handle const values, so pretend they don't exist
                    continue;
                }

                RecordSymbolsReferencedByField(DefineSymbol(field), field);
            }
        }

        asm.Loaded = true;

        Symbol DefineSymbol(IEntity entity)
        {
            var sym = asm.GetSymbol(GetEntitySymbolName(entity), GetEntitySymbolKind(entity));
            sym.Define(entity);

            var parent = entity.DeclaringTypeDefinition;
            if (parent?.ParentModule != null && sym.DeclaringType == null)
            {
                sym.DeclaringType = (TypeSymbol)getAssembly(parent.ParentModule.AssemblyName).GetSymbol(parent.ReflectionName, SymbolKind.Type);
                sym.DeclaringType.AddMember(sym);
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

                var sym = (TypeSymbol) getAssembly(bt.ParentModule!.AssemblyName).GetSymbol(bt.ReflectionName, SymbolKind.Type);

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

                            asm.RecordInternalsVisibleTo(getAssembly(name));
                        }
                    }
                }
            }

            typeSym.Trim();
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
                        if (methodSym.Name == GetEntitySymbolName(bm))
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
                    if (methodSym.Name == GetEntitySymbolName(im))
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
        }

        void RecordSymbolsReferencedByProperty(IProperty property)
        {
            var propertySym = DefineSymbol(property);
            RecordReferenceToType(propertySym, property.DeclaringType);
            RecordSymbolsReferencedByAttributes(propertySym, property.GetAttributes());

            if (property.Getter != null)
            {
                var sym = DefineSymbol(property.Getter);
                RecordSymbolsReferencedByMethod(sym, property.Getter);
            }

            if (property.Setter != null)
            {
                var sym = DefineSymbol(property.Setter);
                RecordSymbolsReferencedByMethod(sym, property.Setter);
            }
        }

        void RecordSymbolsReferencedByEvent(IEvent evt)
        {
            var eventSym = DefineSymbol(evt);
            RecordReferenceToType(eventSym, evt.DeclaringType);
            RecordSymbolsReferencedByAttributes(eventSym, evt.GetAttributes());

            if (evt.AddAccessor != null)
            {
                var sym = DefineSymbol(evt.AddAccessor);
                RecordSymbolsReferencedByMethod(sym, evt.AddAccessor);
            }

            if (evt.RemoveAccessor != null)
            {
                var sym = DefineSymbol(evt.RemoveAccessor);
                RecordSymbolsReferencedByMethod(sym, evt.RemoveAccessor);
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
                fromSym.RecordReferencedSymbol(getAssembly(td.ParentModule.AssemblyName).GetSymbol(GetEntitySymbolName(toMember), GetEntitySymbolKind(toMember)));
            }
            else
            {
                fromSym.RecordUnhomedMethodReference(GetEntitySymbolName(toMember));
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
                    var definingAsm = getAssembly(td.ParentModule.AssemblyName);
                    var toSym = definingAsm.GetSymbol(t.ReflectionName, SymbolKind.Type);
                    fromSym.RecordReferencedSymbol(toSym);
                }

                foreach (var ta in t.TypeArguments)
                {
                    RecordReferenceToType(fromSym, ta);
                }

                t = t.DeclaringType;
            }
        }

        string GetEntitySymbolName(IEntity entity)
        {
            if (entity is IMethod method)
            {
                _ = sb.Clear()
                    .Append(entity.ReflectionName)
                    .Append('(');

                bool first = true;
                foreach (var p in method.Parameters)
                {
                    if (!first)
                    {
                        _ = sb.Append(", ");
                    }
                    else
                    {
                        first = false;
                    }

                    _ = sb.Append(p.Type.ReflectionName);
                }

                return sb.Append(')').ToString();
            }

            return entity.ReflectionName;
        }
    }

    public static SymbolKind GetEntitySymbolKind(IEntity entity)
    {
        return entity.SymbolKind switch
        {
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Method => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Constructor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Destructor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Accessor => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Operator => SymbolKind.Method,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.TypeDefinition => SymbolKind.Type,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Field => SymbolKind.Field,
            ICSharpCode.Decompiler.TypeSystem.SymbolKind.Event => SymbolKind.Event,
            _ => SymbolKind.Misc,
        };
    }
}
