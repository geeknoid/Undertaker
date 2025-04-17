using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Numerics;
using System.Text;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using Undertaker.Graph;

namespace Undertaker;

internal static class Program
{
    private const int MaxConcurrentAssemblyLoads = 16;
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
                "Path of the report on dead symbols to produce"),

            new Option<string>(
                ["-ar", "--alive-report"],
                "Path of the report on alive symbols to produce"),

            new Option<string>(
                ["-npr", "--needlessly-public-report"],
                "Path of the report on needlessly public symbols to produce"),

            new Option<string>(
                ["-alc", "--assembly-layer-cake"],
                "Path of the assembly layer cake file to produce"),

            new Option<string>(
                ["-gd", "--graph-dump"],
                "Path of the graph dump file to produce"),

            new Option<bool>(
                ["-cle", "--continue-on-load-errors"],
                "Proceed to the analysis and output phases even if some assemblies didn't load"),

            new Option<bool>(
                ["-v", "--verbose"],
                "Output progress reports"),
        };

        rootCommand.Handler = CommandHandler.Create<UndertakerArgs>(ExecuteAsync);

        return rootCommand.InvokeAsync(args);
    }

    private static async Task<int> ExecuteAsync(UndertakerArgs args)
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
                Error($"Unable to read root assembly file {args.RootAssemblies.FullName}: {ex.Message}");
                return 1;
            }
        }

        // load the assemblies
        int successCount = 0;
        int errorCount = 0;
        int skipCount = 0;

        var files = new Queue<FileInfo>(args.Assemblies!.GetFiles("*.dll", SearchOption.AllDirectories));
        var tasks = new HashSet<Task<CSharpDecompiler>>(MaxConcurrentAssemblyLoads);
        var map = new Dictionary<Task<CSharpDecompiler>, FileInfo>(MaxConcurrentAssemblyLoads);

        while (files.Count > 0)
        {
            while (tasks.Count < tasks.Capacity && files.Count > 0)
            {
                var file = files.Dequeue();
                var task = Task.Run(() =>
                {
                    Out($"Loading assembly {file.FullName}");

                    return new CSharpDecompiler(file.FullName, new DecompilerSettings
                    {
                        AutoLoadAssemblyReferences = false,
                        LoadInMemory = false,
                        ThrowOnAssemblyResolveErrors = false,
                    });
                });

                _ = tasks.Add(task);
                map.Add(task, file);
            }

            var t = await Task.WhenAny(tasks);
            await CompleteTask(t);
            _ = tasks.Remove(t);
        }

        await foreach (var task in Task.WhenEach(tasks))
        {
            await CompleteTask(task);
        }

        if (errorCount > 0)
        {
            if (args.ContinueOnLoadErrors)
            {
                Warn($"Unable to load {errorCount} assemblies, ignoring");
            }
            else
            {
                Error($"Unable to load {errorCount} assemblies, exiting");
                return 1;
            }
        }

        Out($"Done loading assemblies: loaded {successCount}, skipped {skipCount}, failed {errorCount}");

        ProduceDeadReport();
        ProduceAliveReport();
        ProduceNeedlesslyPublicReport();
        ProduceAssemblyLayerCake();
        ProduceGraphDump();

        if (args.DeadReport == null && args.AliveReport == null && args.NeedlesslyPublicReport == null && args.AssemblyLayerCake == null && args.GraphDump == null)
        {
            Out("No output requested");
        }

        return 0;

        async Task CompleteTask(Task<CSharpDecompiler> task)
        {
            var file = map[task];
            _ = map.Remove(task);

            try
            {
                var decomp = await task;
                graph.LoadAssembly(decomp);
                successCount++;
            }
            catch (BadImageFormatException)
            {
                Warn($"{file.FullName} is not a .NET assembly, ignoring");
                skipCount++;
            }
            catch (MetadataFileNotSupportedException)
            {
                Warn($"{file.FullName} is not a .NET assembly, ignoring");
                skipCount++;
            }
            catch (Exception ex)
            {
                Error($"Unable to load assembly {file.FullName}: {ex.Message}");
                errorCount++;
            }
        }

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
                    Out($"Output report on dead symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on dead symbols to {path}: {ex.Message}");
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
                    Out($"Output report on alive symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on alive symbols to {path}: {ex.Message}");
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
                    Out($"Output report on needlessly public symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on needlessly public symbols to {path}: {ex.Message}");
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
                    Out($"Output assembly layer cake to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output assembly layer cake to {path}: {ex.Message}");
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
                    Out($"Output graph dump to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write graph dump to {path}: {ex.Message}");
                }
            }
        }

        void Out(string message)
        {
            if (args.Verbose)
            {
                Console.WriteLine(message);
            }
        }

        void Warn(string message)
        {
            Console.WriteLine("WARN: " + message);
        }

        void Error(string message)
        {
            Console.Error.WriteLine("ERROR: " + message);
        }   
    }
}
