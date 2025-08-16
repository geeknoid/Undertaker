using System.CommandLine;
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
        public string? DuplicateAssemblies { get; set; }
        public string? GraphDump { get; set; }
        public bool ContinueOnLoadErrors { get; set; }
        public bool Verbose { get; set; }
        public bool DumpMemory { get; set; }
        public bool CSV { get; set; }
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
                ["-da", "--duplicate-assemblies"],
                "Path of the report to produce on assemblies which were found multiple times as input"),

            new Option<string>(
                ["-nivt", "--needless-internals-visible-to"],
                "Path of the JSON report to produce on needless uses of [InternalsVisibleTo]"),

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

            new Option<bool>(
                ["-csv"],
                "Switch some output files from JSON to CSV format"),
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
            && args.DuplicateAssemblies == null
            && args.NeedlessInternalsVisibleTo == null
            && args.AssemblyLayerCake == null
            && args.DependencyDiagram == null)
        {
            Out("No explicit output requested, generating default outputs");

            if (args.CSV)
            {
                args.DeadSymbols = "./dead-symbols.csv";
                args.NeedlessInternalsVisibleTo = "./needless-internals-visible-to.csv";
                args.DuplicateAssemblies = "./duplicate-assemblies.csv";
                args.NeedlesslyPublicSymbols = "./needlessly-public-symbols.csv";
            }
            else
            {
                args.DeadSymbols = "./dead-symbols.json";
                args.NeedlessInternalsVisibleTo = "./needless-internals-visible-to.json";
                args.DuplicateAssemblies = "./duplicate-assemblies.json";
                args.NeedlesslyPublicSymbols = "./needlessly-public-symbols.json";
            }

            args.AliveSymbols = "./alive-symbols.json";
            args.AliveByTestSymbols = "./alive-by-test-symbols.json";
            args.UnreferencedAssemblies = "./unreferenced-assemblies.txt";
            args.UnanalyzedAssemblies= "./unanalyzed-assemblies.txt";
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

        var tasks = new HashSet<Task<LoadedAssembly>>(MaxConcurrentAssemblyLoads);
        var map = new Dictionary<Task<LoadedAssembly>, FileInfo>(MaxConcurrentAssemblyLoads);

        foreach (var file in args.AssemblyFolder!.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (!(file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (tasks.Count >= MaxConcurrentAssemblyLoads)
            {
                var t = await Task.WhenAny(tasks);
                await CompleteTask(t);
                _ = tasks.Remove(t);
            }

            var task = Task.Run(() =>
            {
                Out($"Loading assembly {file.FullName}");
                return new LoadedAssembly(file.FullName);
            });

            _ = tasks.Add(task);
            map.Add(task, file);
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
        var reporter = graph.Done(x => Out($"  {x}"));

        if (args.Verbose)
        {
            System.GC.Collect(2, GCCollectionMode.Aggressive);
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var mem = proc.PrivateMemorySize64 / 1024 / 1024;
            Out($"Total memory used: {mem}MB");
        }

        Out("Generating reports...");
        return !OutputDeadSymbols() ||
            !OutputAliveSymbols() ||
            !OutputAliveByTestSymbols() ||
            !OutputNeedlesslyPublicSymbols() ||
            !OutputUnreferencedAssemblies() ||
            !OutputUnanalyzedAssemblies() ||
            !OutputDuplicateAssemblies() ||
            !OutputNeedlessInternalsVisibleTo() ||
            !OutputAssemblyLayerCake() ||
            !OutputDependencyDiagram() ||
            !OutputGraphDump()
            ? 1
            : 0;

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
                Warn($"{file.FullName} is not a .NET assembly, skipping");
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
                    var report = reporter.CollectDeadSymbols();

                    if (args.CSV)
                    {
                        using var writer = new StreamWriter(path);
                        foreach (var asm in report.Assemblies)
                        {
                            foreach (var sym in asm.DeadMembers)
                            {
                                writer.WriteLine($"{asm.Assembly},\"{sym.Name}\",{sym.Kind}");
                            }

                            foreach (var sym in asm.DeadTypes)
                            {
                                writer.WriteLine($"{asm.Assembly},\"{sym.Name}\",{sym.Kind}");
                            }
                        }
                    }
                    else
                    {
                        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on dead symbols to {path}");
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
                    var report = reporter.CollectAliveSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on alive symbols to {path}");
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
                    var report = reporter.CollectAliveByTestSymbols();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on symbols alive only by test to {path}");
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
                    var report = reporter.CollectNeedlesslyPublicSymbols();

                    if (args.CSV)
                    {
                        using var writer = new StreamWriter(path);
                        foreach (var asm in report.Assemblies)
                        {
                            foreach (var sym in asm.NeedlesslyPublicMembers)
                            {
                                writer.WriteLine($"{asm},\"{sym}\"");
                            }

                            foreach (var sym in asm.NeedlesslyPublicTypes)
                            {
                                writer.WriteLine($"{asm},\"{sym}\"");
                            }
                        }
                    }
                    else
                    {
                        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on needlessly public symbols to {path}");
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
                    var report = reporter.CollectUnreferencedAssemblies();
                    File.WriteAllLines(path, report);

                    Out($"  Writing report on unreferenced assemblies to {path}");
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
                    var report = reporter.CollectUnanalyzedAssemblies().Order();
                    File.WriteAllLines(path, report);

                    Out($"  Writing report on unanalyzed assemblies to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output unanalyzed assemblies report to {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputDuplicateAssemblies()
        {
            if (args.DuplicateAssemblies != null)
            {
                var path = Path.GetFullPath(args.DuplicateAssemblies);
                try
                {
                    var report = reporter.CollectDuplicateAssemblies().Order();

                    if (args.CSV)
                    {
                        using var writer = new StreamWriter(path);
                        foreach (var asm in report)
                        {
                            writer.WriteLine($"{asm.Assembly},{asm.Version},{asm.Path}");
                            foreach (var other in asm.Duplicates)
                            {
                                writer.WriteLine($"{asm.Assembly},{other.Version},{other.Path}");
                            }
                        }
                    }
                    else
                    {
                        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on duplicate assemblies to {path}");
                }
                catch (Exception ex)
                {
                    Error($"Unable to output duplicate assemblies report to {path}: {ex.Message}");
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
                    var report = reporter.CollectNeedlessInternalsVisibleTo();

                    if (args.CSV)
                    {
                        using var writer = new StreamWriter(path);
                        foreach (var asm in report)
                        {
                            foreach (var other in asm.OtherAssemblies)
                            {
                                writer.WriteLine($"{asm.Assembly},{other}");
                            }
                        }
                    }
                    else
                    {
                        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                        JsonSerializer.Serialize(file, report, _serializationOptions);
                    }

                    Out($"  Writing report on needless [InternalsVisibleTo] to {path}");
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
                    var cake = reporter.CreateAssemblyLayerCake();
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(file, cake, _serializationOptions);
                    }

                    Out($"  Writing assembly layer cake to {path}");
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
                    var dd = reporter.CreateDependencyDiagram();
                    File.WriteAllText(path, dd);

                    Out($"  Writing assembly dependency diagram to {path}");
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
                        reporter.Dump(file);
                    }

                    Out($"  Writing graph dump to {path}");
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
