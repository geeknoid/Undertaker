using System.Globalization;
using System.Text;
using ICSharpCode.Decompiler.TypeSystem;

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

        AssemblyProcessor.Merge(la, GetAssembly, IsTestMethodAttribute);
    }

    private Assembly GetAssembly(string assemblyName)
    {
        if (!_assemblies.TryGetValue(assemblyName, out var asm))
        {
            asm = new Assembly(assemblyName, _rootAssemblies.Contains(assemblyName));
            _assemblies[assemblyName] = asm;
        }

        return asm;
    }

    private bool IsTestMethodAttribute(string attributeName) => _testMethodAttributes.Contains(attributeName);

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

    public void Done(Action<string> log)
    {
        if (!_finalized)
        {
            HandleUnhomedReferences(log);
            HookupDerivedSymbols(log);
            MarkUsedSymbols(log);
            _finalized = true;
        }
    }

    private void Done()
    {
        Done(x => { /* no-op */ });
    }

    /// <summary>
    /// Gets information about the dead symbols in the graph.
    /// </summary>
    /// <remarks>Dead symbols are ones which aren't reachable from the various roots known to the graph.</remarks>
    public GraphReport CollectDeadSymbols()
    {
        Done();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            List<GraphReportSymbol>? deadTypes = null;
            List<GraphReportSymbol>? deadMembers = null;

            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide).Cast<TypeSymbol>())
            {
                if (sym.Marked)
                {
                    foreach (var member in sym.Members.Where(member => !member.Marked && !member.Hide && member.Kind != SymbolKind.Type))
                    {
                        deadMembers ??= [];
                        deadMembers.Add(new(member.Name, Array.Empty<string>(), member.Root));
                    }
                }
                else
                {
                    deadTypes ??= [];
                    deadTypes.Add(new(sym.Name, Array.Empty<string>(), sym.Root));
                }
            }

            if (deadTypes != null || deadMembers != null)
            {
                deadTypes?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
                deadMembers?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));

                IReadOnlyList<GraphReportSymbol>? dt = deadTypes;
                dt ??= Array.Empty<GraphReportSymbol>();

                IReadOnlyList<GraphReportSymbol>? dm = deadMembers;
                dm ??= Array.Empty<GraphReportSymbol>();

                assemblies.Add(new(asm.Name, dt, dm));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets information about the alive symbols in the graph.
    /// </summary>
    /// <remarks>Alive symbols are ones which are reachable from the various roots known to the graph.</remarks>
    public GraphReport CollectAliveSymbols()
    {
        Done();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            List<GraphReportSymbol>? aliveTypes = null;
            List<GraphReportSymbol>? aliveMembers = null;

            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide && sym.Marked).Cast<TypeSymbol>())
            {
                var dependents = sym.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x).ToList();

                aliveTypes ??= [];
                aliveTypes.Add(new(sym.Name, dependents, sym.Root));

                foreach (var member in sym.Members)
                {
                    if (member.Marked && !member.Hide)
                    {
                        dependents = [.. member.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x)];

                        aliveMembers ??= [];
                        aliveMembers.Add(new(member.Name, dependents, member.Root));
                    }
                }
            }

            if (aliveTypes != null || aliveMembers != null)
            {
                aliveTypes?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
                aliveMembers?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));

                IReadOnlyList<GraphReportSymbol>? at = aliveTypes;
                at ??= Array.Empty<GraphReportSymbol>();

                IReadOnlyList<GraphReportSymbol>? am = aliveMembers;
                am ??= Array.Empty<GraphReportSymbol>();

                assemblies.Add(new(asm.Name, at, am));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets information about the alive symbols in the graph that are kept alive strictly by test methods.
    /// </summary>
    public GraphReport CollectAliveByTestSymbols()
    {
        Done();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            List<GraphReportSymbol>? aliveTypes = null;
            List<GraphReportSymbol>? aliveMembers = null;

            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide && sym.Marked).Cast<TypeSymbol>())
            {
                var dependents = sym.Referencers.Where(x => x.Marked && x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(x => x.IsTestMethod).Select(x => x.Name).OrderBy(x => x).ToList();
                if (dependents.Count == 0)
                {
                    continue;
                }

                aliveTypes ??= [];
                aliveTypes.Add(new(sym.Name, dependents, sym.Root));

                foreach (var member in sym.Members)
                {
                    if (member.Marked && !member.Hide)
                    {
                        dependents = [.. member.Referencers.Where(x => x.Marked && x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(x => x.IsTestMethod).Select(x => x.Name).OrderBy(x => x)];
                        if (dependents.Count == 0)
                        {
                            continue;
                        }

                        aliveMembers ??= [];
                        aliveMembers.Add(new(member.Name, dependents, member.Root));
                    }
                }
            }

            if (aliveTypes != null || aliveMembers != null)
            {
                aliveTypes?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
                aliveMembers?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));

                IReadOnlyList<GraphReportSymbol>? at = aliveTypes;
                at ??= Array.Empty<GraphReportSymbol>();

                IReadOnlyList<GraphReportSymbol>? am = aliveMembers;
                am ??= Array.Empty<GraphReportSymbol>();

                assemblies.Add(new(asm.Name, at, am));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets the set of symbols which could be made internal.
    /// </summary>
    public GraphReport CollectNeedlesslyPublicSymbols()
    {
        Done();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            List<GraphReportSymbol>? affectedTypes = null;
            List<GraphReportSymbol>? affectedMembers = null;

            foreach (var sym in asm.Symbols.Where(sym => !sym.Hide && !sym.Root))
            {
                bool usedOutside = false;
                foreach (var r in sym.Referencers)
                {
                    if (r.Assembly != asm)
                    {
                        usedOutside = true;
                        break;
                    }
                }

                if (!usedOutside)
                {
                    if (sym.Kind == SymbolKind.Type)
                    {
                        affectedTypes ??= [];
                        affectedTypes.Add(new(sym.Name, [], sym.Root));
                    }
                    else
                    {
                        affectedMembers ??= [];
                        affectedMembers.Add(new(sym.Name, [], sym.Root));
                    }
                }
            }

            if (affectedTypes != null || affectedTypes != null)
            {
                affectedTypes?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
                affectedMembers?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));

                IReadOnlyList<GraphReportSymbol>? at = affectedTypes;
                at ??= Array.Empty<GraphReportSymbol>();

                IReadOnlyList<GraphReportSymbol>? am = affectedMembers;
                am ??= Array.Empty<GraphReportSymbol>();

                assemblies.Add(new(asm.Name, at, am));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Returns a list of assemblies which are not reachable from any roots.
    /// </summary>
    public IReadOnlyList<string> CollectUnreferencedAssemblies()
    {
        Done();

        var result = new List<string>();

        var aliveReport = CollectAliveSymbols();
        foreach (var asm in aliveReport.Assemblies.Where(asm => asm.Types.Count == 0 && asm.Members.Count == 0))
        {
            result.Add(asm.Assembly);
        }

        return result;
    }

    /// <summary>
    /// Returns a list of assemblies which were referenced but not analyzed.
    /// </summary>
    public IReadOnlyList<string> CollectUnanalyzedAssemblies()
    {
        Done();

        return [.. _assemblies.Values.Where(asm => !asm.Loaded).Select(asm => asm.Name)];
    }

    /// <summary>
    /// Returns a list of assemblies which have uses of [InternalsVisibleTo] that could be removed. 
    /// </summary>
    public IReadOnlyList<NeedlessInternalsVisibleToReport> CollectNeedlessInternalsVisibleTo()
    {
        Done();

        var result = new List<NeedlessInternalsVisibleToReport>();

        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            var otherAssemblies = new List<string>();

            foreach (var other in asm.InternalsVisibleTo.Where(other => other.Loaded))
            {
                bool usesInternals = false;
                foreach (var sym in other.Symbols)
                {
                    foreach (var refSym in sym.ReferencedSymbols)
                    {
                        if (refSym.Assembly == asm && !refSym.IsPublic)
                        {
                            usesInternals = true;
                            break;
                        }
                    }

                    if (usesInternals)
                    {
                        break;
                    }
                }

                if (!usesInternals)
                {
                    otherAssemblies.Add(other.Name);
                }
            }

            if (otherAssemblies.Count > 0)
            {
                otherAssemblies.Sort((x, y) => string.CompareOrdinal(x, y));
                result.Add(new(asm.Name, otherAssemblies));
            }
        }

        result.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return result;
    }

    /// <summary>
    /// Creates a layer cake of assembly dependencies.
    /// </summary>
    /// <remarks>
    /// Returns a layer cake of assembly dependencies. Each layer depends only on assemblies in
    /// the layers below.
    /// </remarks>
    /// <returns>A list of layers, where each layer is a list of assembly names.</returns>
    public IReadOnlyList<IReadOnlyList<string>> CreateAssemblyLayerCake()
    {
        Done();

        // Step 1: Build a dependency map for each assembly
        var dependencies = new Dictionary<string, HashSet<string>>();
        foreach (var asm in _assemblies.Values)
        {
            var referenced = new HashSet<string>();
            foreach (var sym in asm.Symbols)
            {
                foreach (var rs in sym.ReferencedSymbols.Where(rs => rs.Assembly != asm))
                {
                    _ = referenced.Add(rs.Assembly.Name);
                }
            }

            dependencies.Add(asm.Name, referenced);
        }

        // Step 2: Initialize dependency counts for each assembly
        var dependencyCount = _assemblies.Values.ToDictionary(a => a.Name, a => 0);
        foreach (var asm in _assemblies.Values)
        {
            foreach (var dependency in dependencies[asm.Name].Where(dependency => _assemblies.ContainsKey(dependency)))
            {
                dependencyCount[dependency]++;
            }
        }

        // Step 3: Create layers
        var layers = new List<List<string>>();
        while (dependencyCount.Any(dc => dc.Value == 0))
        {
            var currentLayer = new List<string>();
            var assembliesToRemove = new List<string>();

            // Find assemblies with no dependencies
            foreach (var asm in dependencyCount.Where(dc => dc.Value == 0).Select(dc => dc.Key))
            {
                if (_assemblies[asm].Loaded)
                {
                    currentLayer.Add(asm);
                }

                assembliesToRemove.Add(asm);
            }

            // Remove assemblies from the dependency map and update dependency counts
            foreach (var assemblyName in assembliesToRemove)
            {
                _ = dependencyCount.Remove(assemblyName);
                foreach (var dependency in dependencies[assemblyName])
                {
                    if (dependencyCount.TryGetValue(dependency, out int value))
                    {
                        dependencyCount[dependency] = --value;
                    }
                }
            }

            layers.Add(currentLayer);
        }

        return layers;
    }

    /// <summary>
    /// Creates a dependency diagram in Mermaid format showing the relationships between assemblies.
    /// </summary>
    public string CreateDependencyDiagram()
    {
        Done();

        var sb = new StringBuilder()
            .AppendLine("stateDiagram-v2");

        var done = new HashSet<string>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded).OrderBy(a => a.Name))
        {
            done.Clear();
            foreach (var sym in asm.Symbols.OrderBy(sym => sym.Name))
            {
                foreach (var rs in sym.ReferencedSymbols.Where(rs => rs.Assembly != asm && rs.Assembly.Loaded).OrderBy(rs => rs.Name))
                {
                    if (done.Add(rs.Assembly.Name))
                    {
                        _ = sb.AppendLine(CultureInfo.InvariantCulture, $"    {asm.Name.Replace('-', '_')} --> {rs.Assembly.Name.Replace('-', '_')}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    public void Dump(TextWriter output)
    {
        Done();

        foreach (var asm in _assemblies.Values.OrderBy(a => a.Name))
        {
            output.Write("ASSEMBLY ");
            output.Write(asm.Name);
            output.WriteLine(".dll");

            if (!asm.Loaded)
            {
                output.WriteLine("  UNPROCESSED ASSEMBLY");
                continue;
            }

            foreach (var sym in asm.Symbols.OrderBy(s => s.Name))
            {
                output.Write("  ");
                output.Write(sym.Name);
                output.Write(" [");
                output.Write(sym.Kind.ToString().ToUpperInvariant());

                output.Write(sym.Marked ? ", ALIVE" : ", DEAD");
                output.WriteLine(sym.Root ? ", ROOT]" : ", NOT ROOT]");

                if (sym.ReferencedSymbols.Count > 0)
                {
                    output.WriteLine("    DIRECTLY REFERENCES");
                    foreach (var s in sym.ReferencedSymbols)
                    {
                        output.Write("      ");
                        output.WriteLine(s.Name);
                    }
                }

                if (sym.UnhomedReferencedMethods.Count > 0)
                {
                    output.WriteLine("    UNHOMED REFERENCES");
                    foreach (var m in sym.UnhomedReferencedMethods)
                    {
                        output.Write("      ");
                        output.WriteLine(m);
                    }
                }

                if (sym.Referencers.Count > 0)
                {
                    output.WriteLine("    DIRECTLY REFERENCED BY");
                    foreach (var s in sym.Referencers)
                    {
                        output.Write("      ");
                        output.WriteLine(s.Name);
                    }
                }
            }
        }
    }
}
