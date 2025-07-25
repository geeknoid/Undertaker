﻿using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using Undertaker.Graph;

namespace Undertaker;

internal static class Program
{
    private const int MaxConcurrentAssemblyLoads = 32;
    private static readonly JsonSerializerOptions _serializationOptions = new() { WriteIndented = true };

    private sealed class UndertakerArgs
    {
        public DirectoryInfo? AssemblyFolder { get; set; }
        public FileInfo? RootAssemblies { get; set; }
        public FileInfo? TestMethodAttributes { get; set; }
        public string? DeadSymbols { get; set; }
        public string? AliveSymbols { get; set; }
        public string? AliveByTestSymbols { get; set; }
        public string? NeedlesslyPublicSymbols { get; set; }
        public string? UnreferencedAssemblies { get; set; }
        public string? AssemblyLayerCake { get; set; }
        public string? NeedlessInternalsVisibleTo { get; set; }
        public string? DependencyDiagram { get; set; }
        public string? UnanalyzedAssemblies { get; set; }
        public string? GraphDump { get; set; }
        public bool ContinueOnLoadErrors { get; set; }
        public bool Verbose { get; set; }
        public bool DumpMemory { get; set; }
    }

    public static Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Helps with dead code detection over a large code base")
        {
            new Argument<DirectoryInfo>("assembly-folder", "Path to a folder containing all the assemblies to work with.").ExistingOnly(),

            new Option<FileInfo>(
                ["-ra", "--root-assemblies"],
                "Path to a text file listing assemblies to be treated as roots, one assembly name per line (with or without a .dll extension)"),

            new Option<FileInfo>(
                ["-tma", "--test-method-attributes"],
                "Path to a text file listing all the attributes that can mark a method as a test, one per line"),

            new Option<string>(
                ["-ds", "--dead-symbols"],
                "Path of the report to produce on dead symbols"),

            new Option<string>(
                ["-as", "--alive-symbols"],
                "Path of the report to produce on alive symbols"),

            new Option<string>(
                ["-abts", "--alive-by-test-symbols"],
                "Path of the report to produce symbols kept alive only by test methods"),

            new Option<string>(
                ["-nps", "--needlessly-public-symbols"],
                "Path of the report to produce on public symbols which could be made internal"),

            new Option<string>(
                ["-ua", "--unreferenced-assemblies"],
                "Path of the report to produce on completely unreferenced assemblies"),

            new Option<string>(
                ["-uaa", "--unanalyzed-assemblies"],
                "Path of the report to produce on assemblies which were referenced but not analyzed"),

            new Option<string>(
                ["-nivt", "--needless-internals-visible-to"],
                "Path of the report to produce on needless uses of [InternalsVisibleTo]"),

            new Option<string>(
                ["-alc", "--assembly-layer-cake"],
                "Path of the assembly layer cake to produce"),

            new Option<string>(
                ["-dd", "--dependency-diagram"],
                "Path of the Mermaid-based assembly dependency diagram to produce"),

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

        if (args.DeadSymbols == null
            && args.AliveSymbols == null
            && args.AliveByTestSymbols == null
            && args.NeedlesslyPublicSymbols == null
            && args.UnreferencedAssemblies == null
            && args.UnanalyzedAssemblies == null
            && args.NeedlessInternalsVisibleTo == null
            && args.AssemblyLayerCake == null
            && args.DependencyDiagram == null)
        {
            Out("No explicit output requested, generating default outputs");

            args.DeadSymbols = "./dead-symbols.json";
            args.AliveSymbols = "./alive-symbols.json";
            args.AliveByTestSymbols = "./alive-by-test-symbols.json";
            args.NeedlesslyPublicSymbols = "./needlessly-public-symbols.json";
            args.UnreferencedAssemblies = "./unreferenced-assemblies.json";
            args.UnanalyzedAssemblies= "./unanalyzed-assemblies.txt";
            args.NeedlessInternalsVisibleTo = "./needless-internals-visible-to.json";
            args.AssemblyLayerCake = "./assembly-layer-cake.json";
            args.DependencyDiagram = "./dependency-diagram.mmd";
        }

        if (args.RootAssemblies != null)
        {
            Out($"Loading root assembly file {args.RootAssemblies.FullName}");

            try
            {
                var lines = File.ReadAllLines(args.RootAssemblies.FullName);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l == String.Empty)
                    {
                        continue;
                    }

                    if (l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        l = l.Substring(0, l.Length - 4);
                    }
                    else if (l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
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

        if (args.TestMethodAttributes != null)
        {
            Out($"Loading test method attribute file {args.TestMethodAttributes.FullName}");

            try
            {
                var lines = File.ReadAllLines(args.TestMethodAttributes.FullName);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l == String.Empty)
                    {
                        continue;
                    }

                    graph.RecordTestMethodAttribute(l);
                }
            }
            catch (Exception ex)
            {
                Error($"Unable to read test method attribute file {args.TestMethodAttributes.FullName}: {ex.Message}");
                return 1;
            }
        }
        else
        {
            Out("Using default test method attributes");

            graph.RecordTestMethodAttribute("Xunit.FactAttribute");
            graph.RecordTestMethodAttribute("Xunit.TheoryAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");
            graph.RecordTestMethodAttribute("MSTest.TestFramework.TestMethodAttribute");
        }

        // load the assemblies
        int successCount = 0;
        int errorCount = 0;
        int skipCount = 0;

        var buf = new List<FileInfo>();
        foreach (var file in args.AssemblyFolder!.GetFiles("*.dll", SearchOption.AllDirectories))
        {
            buf.Add(file);
        }

        foreach (var file in args.AssemblyFolder!.GetFiles("*.exe", SearchOption.AllDirectories))
        {
            buf.Add(file);
        }

        // make the order of input files deterministic
        buf.Sort((x, y) => string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase));

