using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace Undertaker.Graph;

internal static class AssemblyLoader
{
    public static void Load(CSharpDecompiler decomp, Func<string, Assembly> getAssembly)
    {
        var asm = getAssembly(decomp.TypeSystem.MainModule.AssemblyName);

        foreach (var type in decomp.TypeSystem.MainModule.TypeDefinitions)
        {
            RecordSymbolsReferencedByType(asm.DefineSymbol(type), type);

            if (type.Kind == TypeKind.Enum)
            {
                // we don't handle enum values, so pretend they don't exist
                continue;
            }

            foreach (var method in type.Methods)
            {
                var sym = asm.DefineSymbol(method);
                RecordSymbolsReferencedByMethod(sym, method);

                sym.Root = method.Name == "Main" && method.IsStatic;
            }

            foreach (var property in type.Properties)
            {
                RecordSymbolsReferencedByProperty(property);
            }

            foreach (var field in type.Fields)
            {
                if (field.IsConst)
                {
                    // we don't handle const values, so pretend they don't exist
                    continue;
                }

                RecordSymbolsReferencedByField(asm.DefineSymbol(field), field);
            }
        }

        asm.Loaded = true;

        void RecordSymbolsReferencedByType(Symbol typeSym, ITypeDefinition type)
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

            if (type.Name == "<Module>")
            {
                RecordSymbolsReferencedByAttributes(typeSym, decomp.TypeSystem.MainModule.GetModuleAttributes());
                RecordSymbolsReferencedByAttributes(typeSym, decomp.TypeSystem.MainModule.GetAssemblyAttributes());

                foreach (var attr in decomp.TypeSystem.MainModule.GetAssemblyAttributes())
                {
                    Console.WriteLine($"Assembly {asm.Name}, attr {attr.AttributeType.FullName}");

                    if (attr.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
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
        }

        void RecordSymbolsReferencedByMethod(Symbol methodSym, IMethod method)
        {
            RecordReferenceToType(methodSym, method.DeclaringType);

            // include references to all base definition of any overridden method
            if (method.IsOverride)
            {
                var name = GetEntitySymbolName(method);
                foreach (var t in method.DeclaringType.GetNonInterfaceBaseTypes())
                {
                    foreach (var m in t.GetMethods())
                    {
                        if (m.Name == method.Name)
                        {
                            if (GetEntitySymbolName(m) == name)
                            {
                                RecordReferenceToMember(methodSym, m);
                            }
                        }
                    }
                }
            }

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
            if (property.Getter != null)
            {
                var methodSym = asm.DefineSymbol(property.Getter);
                RecordReferenceToType(methodSym, property.DeclaringType);
                RecordSymbolsReferencedByMethod(methodSym, property.Getter);
                RecordSymbolsReferencedByAttributes(methodSym, property.GetAttributes());
            }

            if (property.Setter != null)
            {
                var methodSym = asm.DefineSymbol(property.Setter);
                RecordReferenceToType(methodSym, property.DeclaringType);
                RecordSymbolsReferencedByMethod(methodSym, property.Setter);
                RecordSymbolsReferencedByAttributes(methodSym, property.GetAttributes());
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
            if (td != null && td.ParentModule != null)
            {
                var definingAsm = getAssembly(td.ParentModule.AssemblyName);
                var toSym = definingAsm.GetSymbol(GetEntitySymbolName(toMember));
                fromSym.RecordReferencedSymbol(toSym);
            }
        }

        void RecordReferenceToType(Symbol fromSym, IType toType)
        {
            var t = toType;
            while (t != null)
            {
                var td = t.GetDefinition();
                if (td != null && td.ParentModule != null)
                {
                    var definingAsm = getAssembly(td.ParentModule.AssemblyName);
                    var toSym = definingAsm.GetSymbol(t.FullName);
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

    public static string GetEntitySymbolName(IEntity entity)
    {
        if (entity is IMethod method)
        {
            var sb = new StringBuilder()
                .Append(entity.FullName)
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

                _ = sb.Append(p.Type.Name);
            }

            return sb.Append(')').ToString();
        }

        return entity.FullName;
    }
}
