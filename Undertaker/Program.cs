using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using Undertaker.Graph;

namespace Undertaker;

internal static class Program
{
    private static readonly JsonSerializerOptions _serializationOptions = new() { WriteIndented = true };

    private sealed class UndertakerArgs
    {
        public DirectoryInfo? Assemblies { get; set; }
        public FileInfo? RootAssemblies { get; set; }
        public string? DeadReport { get; set; }
        public string? AliveReport { get; set; }
        public string? NeedlesslyPublicReport { get; set; }
        public string? GraphDump { get; set; }
        public string? AssemblyLayerCake { get; set; }
        public bool ContinueOnLoadErrors { get; set; }
        public bool Verbose { get; set; }
    }

    public static Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Helps with dead code detection over a large code base")
        {
            new Argument<DirectoryInfo>("assemblies", "Path to folder containing all the assemblies to work with.").ExistingOnly(),

            new Option<FileInfo>(
                ["-ra", "--root-assemblies"],
                "Path to a text file listing assemblies to be treated as root, one assembly name per line"),

            new Option<string>(
                ["-dr", "--dead-report"],
                "Path of the dead code report file to produce"),

            new Option<string>(
                ["-ar", "--alive-report"],
                "Path of the alive code report file to produce"),

            new Option<string>(
                ["-npr", "--needlessly-public-report"],
                "Path of the needlessly public report file to produce"),

            new Option<string>(
                ["-alc", "--assembly-layer-cake"],
                "Path of the assembly layer cake file to produce"),

            new Option<string>(
                ["-gd", "--graph-dump"],
                "Path of the graph dump file to produce"),

            new Option<bool>(
                ["-ce", "--continue-on-load-errors"],
                "Proceed to the analysis and output phase even if some assemblies didn't load"),

            new Option<bool>(
                ["-v", "--verbose"],
                "Output progress reports"),
        };

        rootCommand.Handler = CommandHandler.Create<UndertakerArgs>(ExecuteAsync);

        return rootCommand.InvokeAsync(args);
    }

    private static Task<int> ExecuteAsync(UndertakerArgs args)
    {
        var graph = new AssemblyGraph();

        if (args.RootAssemblies != null)
        {
            try
            {
                var lines = File.ReadAllLines(args.RootAssemblies.FullName);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        l = l.Substring(0, l.Length - 4);
                    }

                    graph.RecordRootAssembly(l);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Unable to read root assembly file {args.RootAssemblies.FullName}: {ex.Message}");
                return Task.FromResult(1);
            }
        }

#if false
        // parallelize reading all the assemblies
        var tasks = new List<Task<CSharpDecompiler>>();
        var files = new Dictionary<Task, string>();
        foreach (var file in args.Assemblies!.EnumerateFiles("*.dll", SearchOption.AllDirectories))
        {
            var task = Task.Run(() =>
            {
                if (args.Verbose)
                {
                    Console.WriteLine($"Loading assembly {file.FullName}");
                }

                return new CSharpDecompiler(file.FullName, new DecompilerSettings
                {
                    AutoLoadAssemblyReferences = false,
                    LoadInMemory = false,
                    ThrowOnAssemblyResolveErrors = false,
                });
            });

            tasks.Add(task);
            files.Add(task, file.FullName);
        }

        // insert each loaded assembly into the graph in a single-threaded context
        int errorCount = 0;
        await foreach (var task in Task.WhenEach(tasks))
        {
            try
            {
                var decomp = await task;
                graph.LoadAssembly(decomp);
            }
            catch (BadImageFormatException)
            {
                Console.WriteLine($"WARNING: {files[task]} is not a .NET assembly, ignoring");
            }
            catch (MetadataFileNotSupportedException)
            {
                Console.WriteLine($"WARNING: {files[task]} is not a .NET assembly, ignoring");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Unable to load assembly {files[task]}: {ex.Message}");
                errorCount++;
            }
        }
