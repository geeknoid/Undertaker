using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
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
        public string? GraphDump { get; set; }
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
                ["-gd", "--graph-dump"],
                "Path of the graph dump file to produce"),

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

        int errorCount = 0;
        foreach (var file in args.Assemblies!.EnumerateFiles("*.dll", SearchOption.AllDirectories))
        {
            try
            {
                if (args.Verbose)
                {
                    Console.WriteLine($"Loading assembly {file.FullName}");
                }

                graph.LoadAssembly(file.FullName);
            }
            catch (BadImageFormatException ex)
            {
                Console.Error.WriteLine($"ERROR: {file.FullName} is not a valid .NET assembly, skipping");
            }
            catch (MetadataFileNotSupportedException)
            {
                Console.Error.WriteLine($"ERROR: {file.FullName} is not a valid .NET assembly, skipping");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Unable to load assembly {file.FullName}: {ex.Message}");
                errorCount++;
            }
        }

        if (errorCount > 0)
        {
            Console.Error.WriteLine($"ERROR: Unable to load {errorCount} assemblies, exiting");
            return Task.FromResult(1);
        }

        if (args.Verbose)
        {
            Console.WriteLine("Done reading assemblies");
        }

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
                Console.Error.WriteLine($"ERROR: Unable to graph dump to {path}: {ex.Message}");
            }
        }

        if (args.DeadReport == null && args.AliveReport == null && args.GraphDump == null)
        {
            if (args.Verbose)
            {
                Console.WriteLine("No output requested");
            }
        }

        return Task.FromResult(0);
    }
}
