using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Undertaker.Graph;

namespace Undertaker;

internal static class Program
{
    private const int MaxConcurrentAssemblyLoads = 32;
    private static readonly JsonSerializerOptions _serializationOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class UndertakerArgs(ParseResult parseResult)
    {
        public DirectoryInfo[]? AssemblyFolders { get; set; } = parseResult.GetValue<DirectoryInfo[]>("input folders");
        public FileInfo? RootAssemblies { get; set; } = parseResult.GetValue<FileInfo>("-ra");
        public FileInfo? ReflectionSymbols { get; set; } = parseResult.GetValue<FileInfo>("-rs");
        public FileInfo? TestMethodAttributes { get; set; } = parseResult.GetValue<FileInfo>("-tma");
        public FileInfo? ReflectionMarkerAttributes { get; set; } = parseResult.GetValue<FileInfo>("-rma");
        public DirectoryInfo? DeadSymbols { get; set; } = parseResult.GetValue<DirectoryInfo>("-ds");
        public DirectoryInfo? AliveSymbols { get; set; } = parseResult.GetValue<DirectoryInfo>("-as");
        public DirectoryInfo? AliveByTestSymbols { get; set; } = parseResult.GetValue<DirectoryInfo>("-abts");
        public DirectoryInfo? NeedlesslyPublicSymbols { get; set; } = parseResult.GetValue<DirectoryInfo>("-nps");
        public DirectoryInfo? NeedlessInternalsVisibleTo { get; set; } = parseResult.GetValue<DirectoryInfo>("-nivt");
        public FileInfo? UnreferencedAssemblies { get; set; } = parseResult.GetValue<FileInfo>("-ua");
        public FileInfo? AssemblyLayerCake { get; set; } = parseResult.GetValue<FileInfo>("-alc");
        public FileInfo? DependencyDiagram { get; set; } = parseResult.GetValue<FileInfo>("-dd");
        public FileInfo? UnanalyzedAssemblies { get; set; } = parseResult.GetValue<FileInfo>("-uaa");
        public FileInfo? DuplicateAssemblies { get; set; } = parseResult.GetValue<FileInfo>("-da");
        public DirectoryInfo? UnreferencedSymbols { get; set; } = parseResult.GetValue<DirectoryInfo>("-urs");
        public DirectoryInfo? GraphDumps { get; set; } = parseResult.GetValue<DirectoryInfo>("-gd");
        public bool ContinueOnLoadErrors { get; set; } = parseResult.GetValue<bool>("-cle");
        public bool Verbose { get; set; } = parseResult.GetValue<bool>("-v");
        public bool DumpMemory { get; set; }
        public bool CSV { get; set; } = parseResult.GetValue<bool>("-csv");
    }

