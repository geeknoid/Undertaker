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
    internal SymbolTable SymbolTable { get; } = new();
    internal Assembly UnhomedAssembly { get; } = new Assembly("$$UNHOMED$$", root: false);

    public AssemblyGraph()
    {
        _assemblies[UnhomedAssembly.Name] = UnhomedAssembly;
    }

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
            TrimExcess();
        }
    }

    private void TrimExcess()
    {
        foreach (var asm in _assemblies)
        {
            asm.Value.TrimExcess();
        }

        SymbolTable.TrimExcess();
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
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol))
            {
                if (!sym.Root)
                {
                    continue;
                }

                sym.Mark(this);
            }
        }
    }

    private void HandleUnhomedReferences(Action<string> log)
    {
        log("Handling unhomed references...");

        HashSet<SymbolId> removals = [];

        // For each unhomed method reference, we try to find a matching symbol in the loaded assemblies.
        foreach (var symId in UnhomedAssembly.Symbols)
        {
            var sym = SymbolTable.GetSymbol(symId);
                
            foreach (var otherAsm in _assemblies.Values.Where(otherAsm => otherAsm.Loaded && otherAsm != UnhomedAssembly))
            {
                var method = otherAsm.FindSymbol(this, sym.Name, SymbolKind.Method) as MethodSymbol;
                if (method is not null)
                {
                    sym.ReplaceMethodReference(this, method);
                    _ = removals.Add(symId);
                    break;
                }
            }
        }

        UnhomedAssembly.RemoveSymbols(this, removals);
        _ = _assemblies.Remove(UnhomedAssembly.Name);
    }

    private void HookupDerivedSymbols(Action<string> log)
    {
        /// For all interface types, we need to create a reference from interface members to any implementations of these members.
        log("Linking interface members to matching implementations");
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Interface))
            {
                foreach (var ifaceMember in sym.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                {
                    foreach (var derivedType in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                    {
                        foreach (var derivedMember in derivedType.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
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
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>())
            {
                foreach (var member in sym.Members.Select(SymbolTable.GetSymbol).Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(member => member.IsVirtualOrOverrideOrAbstract))
                {
                    var memberSig = member.GetSignature();

                    foreach (var derived in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                    {
                        foreach (var derivedMember in derived.Members.Select(SymbolTable.GetSymbol).Where(x => x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(member => member.IsOverride))
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

        // For interface types in unanalyzed assemblies, we need to create a reference from the interface type to all implementations of its members
        log("Linking interface members in unanalyzed assemblies to derived implementations");
        foreach (var asm in _assemblies.Values.Where(asm => !asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Interface))
            {
                foreach (var derivedType in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                {
                    foreach (var ifaceMember in sym.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                    {
                        foreach (var derivedMember in derivedType.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
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
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Class))
            {
                foreach (var classMember in sym.Members.Select(SymbolTable.GetSymbol).Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                {
                    foreach (var derivedType in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                    {
                        foreach (var derivedMember in derivedType.Members.Select(SymbolTable.GetSymbol).Where(member => member.Kind == SymbolKind.Method).Cast<MethodSymbol>())
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
            TrimExcess();
            HandleUnhomedReferences(log);
            HookupDerivedSymbols(log);
            MarkUsedSymbols(log);
            _finalized = true;
        }

        return new Reporter(_assemblies, SymbolTable);
    }
}
