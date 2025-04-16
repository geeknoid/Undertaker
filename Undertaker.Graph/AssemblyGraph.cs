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

    private Assembly GetAssembly(string assemblyName, Version assemblyVersion)
    {
        if (!_assemblies.TryGetValue(assemblyName, out var asm))
        {
            asm = new Assembly(assemblyName, assemblyVersion, _rootAssemblies.Contains(assemblyName));
            _assemblies[assemblyName] = asm;
        }

        return asm;
    }

    /// <summary>
    /// Gets information about the dead symbols in the graph.
    /// </summary>
    /// <remarks>Dead symbols are ones which aren't reachable from the various roots known to the graph.</remarks>
    public DeadReport CollectDeadReport()
    {
        MarkUsedSymbols();

        var assemblies = new List<DeadSymbols>();
        foreach (var asm in _assemblies.Values)
        {
            if (!asm.Loaded)
            {
                // unprocessed referenced assembly, skip it
                continue;
            }

            var deadTypes = new List<string>();
            var deadMembers = new List<string>();

            foreach (var sym in asm.Symbols.Values)
            {
                if (sym.Kind != SymbolKind.Type || sym.Hidden)
                {
                    continue;
                }

                if (sym.Marked)
                {
                    foreach (var member in sym.Children)
                    {
                        if (member.Marked || member.Hidden || member.Kind == SymbolKind.Type)
                        {
                            continue;
                        }

                        deadMembers.Add(member.Name);
                    }
                }
                else
                {
                    deadTypes.Add(sym.Name);
                }
            }

            deadTypes.Sort();
            deadMembers.Sort();
            assemblies.Add(new(asm.Name, deadTypes, deadMembers));
        }

        assemblies.Sort((x, y) => string.CompareOrdinal(x.Assembly, y.Assembly));
        return new(assemblies);
    }

    /// <summary>
    /// Gets information about the alive symbols in the graph.
    /// </summary>
    /// <remarks>Alive symbols are ones which are reachable from the various roots known to the graph.</remarks>
    public AliveReport CollectAliveReport()
    {
        MarkUsedSymbols();

        var assemblies = new List<AliveSymbols>();
        foreach (var asm in _assemblies.Values)
        {
            if (!asm.Loaded)
            {
                // unprocessed referenced assembly, skip it
                continue;
            }

            var aliveTypes = new List<SymbolReferences>();
            var aliveMembers = new List<SymbolReferences>();

            foreach (var sym in asm.Symbols.Values)
            {
                if (sym.Kind != SymbolKind.Type || sym.Hidden)
                {
                    continue;
                }

                if (sym.Marked)
                {
                    var because = sym.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x).ToList();
                    aliveTypes.Add(new SymbolReferences(sym.Name, because, sym.Root));

                    foreach (var member in sym.Children)
                    {
                        if (member.Marked && !member.Hidden)
                        {
                            because = member.Referencers.Where(x => x.Marked).Select(x => x.Name).OrderBy(x => x).ToList();
                            aliveMembers.Add(new SymbolReferences(member.Name, because, member.Root));
                        }
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

    private void MarkUsedSymbols()
    {
        foreach (var asm in _assemblies.Values)
        {
            if (!asm.Loaded)
            {
                continue;
            }

            foreach (var sym in asm.Symbols.Values)
            {
                if (!sym.Root)
                {
                    continue;
                }

                sym.Mark();
            }
        }
    }

    public override string ToString()
    {
        MarkUsedSymbols();

        var sb = new StringBuilder();

        foreach (var asm in _assemblies.Values)
        {
            _ = sb.Append("ASSEMBLY ").Append(asm.Name).AppendLine(".dll");

            if (!asm.Loaded)
            {
                _ = sb.AppendLine("  UNPROCESSED ASSEMBLY");
                continue;
            }

            foreach (var sym in asm.Symbols.Values)
            {
                _ = sb.Append("  ").Append(sym.Name).Append(" [").Append(sym.Kind.ToString().ToUpperInvariant());
                _ = sym.Marked ? sb.Append(", ALIVE") : sb.Append(", DEAD");
                _ = sym.Root ? sb.AppendLine(", ROOT]") : sb.AppendLine(", NOT ROOT]");

                if (sym.ReferencedSymbols.Count > 0)
                {
                    _ = sb.AppendLine("    DIRECTLY REFERENCES");
                    foreach (var s in sym.ReferencedSymbols.Values)
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
