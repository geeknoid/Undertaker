using System.Globalization;
using System.Text;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Emits reports on a processed graph of assemblies.
/// </summary>
public sealed class Reporter
{
    private readonly Dictionary<string, Assembly> _assemblies;
    private readonly SymbolTable _symbolTable;
    private readonly IReadOnlyList<IReadOnlyList<string>> _layerCake;
    private readonly string _dependencyDiagram;

    internal Reporter(Dictionary<string, Assembly> assemblies, SymbolTable symbolTable, IReadOnlyList<IReadOnlyList<string>> layerCake, string dependencyDiagram)
    {
        _assemblies = assemblies;
        _symbolTable = symbolTable;
        _layerCake = layerCake;
        _dependencyDiagram = dependencyDiagram;
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
                        deadMembers ??= [];
                        deadMembers.Add(new DeadReportSymbol(member.Name, member.Kind.ToString(), member.Access));
                    }
                }
                else
                {
                    deadTypes ??= [];
                    deadTypes.Add(new DeadReportSymbol(sym.Name, sym.TypeKind.ToString(), sym.Access));
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
    /// Gets information about the unreferenced symbols in the graph.
    /// </summary>
    /// <remarks>Unreferenced symbols are ones which aren't referenced by any other symbol in the graph.</remarks>
    public DeadReport CollectUnreferencedSymbols()
    {
        var assemblies = new List<DeadReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && !asm.IsSystemAssembly))
        {
            List<DeadReportSymbol>? deadTypes = null;
            List<DeadReportSymbol>? deadMembers = null;

            foreach (var sym in asm.Symbols.Select(_symbolTable.GetSymbol).Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide).Cast<TypeSymbol>())
            {
                if (sym.Referencers.Count > 0 || sym.Marked)
                {
                    foreach (var member in sym.Members.Select(_symbolTable.GetSymbol).Where(member => member.Referencers.Count == 0 && member.Kind != SymbolKind.Type && !member.Marked))
                    {
                        deadMembers ??= [];
                        deadMembers.Add(new DeadReportSymbol(member.Name, member.Kind.ToString(), member.Access));
                    }
                }
                else
                {
                    deadTypes ??= [];
                    deadTypes.Add(new DeadReportSymbol(sym.Name, sym.TypeKind.ToString(), sym.Access));
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
        // we don't include assemblies which were just referenced as a result of being the target of an InternalsVisibleTo attribute
        return [.. _assemblies.Values.Where(asm => !asm.Loaded && asm.Symbols.Count > 0 && !asm.IsSystemAssembly).Select(asm => asm.Name)];
    }

    /// <summary>
    /// Returns a list of assemblies which were seen multiple times.
    /// </summary>
    public IReadOnlyList<DuplicateAssemblyReport> CollectDuplicateAssemblies()
    {
        var result = new List<DuplicateAssemblyReport>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded && asm.Duplicates.Count > 0))
        {
            result.Add(new DuplicateAssemblyReport(asm.Name, asm.Version!, asm.Path!, asm.Duplicates));
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
    /// <returns>A list of layers, where each layer is a list of assembly names at that layer.</returns>
    public IReadOnlyList<IReadOnlyList<string>> CreateAssemblyLayerCake() => _layerCake;

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

    public void Dump(string path)
    {
        foreach (var asm in _assemblies.Values)
        {
            var p = Path.Combine(path, asm.Name.Trim()) + ".txt";

            using var output = File.CreateText(p);

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
                output.Write(sym.ReflectionTarget ? ", REFLECTION_TARGET" : ", !REFLECTION_TARGET");
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

            output.Close();
        }
    }
}