        var files = new Queue<FileInfo>(buf);

        var tasks = new HashSet<Task<LoadedAssembly>>(MaxConcurrentAssemblyLoads);
        var map = new Dictionary<Task<LoadedAssembly>, FileInfo>(MaxConcurrentAssemblyLoads);

        while (files.Count > 0)
        {
            while (tasks.Count < MaxConcurrentAssemblyLoads && files.Count > 0)
            {
                var file = files.Dequeue();
                var task = Task.Run(() =>
                {
                    Out($"Loading assembly {file.FullName}");
                    return new LoadedAssembly(file.FullName);
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

        Out("Analyzing...");
        graph.Done(x => Out($"  {x}"));

        if (!OutputDeadSymbols() ||
            !OutputAliveSymbols() ||
            !OutputAliveByTestSymbols() ||
            !OutputNeedlesslyPublicSymbols() ||
            !OutputUnreferencedAssemblies() ||
            !OutputUnanalyzedAssemblies() ||
            !OutputNeedlessInternalsVisibleTo() ||
            !OutputAssemblyLayerCake() ||
            !OutputDependencyDiagram() ||
            !OutputGraphDump())
        {
            return 1;
        }

        return 0;

        async Task CompleteTask(Task<LoadedAssembly> task)
        {
            var file = map[task];
            _ = map.Remove(task);

            try
            {
                using (var la = await task)
                {
                    graph.MergeAssembly(la);
                }

                successCount++;
            }
            catch (BadImageFormatException)
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

        bool OutputDeadSymbols()
        {
            if (args.DeadSymbols != null)
            {
                var path = Path.GetFullPath(args.DeadSymbols);
                try
                {
                    var report = graph.CollectDeadSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output report on dead symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on dead symbols to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputAliveSymbols()
        {
            if (args.AliveSymbols != null)
            {
                var path = Path.GetFullPath(args.AliveSymbols);
                try
                {
                    var report = graph.CollectAliveSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output report on alive symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on alive symbols to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputAliveByTestSymbols()
        {
            if (args.AliveByTestSymbols != null)
            {
                var path = Path.GetFullPath(args.AliveByTestSymbols);
                try
                {
                    var report = graph.CollectAliveByTestSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output report on symbols alive by test to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on symbols alive by test to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputNeedlesslyPublicSymbols()
        {
            if (args.NeedlesslyPublicSymbols != null)
            {
                var path = Path.GetFullPath(args.NeedlesslyPublicSymbols);
                try
                {
                    var report = graph.CollectNeedlesslyPublicSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output report on needlessly public symbols to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write report on needlessly public symbols to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputUnreferencedAssemblies()
        {
            if (args.UnreferencedAssemblies != null)
            {
                var path = Path.GetFullPath(args.UnreferencedAssemblies);
                try
                {
                    var report = graph.CollectUnreferencedAssemblies();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output unreferenced assemblies report to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output unreferenced assemblies report to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputUnanalyzedAssemblies()
        {
            if (args.UnanalyzedAssemblies != null)
            {
                var path = Path.GetFullPath(args.UnanalyzedAssemblies);
                try
                {
                    var report = graph.CollectUnanalyzedAssemblies().Order();
                    File.WriteAllLines(path, report);

                    Out($"Output analyzed assemblies report to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output unanalyzed assemblies report to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputNeedlessInternalsVisibleTo()
        {
            if (args.NeedlessInternalsVisibleTo != null)
            {
                var path = Path.GetFullPath(args.NeedlessInternalsVisibleTo);
                try
                {
                    var report = graph.CollectNeedlessInternalsVisibleTo();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"Output needless [InternalsVisibleTo] report to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output needless [InternalsVisibleTo] report to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputAssemblyLayerCake()
        {
            if (args.AssemblyLayerCake != null)
            {
                var path = Path.GetFullPath(args.AssemblyLayerCake);
                try
                {
                    var cake = graph.CreateAssemblyLayerCake();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, cake, _serializationOptions);
                    }

                    Out($"Output assembly layer cake to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output assembly layer cake to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputDependencyDiagram()
        {
            if (args.DependencyDiagram != null)
            {
                var path = Path.GetFullPath(args.DependencyDiagram);
                try
                {
                    var dd = graph.CreateDependencyDiagram();
                    File.WriteAllText(path, dd);
                    Out($"Output assembly dependency diagram to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output dependency diagram to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputGraphDump()
        {
            if (args.GraphDump != null)
            {
                var path = Path.GetFullPath(args.GraphDump);
                try
                {
                    using (var file = File.CreateText(path))
                    {
                        graph.Dump(file);
                    }

                    Out($"Output graph dump to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to write graph dump to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        void Out(string message)
        {
            if (args.Verbose)
            {
                if (args.DumpMemory)
                {
                    var proc = System.Diagnostics.Process.GetCurrentProcess();
                    var mem = proc.PrivateMemorySize64 / 1024 / 1024;
                    Console.WriteLine($"{message} ({mem}MB)");
                }
                else
                {
                    Console.WriteLine(message);
                }
            }
        }

        void Warn(string message) => Console.WriteLine("WARNING: " + message);
        void Error(string message) => Console.Error.WriteLine("ERROR: " + message);
    }
}
