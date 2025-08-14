using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Emits reports on a processed graph of assemblies.
/// </summary>
public sealed class Reporter
{
    private readonly Dictionary<string, Assembly> _assemblies;
    private readonly SymbolTable _symbolTable;

    internal Reporter(Dictionary<string, Assembly> assemblies, SymbolTable symbolTable)
    {
        _assemblies = assemblies;
        _symbolTable = symbolTable;
    }

    /// <summary>
    /// Gets information about the dead symbols in the graph.
    /// </summary>
    /// <remarks>Dead symbols are ones which aren't reachable from the various roots known to the graph.</remarks>
    public DeadReport CollectDeadSymbols()
    {
        var assemblies = new List<DeadReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            List<DeadReportSymbol>? deadTypes = null;
            List<DeadReportSymbol>? deadMembers = null;

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide).Cast<TypeSymbol>())
            {
                if (sym.Marked)
                {
                    foreach (var member in sym.Members.Select(_symbolTable.GetSymbol).Where(member => !member.Marked && !member.Hide && member.Kind != SymbolKind.Type))
                    {
                        var kind = "Method";
                        if (member is FieldSymbol)
                        {
                            kind = "Field";
                        }
                        else if (member is EventSymbol)
                        {
                            kind = "Event";
                        }

                        deadMembers ??= [];
                        deadMembers.Add(new DeadReportSymbol(member.Name, kind));
                    }
                }
                else
                {
                    deadTypes ??= [];
                    deadTypes.Add(new DeadReportSymbol(sym.Name, sym.TypeKind.ToString()));
                }
            }

            if (deadTypes != null || deadMembers != null)
            {
                deadTypes?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                deadMembers?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

                IReadOnlyList<DeadReportSymbol>? dt = deadTypes;
                dt ??= [];

                IReadOnlyList<DeadReportSymbol>? dm = deadMembers;
                dm ??= [];

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
    public AliveReport CollectAliveSymbols()
    {
        var assemblies = new List<AliveReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            List<AliveReportSymbol>? aliveTypes = null;
            List<AliveReportSymbol>? aliveMembers = null;

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide && sym.Marked).Cast<TypeSymbol>())
            {
                var dependents = sym.Referencers.Select(_symbolTable.GetSymbol).Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x).ToList();

                aliveTypes ??= [];
                aliveTypes.Add(new(sym.Name, dependents, sym.Root));

                foreach (var member in sym.Members.Select(_symbolTable.GetSymbol))
                {
                    if (member.Marked)
                    {
                        dependents = [.. member.Referencers.Select(_symbolTable.GetSymbol).Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x)];

                        aliveMembers ??= [];
                        aliveMembers.Add(new(member.Name, dependents, member.Root));
                    }
                }
            }

            if (aliveTypes != null || aliveMembers != null)
            {
                aliveTypes?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                aliveMembers?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

                IReadOnlyList<AliveReportSymbol>? at = aliveTypes;
                at ??= [];

                IReadOnlyList<AliveReportSymbol>? am = aliveMembers;
                am ??= [];

                assemblies.Add(new(asm.Name, at, am));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets information about the alive symbols in the graph that are kept alive strictly by test methods.
    /// </summary>
    public AliveReport CollectAliveByTestSymbols()
    {
        var assemblies = new List<AliveReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            List<AliveReportSymbol>? aliveTypes = null;
            List<AliveReportSymbol>? aliveMembers = null;

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide && sym.Marked).Cast<TypeSymbol>())
            {
                var dependents = sym.Referencers.Select(_symbolTable.GetSymbol).Where(x => x.Marked && x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(x => x.IsTestMethod).Select(x => x.Name).OrderBy(x => x).ToList();
                if (dependents.Count == 0)
                {
                    continue;
                }

                aliveTypes ??= [];
                aliveTypes.Add(new(sym.Name, dependents, sym.Root));

                foreach (var member in sym.Members.Select(_symbolTable.GetSymbol))
                {
                    if (member.Marked && !member.Hide)
                    {
                        dependents = [.. member.Referencers.Select(_symbolTable.GetSymbol).Where(x => x.Marked && x.Kind == SymbolKind.Method).Cast<MethodSymbol>().Where(x => x.IsTestMethod).Select(x => x.Name).OrderBy(x => x)];
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
                aliveTypes?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                aliveMembers?.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

                IReadOnlyList<AliveReportSymbol>? at = aliveTypes;
                at ??= [];

                IReadOnlyList<AliveReportSymbol>? am = aliveMembers;
                am ??= [];

                assemblies.Add(new(asm.Name, at, am));
            }
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets the set of symbols which could be made internal.
    /// </summary>
    public NeedlesslyPublicReport CollectNeedlesslyPublicSymbols()
    {
        var assemblies = new List<NeedlesslyPublicReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            List<string>? affectedTypes = null;
            List<string>? affectedMembers = null;

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).Where(sym => !sym.Hide && !sym.Root && sym.IsPublic))
            {
                bool usedOutside = false;
                foreach (var r in sym.Referencers.Select(_symbolTable.GetSymbol))
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
                        affectedTypes.Add(sym.Name);
                    }
                    else
                    {
                        affectedMembers ??= [];
                        affectedMembers.Add(sym.Name);
                    }
                }
            }

            if (affectedTypes != null || affectedTypes != null)
            {
                affectedTypes?.Sort();
                affectedMembers?.Sort();

                IReadOnlyList<string>? at = affectedTypes;
                at ??= [];

                IReadOnlyList<string>? am = affectedMembers;
                am ??= [];

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
        var result = new List<string>();

        var aliveReport = CollectAliveSymbols();
        foreach (var asm in aliveReport.Assemblies.Where(asm => asm.AliveTypes.Count == 0 && asm.AliveMembers.Count == 0))
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
        return [.. _assemblies.Values.Where(asm => !asm.Loaded).Select(asm => asm.Name)];
    }

    /// <summary>
    /// Returns a list of assemblies which were seen multiple times.
    /// </summary>
    public IReadOnlyList<DuplicateAssemblyReport> CollectDuplicateAssemblies()
    {
        var result = new List<DuplicateAssemblyReport>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && asm.Duplicates.Count > 0))
        {
            result.Add(new DuplicateAssemblyReport(asm.Name, asm.Version!, asm.Duplicates));
        }

        return result;
    }

    /// <summary>
    /// Returns a list of assemblies which have uses of [InternalsVisibleTo] that could be removed. 
    /// </summary>
    public IReadOnlyList<NeedlessInternalsVisibleToReport> CollectNeedlessInternalsVisibleTo()
    {
        var result = new List<NeedlessInternalsVisibleToReport>();

        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            var otherAssemblies = new List<string>();

            foreach (var other in asm.InternalsVisibleTo.Where(other => other.Loaded))
            {
                bool usesInternals = false;
                foreach (var sym in other.Symbols.Select(_symbolTable.GetSymbol))
                {
                    foreach (var refSym in sym.ReferencedSymbols.Select(_symbolTable.GetSymbol))
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
        // Step 1: Build a dependency map for each assembly
        var dependencies = new Dictionary<string, HashSet<string>>();
        foreach (var asm in _assemblies.Values)
        {
            var referenced = new HashSet<string>();
            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol))
            {
                foreach (var rs in sym.ReferencedSymbols.Select(_symbolTable.GetSymbol).Where(rs => rs.Assembly != asm))
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
        var sb = new StringBuilder()
            .AppendLine("stateDiagram-v2");

        var done = new HashSet<string>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded).OrderBy(a => a.Name))
        {
            done.Clear();
            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).OrderBy(sym => sym.Name))
            {
                foreach (var rs in sym.ReferencedSymbols.Select(_symbolTable.GetSymbol).Where(rs => rs.Assembly != asm && rs.Assembly.Loaded).OrderBy(rs => rs.Name))
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
        foreach (var asm in _assemblies.Values.OrderBy(a => a.Name))
        {
            output.Write("ASSEMBLY ");
            output.Write(asm.Name);
            output.Write(".dll");

            if (!asm.Loaded)
            {
                output.WriteLine(" [!LOADED]");
                continue;
            }

            output.Write(" [LOADED");
            output.Write(asm.IsRootAssembly ? ", ROOT" : ", !ROOT");
            output.WriteLine(asm.IsSystemAssembly ? ", SYSTEM]" : ", !SYSTEM]");

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).OrderBy(s => s.Name))
            {
                output.Write("  ");
                output.Write(sym.Name);
                output.Write(" [");
                output.Write(sym.Kind.ToString().ToUpperInvariant());

                output.Write(sym.Marked ? ", ALIVE" : ", DEAD");
                output.Write(sym.Hide ? ", HIDE" : ", !HIDE");
                output.Write(sym.Pinned ? ", PINNED" : ", !PINNED");
                output.WriteLine(sym.Root ? ", ROOT]" : ", !ROOT]");

                if (sym.ReferencedSymbols.Count > 0)
                {
                    output.WriteLine("    DIRECTLY REFERENCES");
                    foreach (var s in sym.ReferencedSymbols.Select(_symbolTable.GetSymbol))
                    {
                        output.Write("      ");
                        output.WriteLine(s.Name);
                    }
                }

                if (sym.Referencers.Count > 0)
                {
                    output.WriteLine("    DIRECTLY REFERENCED BY");
                    foreach (var s in sym.Referencers.Select(_symbolTable.GetSymbol))
                    {
                        output.Write("      ");
                        output.WriteLine(s.Name);
                    }
                }
            }
        }
    }
}