    public static Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Helps with dead code detection over a large code base")
        {
            new Argument<DirectoryInfo[]>("input folders")
            {
                Description = "Paths to folders containing the assemblies to analyze.",
            }.AcceptExistingOnly(),

            new Option<FileInfo>("-ra", "--root-assemblies")
            {
                Description = "Path to a text file listing assemblies to be treated as roots, one assembly name per line (with or without a .dll extension)",
                HelpName = "text file",
            }.AcceptExistingOnly(),

            new Option<FileInfo>("-rs", "--reflection-symbols")
            {
                Description = "Path to a text file listing symbols accessed through reflection, with each line in the form of `assembly-name:fully-qualified-symbol-name`",
                HelpName = "text file",
            }.AcceptExistingOnly(),

            new Option<FileInfo>("-tma", "--test-method-attributes")
            {
                Description = "Path to a text file listing all the attributes that can mark a method as a test, one per line",
                HelpName = "text file",
            }.AcceptExistingOnly(),

            new Option<FileInfo>("-rma", "--reflection-marker-attributes")
            {
                Description = "Path to a text file listing all the attributes that can mark a method as being used from reflection, one per line",
                HelpName = "text file",
            }.AcceptExistingOnly(),

            new Option<DirectoryInfo>("-ds", "--dead-symbols")
            {
                Description = "Directory path where to emit the per-assembly reports on dead symbols",
                HelpName = "output folder",
            },

            new Option<DirectoryInfo>("-as", "--alive-symbols")
            {
                Description = "Directory path where to emit the per-assembly reports on alive symbols",
                HelpName = "output folder",
            },

            new Option<DirectoryInfo>("-abts", "--alive-by-test-symbols")
            {
                Description = "Directory path where to emit the per-assembly reports on symbols kept alive only by test methods",
                HelpName = "output folder",
            },

            new Option<DirectoryInfo>("-nps", "--needlessly-public-symbols")
            {
                Description = "Directory path where to emit the per-assembly reports on public symbols which could be made internal",
                HelpName = "output folder",
            },

            new Option<DirectoryInfo>("-nivt", "--needless-internals-visible-to")
            {
                Description = "Directory path where to emit the per-assembly reports on needless uses of [InternalsVisibleTo]",
                HelpName = "output folder",
            },

            new Option<FileInfo>("-ua", "--unreferenced-assemblies")
            {
                Description = "Path of the report to produce on completely unreferenced assemblies",
                HelpName = "output file",
            },

            new Option<FileInfo>("-uaa", "--unanalyzed-assemblies")
            {
                Description = "Path of the report to produce on assemblies which were referenced but not analyzed",
                HelpName = "output file",
            },

            new Option<FileInfo>("-da", "--duplicate-assemblies")
            {
                Description = "Path of the report to produce on assemblies which were found multiple times as input",
                HelpName = "output file",
            },

            new Option<DirectoryInfo>("-urs", "--unreferenced-symbols")
            {
                Description = "Directory path where to emit the per-assembly reports on completely unreferenced symbols",
                HelpName = "output folder",
            },

            new Option<FileInfo>("-alc", "--assembly-layer-cake")
            {
                Description = "Path of the assembly layer cake to produce",
                HelpName = "output file",
            },

            new Option<FileInfo>("-dd", "--dependency-diagram")
            {
                Description = "Path of the Mermaid-based assembly dependency diagram to produce",
                HelpName = "output file",
            },

            new Option<DirectoryInfo>("-gd", "--graph-dumps")
            {
                Description = "Directory path for the graph dump files to produce",
                HelpName = "output folder",
                Hidden = true,
            },

            new Option<bool>("-cle", "--continue-on-load-errors")
            {
                Description = "Proceed to the analysis and output phases even if some assemblies didn't load",
            },

            new Option<bool>("-v", "--verbose")
            {
                Description = "Output progress reports",
            },

            new Option<bool>("-csv")
            {
                Description = "Switch some output files from JSON to CSV format",
            },
        };

