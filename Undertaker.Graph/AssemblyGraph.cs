using System.Globalization;
using System.Text;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph;

/// <summary>
/// Dependency graph to identity dead symbols in a collection of assemblies.
/// </summary>
/// <remarks>
/// Once you create a graph, you then augment it with assembly state one by one in order to build up the graph.
/// After all the assemblies are loaded, you can then collect reports to get information about all the dead
/// or alive symbols across all the assemblies.
/// </remarks>
public sealed class AssemblyGraph
{
    private readonly Dictionary<string, Assembly> _assemblies = [];
    private readonly HashSet<string> _rootAssemblies = [];
    private readonly HashSet<string> _testMethodAttributes = [];
    private bool _finalized;

    /// <summary>
    /// Indicates a particular assembly should be considered a root assembly.
    /// </summary>
    /// <remarks>
    /// Within the set of assemblies that the graph holds, any assembly that is used by code not being evaluated by the graph
    /// should be considered a root. In other words, if some of the assemblies that are being analyzed are considered part of
    /// the API surface consumed by 3rd parties, then those assemblies should be recorded as roots.
    /// 
    /// Any public symbol in a root assembly is marked as a root symbol within the graph.
    /// </remarks>
    public void RecordRootAssembly(string assemblyName)
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Cannot add root assemblies after the graph has been finalized.");
        }

        _ = _rootAssemblies.Add(assemblyName);
    }

    public void RecordTestMethodAttribute(string attributeName)
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Cannot add test method attributes after the graph has been finalized.");
        }

        _ = _testMethodAttributes.Add(attributeName);
    }

    /// <summary>
    /// Merge a new asssembly into the graph.
    /// </summary>
    public void MergeAssembly(LoadedAssembly la)
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Cannot merge new assemblies after the graph has been finalized.");
        }

        AssemblyProcessor.Merge(this, la);

        // every once in a while, try to reclaim wasted space so we minimize RAM usage
        if (_assemblies.Count % 100 == 0)
        {
            foreach (var asm in _assemblies)
            {
                asm.Value.Trim();
            }
        }
    }

    internal Assembly GetAssembly(string assemblyName)
    {
        if (!_assemblies.TryGetValue(assemblyName, out var asm))
        {
            asm = new Assembly(assemblyName, _rootAssemblies.Contains(assemblyName));
            _assemblies[assemblyName] = asm;
        }

        return asm;
    }

    internal bool IsTestMethodAttribute(string attributeName) => _testMethodAttributes.Contains(attributeName);

    private void MarkUsedSymbols(Action<string> log)
    {
        log("Marking used symbols...");
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols)
            {
                if (!sym.Root)
                {
                    continue;
                }

                sym.Mark();
            }
        }
    }

    private void HandleUnhomedReferences(Action<string> log)
    {
        // Create a new assembly called the UNHOMED assembly, which will hold all unhomed references.
        var unhomedAssembly = new Assembly("UNHOMED", root: true);
        _assemblies["UNHOMED"] = unhomedAssembly;

        log("Handling unhomed references...");

        // iterate through all the unhomed references in the graph
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols)
            {
                // For each unhomed reference, we try to find a matching symbol in the loaded assemblies.
                foreach (var member in sym.UnhomedReferencedMethods)
                {
                    bool found = false;
                    foreach (var otherAsm in _assemblies.Values.Where(otherAsm => otherAsm.Loaded && otherAsm != unhomedAssembly))
                    {
                        var method = otherAsm.FindSymbol(member, SymbolKind.Method) as MethodSymbol;
                        if (method is not null)
                        {
                            sym.RecordReferencedSymbol(method);
                            found = true;
                            break;
                        }
                    }

                    // If we can't find a match, add it to the UNHOMED assembly as if that assembly had declared the symbol.
                    if (!found)
                    {
                        var unhomedSym = unhomedAssembly.GetSymbol(member, SymbolKind.Method);
                        sym.RecordReferencedSymbol(unhomedSym);
                    }
                }
            }
        }
    }

    private void HookupDerivedSymbols(Action<string> log)
    {
        /// For all interface types, we need to create a reference from interface members to any implementations of these members.
        log("Linking interface members to matching implementations");
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Interface))
            {
                foreach (var ifaceMember in sym.Members.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                {
                    foreach (var derivedType in sym.DerivedTypes)
                    {
                        foreach (var derivedMember in derivedType.Members.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                        {
                            if (ifaceMember.GetSignature() == derivedMember.GetSignature())
                            {
                                ifaceMember.RecordReferencedSymbol(derivedMember);
                            }
                        }
                    }
                }
            }
        }

        // If a method is an override or is virtual, we must create a reference to all implementations of the member in any derived types
        log("Linking virtual and override methods to derived implementations");
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>())
            {
                foreach (var member in sym.Members.Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(member => member.IsVirtualOrOverrideOrAbstract))
                {
                    var memberSig = member.GetSignature();

                    foreach (var derived in sym.DerivedTypes)
                    {
                        foreach (var derivedMember in derived.Members.Where(x => x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(member => member.IsOverride))
                        {
                            if (memberSig == derivedMember.GetSignature())
                            {
                                member.RecordReferencedSymbol(derivedMember);
                            }
                        }
                    }
                }
            }
        }

        // for any methods declared in the UNHOMED assembly, we need to add a reference from the unhomed assembly symbol to each
        // override method in the graph having the same signature.
        var unhomedAssembly = _assemblies["UNHOMED"];
        log($"Linking {unhomedAssembly.Symbols.Count} unhomed methods to overrides in any assembly");

        var signatureMap = new Dictionary<string, List<MethodSymbol>>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && asm != unhomedAssembly))
        {
            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(sym => sym.IsOverride))
            {
                var signature = sym.GetSignature();
                if (!signatureMap.TryGetValue(signature, out var methods))
                {
                    methods = [];
                    signatureMap[signature] = methods;
                }

                methods.Add(sym);
            }
        }

        foreach (var unhomedSym in unhomedAssembly.Symbols.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
        {
            var sig = unhomedSym.GetSignature();
            if (signatureMap.TryGetValue(sig, out var matchingMethods))
            {
                foreach (var matchingMethod in matchingMethods)
                {
                    unhomedSym.RecordReferencedSymbol(matchingMethod);
                }
            }
        }

        // For interface types in unanalyzed assemblies, we need to create a reference from the interface type to all implementations of its members
        log("Linking interface members in unanalyzed assemblies to derived implementations");
        foreach (var asm in _assemblies.Values.Where(asm => !asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Interface))
            {
                foreach (var derivedType in sym.DerivedTypes)
                {
                    foreach (var ifaceMember in sym.Members.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                    {
                        foreach (var derivedMember in derivedType.Members.Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                        {
                            if (ifaceMember.GetSignature() == derivedMember.GetSignature())
                            {
                                sym.RecordReferencedSymbol(derivedMember);
                            }
                        }
                    }
                }
            }
        }

        // For classes in unanalyzed assemblies, we need to create a reference from the class type to all implementations of its abstract or virtual members 
        log("Linking virtual and override methods in unanalyzed assemblies to derived implementations");
        foreach (var asm in _assemblies.Values.Where(asm => !asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Class))
            {
                foreach (var classMember in sym.Members.Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                {
                    foreach (var derivedType in sym.DerivedTypes)
                    {
                        foreach (var derivedMember in derivedType.Members.Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                        {
                            if (classMember.GetSignature() == derivedMember.GetSignature())
                            {
                                sym.RecordReferencedSymbol(derivedMember);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Completes the graph and returns a reporter to extract meaning from the graph.
    /// </summary>
    /// <param name="log">Receives progress messages as the graph analysis is performed.</param>
    public Reporter Done(Action<string> log)
    {
        if (!_finalized)
        {
            HandleUnhomedReferences(log);
            HookupDerivedSymbols(log);
            MarkUsedSymbols(log);
            _finalized = true;
        }

        return new Reporter(_assemblies);
    }
}
