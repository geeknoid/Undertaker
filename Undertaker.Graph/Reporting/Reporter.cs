using System.Globalization;
using System.Text;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Emits reports on a processed graph of assemblies.
/// </summary>
public sealed class Reporter
{
    private readonly Dictionary<string, Assembly> _assemblies;

    internal Reporter(Dictionary<string, Assembly> assemblies)
    {
        _assemblies = assemblies;
    }

    /// <summary>
    /// Gets information about the dead symbols in the graph.
    /// </summary>
    /// <remarks>Dead symbols are ones which aren't reachable from the various roots known to the graph.</remarks>
    public GraphReport CollectDeadSymbols()
    {
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
                        deadMembers.Add(new(member.Name, [], member.Root));
                    }
                }
                else
                {
                    deadTypes ??= [];
                    deadTypes.Add(new(sym.Name, [], sym.Root));
                }
            }

            if (deadTypes != null || deadMembers != null)
            {
                deadTypes?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
                deadMembers?.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));

                IReadOnlyList<GraphReportSymbol>? dt = deadTypes;
                dt ??= [];

                IReadOnlyList<GraphReportSymbol>? dm = deadMembers;
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
    public GraphReport CollectAliveSymbols()
    {
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
                at ??= [];

                IReadOnlyList<GraphReportSymbol>? am = aliveMembers;
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
    public GraphReport CollectAliveByTestSymbols()
    {
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
                at ??= [];

                IReadOnlyList<GraphReportSymbol>? am = aliveMembers;
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
    public GraphReport CollectNeedlesslyPublicSymbols()
    {
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
                at ??= [];

                IReadOnlyList<GraphReportSymbol>? am = affectedMembers;
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
        return [.. _assemblies.Values.Where(asm => !asm.Loaded).Select(asm => asm.Name)];
    }

    /// <summary>
    /// Returns a list of assemblies which were seen multiple times.
    /// </summary>
    public IReadOnlyList<DuplicateAssemnblyReport> CollectDuplicateAssemblies()
    {
        var result = new List<DuplicateAssemnblyReport>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && asm.Duplicates.Count > 0))
        {
            result.Add(new DuplicateAssemnblyReport(asm.Name, asm.Version!, asm.Duplicates));
        }

        return result;
    }

    /// <summary>
    /// Returns a list of assemblies which have uses of [InternalsVisibleTo] that could be removed. 
    /// </summary>
    public IReadOnlyList<NeedlessInternalsVisibleToReport> CollectNeedlessInternalsVisibleTo()
    {
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