#else

        int errorCount = 0;
        foreach (var file in args.Assemblies!.EnumerateFiles("*.dll", SearchOption.AllDirectories))
        {
            if (args.Verbose)
            {
                Console.WriteLine($"Loading assembly {file.FullName}");
            }

            try
            {
                var decomp = new CSharpDecompiler(file.FullName, new DecompilerSettings
                {
                    AutoLoadAssemblyReferences = false,
                    LoadInMemory = false,
                    ThrowOnAssemblyResolveErrors = false,
                });

                graph.LoadAssembly(decomp);
            }
            catch (BadImageFormatException)
            {
                Console.WriteLine($"WARNING: {file.FullName} is not a .NET assembly, ignoring");
            }
            catch (MetadataFileNotSupportedException)
            {
                Console.WriteLine($"WARNING: {file.FullName} is not a .NET assembly, ignoring");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Unable to load assembly {file.FullName}: {ex.Message}");
                errorCount++;
            }
        }

#endif

        if (errorCount > 0)
        {
            if (args.ContinueOnLoadErrors)
            {
                Console.Error.WriteLine($"ERROR: Unable to load {errorCount} assemblies, ignoring");
            }
            else
            {
                Console.Error.WriteLine($"ERROR: Unable to load {errorCount} assemblies, exiting");
                return Task.FromResult(1);
            }
        }

        if (args.Verbose)
        {
            Console.WriteLine("Done processing assemblies");
        }

        ProduceDeadReport();
        ProduceAliveReport();
        ProduceNeedlesslyPublicReport();
        ProduceAssemblyLayerCake();
        ProduceGraphDump();

        if (args.Verbose)
        {
            if (args.DeadReport == null && args.AliveReport == null && args.NeedlesslyPublicReport == null && args.AssemblyLayerCake == null && args.GraphDump == null)
            {
                Console.WriteLine("No output requested");
            }
        }

        return Task.FromResult(0);

        void ProduceDeadReport()
        {
            if (args.DeadReport != null)
            {
                var path = Path.GetFullPath(args.DeadReport);
                try
                {
                    var report = graph.CollectDeadReport();
                    var json = JsonSerializer.Serialize(report, _serializationOptions);
                    File.WriteAllText(path, json);

                    if (args.Verbose)
                    {
                        Console.WriteLine($"Output dead report to {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: Unable to write dead report to {path}: {ex.Message}");
                }
            }
        }

        void ProduceAliveReport()
        {
            if (args.AliveReport != null)
            {
                var path = Path.GetFullPath(args.AliveReport);
                try
                {
                    var report = graph.CollectAliveReport();
                    var json = JsonSerializer.Serialize(report, _serializationOptions);
                    File.WriteAllText(path, json);

                    if (args.Verbose)
                    {
                        Console.WriteLine($"Output alive report to {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: Unable to write alive report to {path}: {ex.Message}");
                }
            }
        }

        void ProduceNeedlesslyPublicReport()
        {
            if (args.NeedlesslyPublicReport != null)
            {
                var path = Path.GetFullPath(args.NeedlesslyPublicReport);
                try
                {
                    var report = graph.CollectNeedlesslyPublicReport();
                    var json = JsonSerializer.Serialize(report, _serializationOptions);
                    File.WriteAllText(path, json);

                    if (args.Verbose)
                    {
                        Console.WriteLine($"Output 'needlessly public' report to {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: Unable to write 'needlessly public' report to {path}: {ex.Message}");
                }
            }
        }

        void ProduceAssemblyLayerCake()
        {
            if (args.AssemblyLayerCake != null)
            {
                var path = Path.GetFullPath(args.AssemblyLayerCake);
                try
                {
                    var cake = graph.CreateLayerCake();
                    var json = JsonSerializer.Serialize(cake, _serializationOptions);
                    File.WriteAllText(path, json);

                    if (args.Verbose)
                    {
                        Console.WriteLine($"Output assembly layer cake to {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: Unable to output assembly layer cake to {path}: {ex.Message}");
                }
            }
        }

        void ProduceGraphDump()
        {
            if (args.GraphDump != null)
            {
                var path = Path.GetFullPath(args.GraphDump);
                try
                {
                    File.WriteAllText(path, graph.ToString());

                    if (args.Verbose)
                    {
                        Console.WriteLine($"Output graph dump to {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: Unable to write graph dump to {path}: {ex.Message}");
                }
            }
        }
    }
}
