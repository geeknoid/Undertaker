﻿using System.Globalization;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;

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
        _ = _rootAssemblies.Add(assemblyName);
    }

    /// <summary>
    /// LOads a new asssembly into the graph.
    /// </summary>
    /// <param name="path">The file system path to the assembly to load.</param>
    public void LoadAssembly(string path)
    {
        var decomp = new CSharpDecompiler(path, new DecompilerSettings
        {
            AutoLoadAssemblyReferences = false,
            LoadInMemory = true,
            ThrowOnAssemblyResolveErrors = false,
        });

        LoadAssembly(decomp);
    }

    /// <summary>
    /// LOads a new asssembly into the graph.
    /// </summary>
    public void LoadAssembly(CSharpDecompiler decomp)
    {
        AssemblyLoader.Load(decomp, GetAssembly);
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

    private void MarkUsedSymbols()
    {
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

    /// <summary>
    /// Gets information about the dead symbols in the graph.
    /// </summary>
    /// <remarks>Dead symbols are ones which aren't reachable from the various roots known to the graph.</remarks>
    public GraphReport CollectDeadSymbols()
    {
        MarkUsedSymbols();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            var deadTypes = new List<GraphReportSymbol>();
            var deadMembers = new List<GraphReportSymbol>();

            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide).Cast<TypeSymbol>())
            {
                if (sym.Marked)
                {
                    foreach (var member in sym.Children.Where(member => !member.Marked && !member.Hide && member.Kind != SymbolKind.Type))
                    {
                        deadMembers.Add(new(member.Name, [], member.Root));
                    }
                }
                else
                {
                    deadTypes.Add(new(sym.Name, [], sym.Root));
                }
            }

            deadTypes.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            deadMembers.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            assemblies.Add(new(asm.Name, deadTypes, deadMembers));
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
        MarkUsedSymbols();

        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            var aliveTypes = new List<GraphReportSymbol>();
            var aliveMembers = new List<GraphReportSymbol>();

            foreach (var sym in asm.Symbols.Where(sym => sym.Kind == SymbolKind.Type && !sym.Hide && sym.Marked).Cast<TypeSymbol>())
            {
                var dependents = sym.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x).ToList();
                aliveTypes.Add(new(sym.Name, dependents, sym.Root));

                foreach (var member in sym.Children)
                {
                    if (member.Marked && !member.Hide)
                    {
                        dependents = [.. member.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x)];
                        aliveMembers.Add(new(member.Name, dependents, member.Root));
                    }
                }
            }

            aliveTypes.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            aliveMembers.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            assemblies.Add(new(asm.Name, aliveTypes, aliveMembers));
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets the set of types and symbols which could be made internal.
    /// </summary>
    public GraphReport CollectPublicSymbols()
    {
        var assemblies = new List<GraphReportAssembly>();
        foreach (var asm in _assemblies.Values.Where(asm => asm.Loaded))
        {
            var affectedTypes = new List<GraphReportSymbol>();
            var affectedMembers = new List<GraphReportSymbol>();

            foreach (var sym in asm.Symbols)
            {
                if (sym.Hide || sym.Root)
                {
                    continue;
                }

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
                        affectedTypes.Add(new(sym.Name, [], sym.Root));
                    }
                    else
                    {
                        affectedMembers.Add(new(sym.Name, [], sym.Root));
                    }
                }
            }

            affectedTypes.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            affectedMembers.Sort((x, y) => string.CompareOrdinal(x.Symbol, y.Symbol));
            assemblies.Add(new(asm.Name, affectedTypes, affectedMembers));
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

    public IReadOnlyList<NeedlessInternalsVisibleToReport> CollectInternalsVisibleTo()
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

    public string CreateDependencyDiagram()
    {
        var sb = new StringBuilder()
            .AppendLine("---")
            .AppendLine("title: Assembly Dependency Diagram")
            .AppendLine("---")
            .AppendLine()
            .AppendLine("```mermaid")
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

        return sb.
            AppendLine("```").
            ToString();
    }

    public override string ToString()
    {
        MarkUsedSymbols();

        var sb = new StringBuilder();

        foreach (var asm in _assemblies.Values.OrderBy(a => a.Name))
        {
            _ = sb.Append("ASSEMBLY ").Append(asm.Name).AppendLine(".dll");

            if (!asm.Loaded)
            {
                _ = sb.AppendLine("  UNPROCESSED ASSEMBLY");
                continue;
            }

            foreach (var sym in asm.Symbols.OrderBy(s => s.Name))
            {
                _ = sb.Append("  ").Append(sym.Name).Append(" [").Append(sym.Kind.ToString().ToUpperInvariant());
                _ = sym.Marked ? sb.Append(", ALIVE") : sb.Append(", DEAD");
                _ = sym.Root ? sb.AppendLine(", ROOT]") : sb.AppendLine(", NOT ROOT]");

                if (sym.ReferencedSymbols.Count > 0)
                {
                    _ = sb.AppendLine("    DIRECTLY REFERENCES");
                    foreach (var s in sym.ReferencedSymbols)
                    {
                        _ = sb.Append("      ").AppendLine(s.Name);
                    }
                }

                if (sym.Referencers.Count > 0)
                {
                    _ = sb.AppendLine("    DIRECTLY REFERENCED BY");
                    foreach (var s in sym.Referencers)
                    {
                        _ = sb.Append("      ").AppendLine(s.Name);
                    }
                }
            }
        }

        return sb.ToString();
    }
}
