using System.Globalization;
using System.Reflection;
using System.Reflection.PortableExecutable;
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
/// Register all root, reflection, and test policies before merging any assembly.
/// A merge or finalization failure permanently invalidates the graph; create a new graph to retry.
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
    private GraphState _state;
    private readonly HashSet<(SymbolId Declaration, SymbolId Implementation)> _dispatchLinks = [];
    private readonly HashSet<(SymbolId Owner, SymbolId Accessor)> _reflectionAccessorLinks = [];
    private enum GraphState { Registration, Building, Finalizing, Finalized, Failed }
    internal SymbolTable SymbolTable { get; } = new();
    internal Assembly UnhomedAssembly { get; } = new Assembly("$$UNHOMED$$", root: false);

    public AssemblyGraph()
    {
        _assemblies[UnhomedAssembly.Name] = UnhomedAssembly;
    }

    /// <summary>
    /// Inspects the file's metadata identity and records its actual version if already loaded.
    /// </summary>
    /// <exception cref="BadImageFormatException">The file is not a managed assembly.</exception>
    /// <exception cref="InvalidOperationException">The graph has failed or finalization has started.</exception>
    public bool AssemblyLoaded(string path)
    {
        EnsureBuildingAllowed();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException("The file does not contain an assembly manifest.");
        }

        var definition = metadata.GetAssemblyDefinition();
        var name = metadata.GetString(definition.Name);
        if (_assemblies.TryGetValue(name, out var asm) && asm.Loaded)
        {
            asm.AddDuplicate(path, definition.Version);
            return true;
        }

        return false;
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
    /// <exception cref="InvalidOperationException">Assembly merging or entity creation has already started.</exception>
    public void RecordRootAssembly(string assemblyName)
    {
        EnsureRegistrationAllowed();

        _ = _rootAssemblies.Add(assemblyName);
    }

    /// <summary>
    /// Indicates a particular symbol should be treated as though it is accessed via reflection.
    /// </summary>
    /// <remarks>
    /// Names use metadata reflection names. Method names include a parenthesized parameter-type list
    /// and generic methods include a double-backtick arity suffix before that list.
    /// Indexer properties also include a parenthesized parameter-type list.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Assembly merging or entity creation has already started.</exception>
    public void RecordReflectionSymbol(string assemblyName, string symbolName)
    {
        EnsureRegistrationAllowed();

        if (!_reflectionSymbols.TryGetValue(assemblyName, out var symbols))
        {
            symbols = [];
            _reflectionSymbols[assemblyName] = symbols;
        }

        _ = symbols.Add(symbolName);
    }

    /// <summary>
    /// Registers an attribute that marks a test method.
    /// </summary>
    /// <exception cref="InvalidOperationException">Assembly merging or entity creation has already started.</exception>
    public void RecordTestMethodAttribute(string attributeName)
    {
        EnsureRegistrationAllowed();

        _ = _testMethodAttributes.Add(attributeName);
    }

    /// <summary>
    /// Registers an attribute that marks a symbol as being accessed via reflection.
    /// </summary>
    /// <exception cref="InvalidOperationException">Assembly merging or entity creation has already started.</exception>
    public void RecordReflectionMarkerAttribute(string attributeName)
    {
        EnsureRegistrationAllowed();

        _ = _reflectionMarkerAttributes.Add(attributeName);
    }

    /// <summary>
    /// Merge a new asssembly into the graph.
    /// </summary>
    /// <remarks>Registration must be completed before the first merge. A failed merge permanently invalidates this graph.</remarks>
    /// <exception cref="InvalidOperationException">The graph has failed or finalization has started.</exception>
    public bool MergeAssembly(LoadedAssembly la)
    {
        EnsureBuildingAllowed();
        _state = GraphState.Building;
        try
        {
            var result = AssemblyProcessor.Merge(this, la);

            // Reclaim excess capacity periodically during large scans.
            if (_assemblies.Count % 256 == 0)
            {
                TrimExcess();
            }
            return result;
        }
        catch
        {
            _state = GraphState.Failed;
            throw;
        }
    }

    private void EnsureRegistrationAllowed()
    {
        if (_state != GraphState.Registration)
        {
            throw new InvalidOperationException("Graph policies must be registered before the first merge or entity creation.");
        }
    }

    private void EnsureBuildingAllowed()
    {
        if (_state is not (GraphState.Registration or GraphState.Building))
        {
            throw new InvalidOperationException("Cannot load assemblies after graph finalization has started or an operation has failed.");
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
        if (_state == GraphState.Registration)
        {
            _state = GraphState.Building;
        }
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

    internal void RecordDispatchLink(Symbol declaration, Symbol implementation) =>
        _dispatchLinks.Add((declaration.Id, implementation.Id));

    internal void RecordReflectionAccessor(Symbol owner, Symbol accessor) =>
        _reflectionAccessorLinks.Add((owner.Id, accessor.Id));

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
        log("Linking dispatch declarations to metadata-resolved implementations");
        foreach (var (declarationId, implementationId) in _dispatchLinks)
        {
            var declaration = SymbolTable.GetSymbol(declarationId);
            var implementation = SymbolTable.GetSymbol(implementationId);
            declaration.RecordReferencedSymbol(implementation);
            if (!declaration.Assembly.Loaded && declaration.DeclaringType is { } declaringType)
            {
                SymbolTable.GetSymbol(declaringType).RecordReferencedSymbol(implementation);
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

        foreach (var (ownerId, accessorId) in _reflectionAccessorLinks)
        {
            var owner = SymbolTable.GetSymbol(ownerId);
            if (owner.ReflectionTarget)
            {
                owner.RecordReferencedSymbol(SymbolTable.GetSymbol(accessorId));
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

            if (currentLayer.Count == 0)
            {
                // uh ho, we have a cycle, so give up
                layers.Add(["CIRCULAR REFERENCES DETECTED, ABORTING"]);
                break;
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
    /// <remarks>A failed finalization permanently invalidates this graph. Successful finalization is idempotent.</remarks>
    /// <exception cref="InvalidOperationException">An earlier merge or finalization failed, or finalization is already running.</exception>
    public Reporter Done(Action<string> log)
    {
        if (_state == GraphState.Finalized)
        {
            return new Reporter(_assemblies, SymbolTable, _layerCake!, _dependencyDiagram!);
        }
        EnsureBuildingAllowed();
        ArgumentNullException.ThrowIfNull(log);
        _state = GraphState.Finalizing;
        try
        {
            TrimExcess();
            HackSystemTypes();

            HandleUnhomedReferences(log);
            foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
            {
                asm.SnapshotDependencies(this);
            }

            // Synthetic dispatch edges must not affect real assembly dependency snapshots.
            _layerCake = CreateAssemblyLayerCake();
            _dependencyDiagram = CreateDependencyDiagram();

            HookupDerivedSymbols(log);
            PropageteReflectionTarget(log);
            MarkUsedSymbols(log);
            HandleConstants(log);
            _state = GraphState.Finalized;
        }
        catch
        {
            _state = GraphState.Failed;
            throw;
        }

        return new Reporter(_assemblies, SymbolTable, _layerCake, _dependencyDiagram!);
    }
}
