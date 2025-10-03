using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;
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
    private readonly HashSet<string> _reflectionMarkerAttributes = [];
    private readonly Dictionary<string, HashSet<string>> _reflectionSymbols = [];
    private List<IReadOnlyList<string>>? _layerCake;
    private string? _dependencyDiagram;
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
        if (_layerCake != null)
        {
            throw new InvalidOperationException("Cannot add root assemblies after the graph has been finalized.");
        }

        _ = _rootAssemblies.Add(assemblyName);
    }

    /// <summary>
    /// Indicates a particular symbol should be treated as though it is accessed via reflection.
    /// </summary>
    public void RecordReflectionSymbol(string assemblyName, string symbolName)
    {
        if (_layerCake != null)
        {
            throw new InvalidOperationException("Cannot add root assemblies after the graph has been finalized.");
        }

        if (!_reflectionSymbols.TryGetValue(assemblyName, out var symbols))
        {
            symbols = [];
            _reflectionSymbols[assemblyName] = symbols;
        }

        _ = symbols.Add(symbolName);
    }

    public void RecordTestMethodAttribute(string attributeName)
    {
        if (_layerCake != null)
        {
            throw new InvalidOperationException("Cannot add test method attributes after the graph has been finalized.");
        }

        _ = _testMethodAttributes.Add(attributeName);
    }

    public void RecordReflectionMarkerAttribute(string attributeName)
    {
        if (_layerCake != null)
        {
            throw new InvalidOperationException("Cannot add reflection marker attributes after the graph has been finalized.");
        }

        _ = _reflectionMarkerAttributes.Add(attributeName);
    }

    /// <summary>
    /// Merge a new asssembly into the graph.
    /// </summary>
    public void MergeAssembly(LoadedAssembly la)
    {
        if (_layerCake != null)
        {
            throw new InvalidOperationException("Cannot merge new assemblies after the graph has been finalized.");
        }

        AssemblyProcessor.Merge(this, la);

        // every once in a while, try to reclaim wasted space so we minimize RAM usage
        if (_assemblies.Count % 256 == 0)
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
    internal bool IsReflectionMarkerAttribute(string attributeName) => _reflectionMarkerAttributes.Contains(attributeName);
    internal bool IsReflectionSymbol(string assemblyName, string symbolName) => _reflectionSymbols.TryGetValue(assemblyName, out var symbols) && symbols.Contains(symbolName);

    private void MarkUsedSymbols(Action<string> log)
    {
        log("Marking alive symbols...");
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol))
            {
                if (sym.Root || sym.ReflectionTarget)
                {
                    sym.Mark(this);
                }
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
        foreach (var asm in _assemblies.Values)
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.TypeKind == TypeKind.Interface))
            {
                foreach (var ifaceMember in sym.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                {
                    foreach (var derivedType in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                    {
                        foreach (var derivedMember in derivedType.Members.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Method).Cast<MethodSymbol>())
                        {
                            if (ifaceMember.SimilarSignature(derivedMember))
                            {
                                ifaceMember.RecordReferencedSymbol(derivedMember);
                                break;
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
                    foreach (var derived in sym.DerivedTypes.Select(SymbolTable.GetSymbol).Cast<TypeSymbol>())
                    {
                        foreach (var derivedMember in derived.Members.Select(SymbolTable.GetSymbol).Where(x => x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(member => member.IsOverride))
                        {
                            if (member.SimilarSignature(derivedMember))
                            {
                                member.RecordReferencedSymbol(derivedMember);
                                break;
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
                            if (ifaceMember.SimilarSignature(derivedMember))
                            {
                                sym.RecordReferencedSymbol(derivedMember);
                                break;
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
                            if (classMember.SimilarSignature(derivedMember))
                            {
                                sym.RecordReferencedSymbol(derivedMember);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    private void PropageteReflectionTarget(Action<string> log)
    {
        log("Propagating reflection targets from types to members");

        foreach (var asm in _assemblies.Values)
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(sym => sym.ReflectionTarget))
            {
                foreach (var member in sym.Members.Select(SymbolTable.GetSymbol))
                {
                    member.SetReflectionTarget();
                }
            }
        }
    }

    private void HandleConstants(Action<string> log)
    {
        log("Dealing with constant declarations");

        foreach (var asm in _assemblies.Values)
        {
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>().Where(type => type.DeclaresConstants && !type.Marked))
            {
                // a class is dead but has public constants, so we mark it to avoid false positives
                // (since we generally can't tell when code is accessing constants by looking at IL)
                sym.Mark(this);
            }
        }
    }

    /// <summary>
    /// This provides the definition for system types which we might not encouter during analysis
    /// </summary>
    /// <remarks>
    /// This is to track any methods that the .NET runtime might be calling on interfaces supplied by the 
    /// assemblies under analysis.
    /// </remarks>
    private void HackSystemTypes()
    {
        foreach (var asm in _assemblies.Values.Where(asm => !asm.Loaded && asm.IsSystemAssembly))
        {
            var method_additions = new List<(TypeSymbol, string)>();
            var property_additions = new List<(TypeSymbol, string)>();
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type).Cast<TypeSymbol>())
            {
                var name = sym.Name;
                if (!name.StartsWith("System."))
                {
                    continue;
                }

                if (name == "System.Runtime.CompilerServices.IAsyncStateMachine")
                {
                    method_additions.Add((sym, "System.Runtime.CompilerServices.IAsyncStateMachine.MoveNext()"));
                    method_additions.Add((sym, "System.Runtime.CompilerServices.IAsyncStateMachine.SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine)"));
                }
                else if (name == "System.IDisposable")
                {
                    method_additions.Add((sym, "System.IDisposable.Dispose()"));
                }
                else if (name == "System.Collections.IEnumerable")
                {
                    method_additions.Add((sym, "System.Collections.IEnumerable.GetEnumerator()"));
                }
                else if (name == "System.Collections.Generic.IEnumerable`1")
                {
                    method_additions.Add((sym, "System.Collections.Generic.IEnumerable`1.GetEnumerator()"));
                }
                else if (name == "System.Collections.ICollection")
                {
                    method_additions.Add((sym, "System.Collections.ICollection.CopyTo(System.Array,System.Int32)"));
                    method_additions.Add((sym, "System.Collections.ICollection.get_count()"));
                    property_additions.Add((sym, "System.Collections.ICollection.count"));
                }
                else if (name == "System.Collections.Generic.ICollection`1")
                {
                    method_additions.Add((sym, "System.Collections.ICollection.CopyTo(System.Array,System.Int32)"));
                    method_additions.Add((sym, "System.Collections.ICollection.get_count()"));
                    property_additions.Add((sym, "System.Collections.ICollection.count"));
                }
                else if (name == "System.Object")
                {
                    method_additions.Add((sym, "System.Object.ToString()"));
                    method_additions.Add((sym, "System.Object.GetHashCode()"));
                    method_additions.Add((sym, "System.Object.Equals(System.Object)"));
                }
            }

            foreach (var addition in method_additions)
            {
                var method = asm.GetSymbol(this, addition.Item2, SymbolKind.Method);
                addition.Item1.AddMember(method);
            }

            foreach (var addition in property_additions)
            {
                var property = asm.GetSymbol(this, addition.Item2, SymbolKind.Property);
                addition.Item1.AddMember(property);
            }
        }
    }

    /// <summary>
    /// Creates a layer cake of assembly dependencies.
    /// </summary>
    /// <remarks>
    /// Returns a layer cake of assembly dependencies. Each layer depends only on assemblies in
    /// the layers below.
    /// </remarks>
    /// <returns>A list of layers, where each layer is a list of assembly names at that layer.</returns>
    private List<IReadOnlyList<string>> CreateAssemblyLayerCake()
    {
        var dependentCounts = new Dictionary<string, int>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            dependentCounts[asm.Name] = 0;
        }

        var dependencies = new Dictionary<string, HashSet<string>>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            var referenced = new HashSet<string>();
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol))
            {
                foreach (var rs in sym.ReferencedSymbols.Select(SymbolTable.GetSymbol).Where(rs => rs.Assembly != asm && rs.Assembly.Loaded))
                {
                    if (referenced.Add(rs.Assembly.Name))
                    {
                        dependentCounts[rs.Assembly.Name]++;
                    }
                }
            }

            referenced.TrimExcess();
            dependencies[asm.Name] = referenced;
        }

        var layers = new List<IReadOnlyList<string>>();
        var assembliesToRemove = new List<string>();
        while (dependentCounts.Count > 0)
        {
            var currentLayer = new List<string>();
            assembliesToRemove.Clear();

            // Find assemblies with no dependent
            foreach (var asm in dependentCounts.Where(dc => dc.Value == 0).Select(dc => dc.Key))
            {
                currentLayer.Add(asm);
                assembliesToRemove.Add(asm);
            }

            // Remove assemblies from the dependency map and update dependent counts
            foreach (var assemblyName in assembliesToRemove)
            {
                _ = dependentCounts.Remove(assemblyName);
                foreach (var dependency in dependencies[assemblyName])
                {
                    if (dependentCounts.TryGetValue(dependency, out int value))
                    {
                        dependentCounts[dependency] = --value;
                    }
                }

                _ = dependencies.Remove(assemblyName);
            }

            layers.Add(currentLayer);
        }

        return layers;
    }

    /// <summary>
    /// Creates a dependency diagram in Mermaid format showing the relationships between assemblies.
    /// </summary>
    private string CreateDependencyDiagram()
    {
        var sb = new StringBuilder()
            .AppendLine("stateDiagram-v2");

        var done = new HashSet<string>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded).OrderBy(a => a.Name))
        {
            done.Clear();
            foreach (var sym in asm.Symbols.Select(SymbolTable.GetSymbol).OrderBy(sym => sym.Name))
            {
                foreach (var rs in sym.ReferencedSymbols.Select(SymbolTable.GetSymbol).Where(rs => rs.Assembly != asm && rs.Assembly.Loaded).OrderBy(rs => rs.Name))
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

    /// <summary>
    /// Completes the graph and returns a reporter to extract meaning from the graph.
    /// </summary>
    /// <param name="log">Receives progress messages as the graph analysis is performed.</param>
    public Reporter Done(Action<string> log)
    {
        if (_layerCake == null)
        {
            TrimExcess();
            HackSystemTypes();

            // we need to do these first, since the code below will introduce downward links in the graph which leads to cycles
            _layerCake = CreateAssemblyLayerCake();
            _dependencyDiagram = CreateDependencyDiagram();

            HandleUnhomedReferences(log);
            HookupDerivedSymbols(log);
            PropageteReflectionTarget(log);
            MarkUsedSymbols(log);
            HandleConstants(log);
        }

        return new Reporter(_assemblies, SymbolTable, _layerCake, _dependencyDiagram!);
    }
}
