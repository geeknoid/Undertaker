using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using System.Text.Json.Serialization;
using Undertaker.Graph;
using Undertaker.Graph.Reporting;

namespace Undertaker;

internal static class Program
{
    private const int MaxConcurrentAssemblyLoads = 32;
    private static readonly JsonSerializerOptions _serializationOptions = new() 
    { 
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class UndertakerArgs
    {
        public DirectoryInfo? AssemblyFolder { get; set; }
        public FileInfo? RootAssemblies { get; set; }
        public FileInfo? ReflectionSymbols { get; set; }
        public FileInfo? TestMethodAttributes { get; set; }
        public FileInfo? ReflectionMarkerAttributes { get; set; }
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
        public string? UnreferencedSymbols { get; set; }
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
                ["-rs", "--reflection-symbols"],
                "Path to a text file listing symbols accessed through reflection, with each line in the form of `assembly-name:fully-qualified-symbol-name`"),

            new Option<FileInfo>(
                ["-tma", "--test-method-attributes"],
                "Path to a text file listing all the attributes that can mark a method as a test, one per line"),

            new Option<FileInfo>(
                ["-rma", "--reflection-marker-attributes"],
                "Path to a text file listing all the attributes that can mark a method as being used from reflection, one per line"),

            new Option<string>(
                ["-ds", "--dead-symbols"],
                "Directory path where to emit the per-assembly reports on dead symbols"),

            new Option<string>(
                ["-as", "--alive-symbols"],
                "Directory path where to emit the per-assembly reports on alive symbols"),

            new Option<string>(
                ["-abts", "--alive-by-test-symbols"],
                "Directory path where to emit the per-assembly reports on symbols kept alive only by test methods"),

            new Option<string>(
                ["-nps", "--needlessly-public-symbols"],
                "Directory path where to emit the per-assembly reports on public symbols which could be made internal"),

            new Option<string>(
                ["-nivt", "--needless-internals-visible-to"],
                "Directory path where to emit the per-assembly reports on needless uses of [InternalsVisibleTo]"),

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
                ["-urs", "--unreferenced-symbols"],
                "Directory path where to emit the per-assembly reports on completely unreferenced symbols"),


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
            && args.UnreferencedSymbols == null
            && args.NeedlessInternalsVisibleTo == null
            && args.AssemblyLayerCake == null
            && args.DependencyDiagram == null)
        {
            Out("No explicit output requested, generating default outputs");

            args.DeadSymbols = "./dead-symbols";
            args.NeedlessInternalsVisibleTo = "./needless-internals-visible-to";
            args.DuplicateAssemblies = "./duplicate-assemblies";
            args.NeedlesslyPublicSymbols = "./needlessly-public-symbols";
            args.AliveSymbols = "./alive-symbols";
            args.AliveByTestSymbols = "./alive-by-test-symbols";
            args.UnreferencedSymbols = "./unreferenced-symbols";

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

        if (args.ReflectionSymbols != null)
        {
            Out($"Loading reflection symbol file {args.ReflectionSymbols.FullName}");

            try
            {
                var lineNumber = 0;
                var lines = File.ReadAllLines(args.ReflectionSymbols.FullName);
                foreach (var line in lines)
                {
                    lineNumber++;

                    var l = line.Trim();
                    if (l == String.Empty)
                    {
                        continue;
                    }

                    var s = l.Split(':', 2);
                    if (s.Length != 2)
                    {
                        Error($"Line {lineNumber} in reflection symbol file {args.ReflectionSymbols.FullName} is not formatted correctly");
                        continue;
                    }

                    var assemblyName = s[0].Trim();
                    var symbolName = s[1].Trim();

                    if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
                    }
                    else if (assemblyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
                    }

                    graph.RecordReflectionSymbol(assemblyName, symbolName);
                }
            }
            catch (Exception ex)
            {
                Error($"Unable to read reflection symbol file {args.ReflectionSymbols.FullName}: {ex.Message}");
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

        if (args.ReflectionMarkerAttributes != null)
        {
            Out($"Loading reflection marker attribute file {args.ReflectionMarkerAttributes.FullName}");

            try
            {
                var lines = File.ReadAllLines(args.ReflectionMarkerAttributes.FullName);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l == String.Empty)
                    {
                        continue;
                    }

                    graph.RecordReflectionMarkerAttribute(l);
                }
            }
            catch (Exception ex)
            {
                Error($"Unable to read reflection marker attribute file {args.ReflectionMarkerAttributes.FullName}: {ex.Message}");
                return 1;
            }
        }
        else
        {
            Out("Using default reflection marker attributes");

            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.AcceptsVerbsAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpDeleteAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpGetAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpHeadAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpOptionsAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpPatchAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpPostAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.HttpPutAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNetCore.Mvc.RouteAttribute");

            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.AcceptVerbsAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.HttpDeleteAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.HttpGetAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.HttpPatchAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.HttpPostAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.HttpPutAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.RouteAttribute");
            graph.RecordReflectionMarkerAttribute("System.Web.Mvc.RoutePrefixAttribute");

            graph.RecordReflectionMarkerAttribute("Microsoft.AspNet.OData.Routing.ODataRouteAttribute");
            graph.RecordReflectionMarkerAttribute("Microsoft.AspNet.OData.Routing.ODataRoutePrefixAttribute");

            graph.RecordReflectionMarkerAttribute("System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute");
        }

        // load the assemblies
        int successCount = 0;
        int errorCount = 0;
        int skipCount = 0;

        var tasks = new HashSet<Task<LoadedAssembly>>(MaxConcurrentAssemblyLoads);
        var map = new Dictionary<Task<LoadedAssembly>, FileInfo>(MaxConcurrentAssemblyLoads);

        Out("Loading assemblies...");

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
                Out($"  Loading assembly {file.FullName}");
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
            !OutputUnreferencedSymbols() ||
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
                Out($"  Writing reports on dead symbols to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectDeadSymbols();
                    foreach (var asm in report.Assemblies)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly);
                        filePath = args.CSV ? filePath + ".csv" : filePath + ".json";

                        try
                        {
                            if (args.CSV)
                            {
                                using var writer = new StreamWriter(filePath);
                                foreach (var sym in asm.DeadMembers)
                                {
                                    writer.WriteLine($"\"{sym.Name}\",{sym.Kind}");
                                }

                                foreach (var sym in asm.DeadTypes)
                                {
                                    writer.WriteLine($"\"{sym.Name}\",{sym.Kind}");
                                }
                            }
                            else
                            {
                                using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                JsonSerializer.Serialize(file, asm, _serializationOptions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to write report on dead symbols to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for dead symbols report at {path}: {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        bool OutputUnreferencedSymbols()
        {
            if (args.UnreferencedSymbols != null)
            {
                var path = Path.GetFullPath(args.UnreferencedSymbols);
                Out($"  Writing reports on unreferenced symbols to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectUnreferencedSymbols();
                    foreach (var asm in report.Assemblies)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly);
                        filePath = args.CSV ? filePath + ".csv" : filePath + ".json";

                        try
                        {
                            if (args.CSV)
                            {
                                using var writer = new StreamWriter(filePath);
                                foreach (var sym in asm.DeadMembers)
                                {
                                    writer.WriteLine($"\"{sym.Name}\",{sym.Kind}");
                                }

                                foreach (var sym in asm.DeadTypes)
                                {
                                    writer.WriteLine($"\"{sym.Name}\",{sym.Kind}");
                                }
                            }
                            else
                            {
                                using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                JsonSerializer.Serialize(file, asm, _serializationOptions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to write report on unreferenced symbols to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for unreferenced symbols report at {path}: {ex.Message}");
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
                Out($"  Writing reports on alive symbols to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectAliveSymbols();
                    foreach (var asm in report.Assemblies)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly) + ".json";
                        try
                        {
                            using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            JsonSerializer.Serialize(file, asm, _serializationOptions);
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to write report on alive symbols to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for alive symbols report at {path}: {ex.Message}");
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
                Out($"  Writing report on symbols alive only by test to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectAliveByTestSymbols();
                    foreach (var asm in report.Assemblies)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly) + ".json";
                        try
                        {
                            using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            JsonSerializer.Serialize(file, asm, _serializationOptions);
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to write report on symbols alive by test to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for reports on symbols alive by tests at {path}: {ex.Message}");
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
                Out($"  Writing report on needlessly public symbols to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectNeedlesslyPublicSymbols();
                    foreach (var asm in report.Assemblies)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly);
                        filePath = args.CSV ? filePath + ".csv" : filePath + ".json";

                        try
                        {
                            if (args.CSV)
                            {
                                using var writer = new StreamWriter(filePath);
                                foreach (var sym in asm.NeedlesslyPublicMembers)
                                {
                                    writer.WriteLine($"\"{sym}\"");
                                }

                                foreach (var sym in asm.NeedlesslyPublicTypes)
                                {
                                    writer.WriteLine($"\"{sym}\"");
                                }
                            }
                            else
                            {
                                using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                JsonSerializer.Serialize(file, asm, _serializationOptions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to write report on needlessly public symbols to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for reports on needlessly public symbols at {path}: {ex.Message}");
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
                Out($"  Writing report on unanalyzed assemblies to {path}");

                try
                {
                    var report = reporter.CollectUnreferencedAssemblies();
                    File.WriteAllLines(path, report);
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
                Out($"  Writing report on unanalyzed assemblies to {path}");

                try
                {
                    var report = reporter.CollectUnanalyzedAssemblies().Order();
                    File.WriteAllLines(path, report);
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
                Out($"  Writing report on duplicate assemblies to {path}");

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
                Out($"  Writing report on needless [InternalsVisibleTo] to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    var report = reporter.CollectNeedlessInternalsVisibleTo();
                    foreach (var asm in report)
                    {
                        var filePath = Path.Combine(di.FullName, asm.Assembly);
                        filePath = args.CSV ? filePath + ".csv" : filePath + ".json";

                        try
                        {
                            if (args.CSV)
                            {
                                using var writer = new StreamWriter(filePath);
                                foreach (var other in asm.OtherAssemblies)
                                {
                                    writer.WriteLine($"{other}");
                                }
                            }
                            else
                            {
                                using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                JsonSerializer.Serialize(file, asm, _serializationOptions);
                            }
                        }
                        catch (Exception ex)
                        {
                            Error($"Unable to output needless [InternalsVisibleTo] report to {filePath}: {ex.Message}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for reports on needless [InternalsVisibleTo] at {path}: {ex.Message}");
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
                Out($"  Writing assembly layer cake to {path}");

                try
                {
                    var cake = reporter.CreateAssemblyLayerCake();
                    using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(file, cake, _serializationOptions);
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
                Out($"  Writing assembly dependency diagram to {path}");

                try
                {
                    var dd = reporter.CreateDependencyDiagram();
                    File.WriteAllText(path, dd);
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
                Out($"  Writing graph dump to {path}");

                try
                {
                    using var file = File.CreateText(path);
                    reporter.Dump(file);
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
