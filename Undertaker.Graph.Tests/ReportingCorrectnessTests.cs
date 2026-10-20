using System.Reflection;
using System.Runtime.CompilerServices;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph.Tests;

public class ReportingCorrectnessTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string FixturePrefix = "Undertaker.Graph.Tests.ReportingFixture";

    [Fact]
    public void NeedlesslyPublicIncludesAssembliesWithOnlyAffectedMembers()
    {
        var graph = new AssemblyGraph();
        var library = AddAssembly(graph, "Library");
        var consumer = AddAssembly(graph, "Consumer");
        var type = AddSymbol(graph, library, "Library.Api", "Type");
        Set(type, "IsPublic", true);
        var member = AddSymbol(graph, library, "Library.Api.Unused()", "Method");
        Set(member, "IsPublic", true);
        Call(type, "AddMember", member);
        var caller = AddSymbol(graph, consumer, "Consumer.Use()", "Method");
        Call(caller, "RecordReferencedSymbol", type);

        var report = Assert.Single(graph.Done(_ => { }).CollectNeedlesslyPublicSymbols().Assemblies);

        Assert.Equal("Library", report.Assembly);
        Assert.Empty(report.NeedlesslyPublicTypes);
        Assert.Equal(["Library.Api.Unused()"], report.NeedlesslyPublicMembers);
    }

    [Fact]
    public void UnreferencedAssembliesIncludeEmptyAndRootlessButNotHiddenLiveSystemOrUnloaded()
    {
        var graph = new AssemblyGraph();
        _ = AddAssembly(graph, "Empty");
        var rootless = AddAssembly(graph, "Rootless");
        _ = AddSymbol(graph, rootless, "Rootless.Unused", "Type");
        var live = AddAssembly(graph, "HiddenLive");
        var hidden = AddSymbol(graph, live, "HiddenLive.Entry()", "Method");
        Set(hidden, "Root", true);
        Set(hidden, "Hide", true);
        _ = AddAssembly(graph, "System.Rootless");
        var unloaded = AddAssembly(graph, "Unloaded", loaded: false);
        _ = AddSymbol(graph, unloaded, "Unloaded.Missing", "Type");

        var reporter = graph.Done(_ => { });

        Assert.Equal(["Empty", "Rootless"], reporter.CollectUnreferencedAssemblies());
        Assert.Empty(reporter.CollectAliveSymbols().Assemblies);
    }

    [Fact]
    public void TestOnlyLivenessIsExclusiveTransitiveAndCycleSafe()
    {
        var reporter = ScanFixtures();
        var report = Assert.Single(reporter.CollectAliveByTestSymbols().Assemblies);
        var members = report.AliveMembers
            .Where(symbol => symbol.Name.ToString().StartsWith(FixturePrefix, StringComparison.Ordinal))
            .Select(symbol => symbol.Name.ToString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                $"{FixturePrefix}.ApiDependency()",
                $"{FixturePrefix}.CycleA()",
                $"{FixturePrefix}.CycleB()",
                $"{FixturePrefix}.Helper()",
                $"{FixturePrefix}.HiddenLeaf()",
                $"{FixturePrefix}.Leaf()",
                $"{FixturePrefix}.TestEntry()",
                $"{FixturePrefix}ExclusiveContainer.Run()",
            },
            members.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(report.AliveTypes, symbol => symbol.Name.ToString() == FixturePrefix);
        Assert.Contains(report.AliveTypes, symbol => symbol.Name.ToString() == $"{FixturePrefix}ExclusiveContainer");
        Assert.DoesNotContain(report.AliveTypes, symbol => symbol.Name.ToString() == $"{FixturePrefix}HiddenContainer");

        var leaf = Assert.Single(report.AliveMembers, symbol => symbol.Name.ToString() == $"{FixturePrefix}.Leaf()");
        Assert.NotNull(leaf.ReferencedBy);
        Assert.Equal([$"{FixturePrefix}.Helper()"], leaf.ReferencedBy.Select(name => name.ToString()));
        Assert.Equal(
            report.AliveMembers.Select(symbol => symbol.Name.ToString()),
            reporter.CollectAliveByTestSymbols().Assemblies.Single().AliveMembers.Select(symbol => symbol.Name.ToString()));
    }

    [Fact]
    public void PublicApiTestMethodsRetainTheirNonTestRoot()
    {
        var reporter = ScanFixtures(rootAssembly: true);
        var report = reporter.CollectAliveByTestSymbols();
        var names = report.Assemblies.SelectMany(assembly => assembly.AliveMembers).Select(symbol => symbol.Name.ToString()).ToList();
        var alive = reporter.CollectAliveSymbols().Assemblies.SelectMany(assembly => assembly.AliveMembers).Select(symbol => symbol.Name.ToString()).ToList();

        Assert.Contains("Undertaker.Graph.Tests.ReportingApiFixture.TestEntry()", alive);
        Assert.Contains($"{FixturePrefix}.ApiDependency()", alive);
        Assert.DoesNotContain("Undertaker.Graph.Tests.ReportingApiFixture.TestEntry()", names);
        Assert.DoesNotContain($"{FixturePrefix}.ApiDependency()", names);
        Assert.Contains($"{FixturePrefix}.Leaf()", names);
    }

    [Fact]
    public void ConstantRetainedTypesAndTheirDependenciesAreNotTestOnly()
    {
        var graph = new AssemblyGraph();
        var assembly = graph.GetAssembly("Constants");
        assembly.Loaded = true;
        var constants = (TypeSymbol)assembly.GetSymbol(graph, "Constants.Values", SymbolKind.Type);
        constants.SetDeclaresConstants();
        var initializer = assembly.GetSymbol(graph, "Constants.Values..cctor()", SymbolKind.Method);
        var dependency = assembly.GetSymbol(graph, "Constants.Dependency()", SymbolKind.Method);
        var dependencyType = (TypeSymbol)assembly.GetSymbol(graph, "Constants.Dependency", SymbolKind.Type);
        dependencyType.AddMember(dependency);
        var tests = (TypeSymbol)assembly.GetSymbol(graph, "Constants.Tests", SymbolKind.Type);
        var test = (MethodSymbol)assembly.GetSymbol(graph, "Constants.Tests.Run()", SymbolKind.Method);
        test.MarkAsTestMethod();
        tests.AddMember(test);
        constants.AddMember(initializer);
        constants.RecordReferencedSymbol(initializer);
        initializer.RecordReferencedSymbol(dependency);
        test.RecordReferencedSymbol(constants);
        test.RecordReferencedSymbol(tests);
        var reporter = graph.Done(_ => { });

        Assert.True(constants.Marked);
        Assert.True(initializer.Marked);
        Assert.True(dependency.Marked);
        var report = Assert.Single(reporter.CollectAliveByTestSymbols().Assemblies);
        Assert.DoesNotContain(report.AliveTypes, symbol => symbol.Name == constants.Name);
        Assert.DoesNotContain(report.AliveMembers, symbol => symbol.Name == initializer.Name || symbol.Name == dependency.Name);
        Assert.Contains(report.AliveMembers, symbol => symbol.Name == test.Name);
    }

    [Fact]
    public void DependencyDiagramUsesSnapshotBeforeSyntheticDispatchEdges()
    {
        var graph = new AssemblyGraph();
        var contracts = AddAssembly(graph, "Contracts");
        var implementation = AddAssembly(graph, "Implementation");
        var contractType = AddSymbol(graph, contracts, "Contracts.IService", "Type");
        Set(contractType, "TypeKind", TypeKind.Interface);
        var contractMethod = AddSymbol(graph, contracts, "Contracts.IService.Run()", "Method");
        Set(contractMethod, "Root", true);
        Call(contractType, "AddMember", contractMethod);
        Call(contractMethod, "RecordReferencedSymbol", contractType);
        var implementationType = AddSymbol(graph, implementation, "Implementation.Service", "Type");
        var implementationMethod = AddSymbol(graph, implementation, "Implementation.Service.Run()", "Method");
        Call(implementationType, "AddMember", implementationMethod);
        Call(implementationMethod, "RecordReferencedSymbol", implementationType);
        Call(implementationType, "AddInterfaceImplemented", contractType);
        Call(implementationType, "RecordReferencedSymbol", contractType);
        Call(graph, "RecordDispatchLink", contractMethod, implementationMethod);

        var reporter = graph.Done(_ => { });

        Assert.Equal(
            $"stateDiagram-v2{Environment.NewLine}    Implementation --> Contracts{Environment.NewLine}",
            reporter.CreateDependencyDiagram());
        Assert.Contains(
            reporter.CollectAliveSymbols().Assemblies.SelectMany(assembly => assembly.AliveMembers),
            symbol => symbol.Name.ToString() == "Implementation.Service.Run()");
        Assert.Equal(reporter.CreateDependencyDiagram(), graph.Done(_ => { }).CreateDependencyDiagram());
    }

    [Fact]
    public void DumpsCreateDirectoriesAndRemoveOnlyOwnedObsoleteFiles()
    {
        WithOutputDirectory(directory =>
        {
            var graph = new AssemblyGraph();
            _ = AddAssembly(graph, "First");
            _ = AddAssembly(graph, "../Unloaded", loaded: false);
            graph.Done(_ => { }).Dump(directory);
            Assert.Equal(
                $"ASSEMBLY First.dll [LOADED, !ROOT, !SYSTEM]{Environment.NewLine}",
                File.ReadAllText(Path.Combine(directory, "First.txt")));
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "unrelated");

            var replacement = new AssemblyGraph();
            _ = AddAssembly(replacement, "Second");
            replacement.Done(_ => { }).Dump(directory);

            Assert.False(File.Exists(Path.Combine(directory, "First.txt")));
            Assert.True(File.Exists(Path.Combine(directory, "Second.txt")));
            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(directory, "notes.txt")));
            Assert.Equal(3, Directory.GetFiles(directory).Length);
        });
    }

    [Fact]
    public void DumpsRejectLoadedAssemblyPathsBeforeWritingOutsideTheDirectory()
    {
        WithOutputDirectory(directory =>
        {
            var graph = new AssemblyGraph();
            _ = AddAssembly(graph, "../escape");

            Assert.Throws<InvalidDataException>(() => graph.Done(_ => { }).Dump(directory));
            Assert.Empty(Directory.GetFiles(directory));
            Assert.False(File.Exists(Path.Combine(directory, "../escape.txt")));
        });
    }

    private static void WithOutputDirectory(Action<string> test)
    {
        var root = Path.Combine(Environment.CurrentDirectory, $"reporting-tests-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "output");
        try
        {
            test(directory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Reporter ScanFixtures(bool rootAssembly = false)
    {
        var graph = new AssemblyGraph();
        var assembly = typeof(ReportingCorrectnessTests).Assembly;
        if (rootAssembly)
        {
            graph.RecordRootAssembly(assembly.GetName().Name!);
        }

        graph.RecordTestMethodAttribute(typeof(ReportingTestAttribute).FullName!);
        graph.RecordReflectionMarkerAttribute(typeof(ReportingReflectionAttribute).FullName!);
        using var loaded = new LoadedAssembly(assembly.Location);
        Assert.True(graph.MergeAssembly(loaded));
        return graph.Done(_ => { });
    }

    private static object AddAssembly(AssemblyGraph graph, string name, bool loaded = true)
    {
        var assembly = Call(graph, "GetAssembly", name)!;
        Set(assembly, "Loaded", loaded);
        return assembly;
    }

    private static object AddSymbol(AssemblyGraph graph, object assembly, string name, string kind)
    {
        var method = assembly.GetType().GetMethods(InstanceMembers)
            .Single(candidate => candidate.Name == "GetSymbol" && candidate.GetParameters().Length == 3);
        var symbolKind = Enum.Parse(method.GetParameters()[2].ParameterType, kind);
        return method.Invoke(assembly, [graph, name, symbolKind])!;
    }

    private static object? Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, InstanceMembers)!.Invoke(target, arguments);

    private static void Set(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, InstanceMembers)!;
        property.DeclaringType!.GetProperty(name, InstanceMembers)!.SetValue(target, value);
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ReportingTestAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ReportingReflectionAttribute : Attribute;

internal static class ReportingFixture
{
    [ReportingTest]
    internal static void TestEntry()
    {
        Helper();
        Shared();
        CycleA();
        SharedCycleA();
        ReflectionEntry();
        HiddenHelper();
        ReportingFixtureExclusiveContainer.Run();
        ReportingFixtureHiddenContainer.Run();
    }

#pragma warning disable CS0028 // A graph root fixture, not this test assembly's entry point.
    [ReportingTest]
    internal static void Main(int value)
    {
        Shared();
        ProductionOnly();
        SharedCycleA();
    }
#pragma warning restore CS0028

    internal static void Helper() => Leaf();
    internal static void Leaf() { }
    internal static void Shared() { }
    internal static void ProductionOnly() { }
    internal static void ApiDependency() { }
    internal static void CycleA() => CycleB();
    internal static void CycleB() => CycleA();
    internal static void SharedCycleA() => SharedCycleB();
    internal static void SharedCycleB() => SharedCycleA();

    [ReportingReflection]
    internal static void ReflectionEntry() => ReflectionLeaf();

    internal static void ReflectionLeaf() { }

    [CompilerGenerated]
    internal static void HiddenHelper() => HiddenLeaf();

    internal static void HiddenLeaf() { }
}

internal static class ReportingFixtureExclusiveContainer
{
    internal static void Run() { }
}

[CompilerGenerated]
internal static class ReportingFixtureHiddenContainer
{
    internal static void Run() { }
}

public static class ReportingApiFixture
{
    [ReportingTest]
    public static void TestEntry() => ReportingFixture.ApiDependency();
}