        rootCommand.SetAction(ExecuteAsync);

        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count == 0)
        {
            return parseResult.InvokeAsync();
        }

        foreach (ParseError parseError in parseResult.Errors)
        {
            Console.Error.WriteLine(parseError.Message);
        }

        return Task.FromResult(1);
    }

    private static async Task<int> ExecuteAsync(ParseResult parseResult)
    {
        var args = new UndertakerArgs(parseResult);
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

            args.DeadSymbols = new("./dead-symbols");
            args.NeedlessInternalsVisibleTo = new("./needless-internals-visible-to");
            args.DuplicateAssemblies = new("./duplicate-assemblies");
            args.NeedlesslyPublicSymbols = new("./needlessly-public-symbols");
            args.AliveSymbols = new("./alive-symbols");
            args.AliveByTestSymbols = new("./alive-by-test-symbols");
            args.UnreferencedSymbols = new("./unreferenced-symbols");

            args.UnreferencedAssemblies = new("./unreferenced-assemblies.txt");
            args.UnanalyzedAssemblies = new("./unanalyzed-assemblies.txt");
            args.AssemblyLayerCake = new("./assembly-layer-cake.json");
            args.DependencyDiagram = new("./dependency-diagram.mmd");
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

            graph.RecordTestMethodAttribute("NUnit.Framework.OneTimeSetUpAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.OneTimeTearDownAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.SetUpAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.SetUpFixtureAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TearDownAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestCaseAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestCaseSourceAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestFixtureSetUpAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TestFixtureTearDownAttribute");
            graph.RecordTestMethodAttribute("NUnit.Framework.TheoryAttribute");

            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestCleanupAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestInitializeAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.STATestMethod");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute");
            graph.RecordTestMethodAttribute("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");
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
            graph.RecordReflectionMarkerAttribute("System.Runtime.Serialization.DataContractAttribute");
            graph.RecordReflectionMarkerAttribute("System.Runtime.Serialization.DataMemberAttribute");
            graph.RecordReflectionMarkerAttribute("System.SerializableAttribute");
            graph.RecordReflectionMarkerAttribute("ProtoBuf.ProtoContractAttribute");

            graph.RecordReflectionMarkerAttribute("Newtonsoft.Json.JsonObjectAttribute");
            graph.RecordReflectionMarkerAttribute("Newtonsoft.Json.JsonPropertyAttribute");
        }

        // load the assemblies
        int successCount = 0;
        int errorCount = 0;
        int badFmtCount = 0;
        int duplicateCount = 0;

        var tasks = new HashSet<Task<LoadedAssembly>>(MaxConcurrentAssemblyLoads);
        var map = new Dictionary<Task<LoadedAssembly>, FileInfo>(MaxConcurrentAssemblyLoads);

        Out("Loading assemblies...");

        foreach (var folder in args.AssemblyFolders!)
        {
            foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (!(file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (graph.AssemblyLoaded(file.FullName))
                {
                    OutLoadStatus(ConsoleColor.Yellow, "Duplicate", $"{file.FullName}");
                    duplicateCount++;
                    continue;
                }

                if (tasks.Count >= MaxConcurrentAssemblyLoads)
                {
                    var t = await Task.WhenAny(tasks);
                    await CompleteTask(t);
                    _ = tasks.Remove(t);
                }

                var task = Task.Run(() => new LoadedAssembly(file.FullName));

                _ = tasks.Add(task);
                map.Add(task, file);
            }
        }

        await foreach (var task in Task.WhenEach(tasks))
        {
            await CompleteTask(task);
        }

        Out($"Done loading assemblies: loaded {successCount}, duplicate(s) {duplicateCount}, not .NET {badFmtCount}, error(s) {errorCount}");

        if (errorCount > 0)
        {
            if (args.ContinueOnLoadErrors)
            {
                Warn($"Unable to load {errorCount} assemblies, ignoring due to -cle option");
            }
            else
            {
                Error($"Unable to load {errorCount} assemblies, exiting");
                return 1;
            }
        }

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
            !OutputGraphDumps()
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
                    if (!graph.MergeAssembly(la))
                    {
                        OutLoadStatus(ConsoleColor.Yellow, "Duplicate", $"{file.FullName}");
                        duplicateCount++;
                    }
                }

                successCount++;
                OutLoadStatus(ConsoleColor.White, "Loaded", $"{file.FullName}");
            }
            catch (BadImageFormatException)
            {
                OutLoadStatus(ConsoleColor.Yellow, "Not .NET", $"{file.FullName}");
                badFmtCount++;
            }
            catch (Exception ex)
            {
                OutLoadStatus(ConsoleColor.Red, "Error", $"{file.FullName}: {ex.Message}");
                errorCount++;
            }
        }

        bool OutputDeadSymbols()
        {
            if (args.DeadSymbols != null)
            {
                var path = args.DeadSymbols.FullName;
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
                var path = args.UnreferencedSymbols.FullName;
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
                var path = args.AliveSymbols.FullName;
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
                var path = args.AliveByTestSymbols.FullName;
                Out($"  Writing reports on symbols alive only by test to {path}");

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
                var path = args.NeedlesslyPublicSymbols.FullName;
                Out($"  Writing reports on needlessly public symbols to {path}");

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
                var path = args.UnreferencedAssemblies.FullName;
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
                var path = args.UnanalyzedAssemblies.FullName;
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
                var path = args.DuplicateAssemblies.FullName;
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
                var path = args.NeedlessInternalsVisibleTo.FullName;
                Out($"  Writing reports on needless [InternalsVisibleTo] to {path}");

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
                var path = args.AssemblyLayerCake.FullName;
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
                var path = args.DependencyDiagram.FullName;
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

        bool OutputGraphDumps()
        {
            if (args.GraphDumps != null)
            {
                var path = args.GraphDumps.FullName;
                Out($"  Writing graph dumps to {path}");

                try
                {
                    var di = Directory.CreateDirectory(path);

                    try
                    {
                        reporter.Dump(path);
                    }
                    catch (Exception ex)
                    {
                        Error($"Unable to write graph dumps to {path}: {ex.Message}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Error($"Unable to create output directory for graph dumps at {path}: {ex.Message}");
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

        void OutLoadStatus(ConsoleColor? color, string status, string message)
        {
            if (args.Verbose)
            {
                Console.Write("  [");
                if (color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                }

                Console.Write($"{status,-10}");
                Console.ResetColor();

                if (args.DumpMemory)
                {
                    var proc = System.Diagnostics.Process.GetCurrentProcess();
                    var mem = proc.PrivateMemorySize64 / 1024 / 1024;

                    Console.WriteLine($"] {message} ({mem}MB)");
                }
                else
                {
                    Console.WriteLine($"] {message}");
                }
            }
        }

        void Warn(string message) => Console.WriteLine("WARNING: " + message);
        void Error(string message) => Console.Error.WriteLine("ERROR: " + message);
    }
}
