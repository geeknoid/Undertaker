using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph.Tests;

public class GraphCorrectnessTests
{
    private static string FixturePath => typeof(GraphCorrectnessTests).Assembly.Location;
    private static string FixtureAssemblyName => typeof(GraphCorrectnessTests).Assembly.GetName().Name!;

    [Fact]
    public void RootAssemblyPreservesPublicOrdinaryMethodAndDependencies()
    {
        var graph = new AssemblyGraph();
        graph.RecordRootAssembly(FixtureAssemblyName);
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var api = Find(graph, loaded, typeof(PublicApi), nameof(PublicApi.Run));
        var dependency = Find(graph, loaded, typeof(PublicApi), "Dependency");

        graph.Done(_ => { });

        Assert.True(api.Root);
        Assert.True(api.NonTestRoot);
        Assert.True(api.Marked);
        Assert.True(dependency.Marked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllPoliciesRejectLateRegistration(bool merged)
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        if (merged)
        {
            graph.MergeAssembly(loaded);
        }
        else
        {
            graph.GetAssembly("AlreadyReferenced");
        }

        Assert.Throws<InvalidOperationException>(() => graph.RecordRootAssembly("Root"));
        Assert.Throws<InvalidOperationException>(() => graph.RecordReflectionSymbol("Root", "Symbol"));
        Assert.Throws<InvalidOperationException>(() => graph.RecordReflectionMarkerAttribute("Marker"));
        Assert.Throws<InvalidOperationException>(() => graph.RecordTestMethodAttribute("Test"));
    }

    [Fact]
    public void FailedFinalizationCannotReturnAPartialReporterOrAcceptMoreWork()
    {
        var graph = new AssemblyGraph();
        var error = new InvalidOperationException("callback failure");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => graph.Done(_ => throw error)));
        Assert.Throws<InvalidOperationException>(() => graph.Done(_ => { }));
        Assert.Throws<InvalidOperationException>(() => graph.RecordRootAssembly("Root"));
        using var loaded = new LoadedAssembly(FixturePath);
        Assert.Throws<InvalidOperationException>(() => graph.MergeAssembly(loaded));
        Assert.Throws<InvalidOperationException>(() => graph.AssemblyLoaded(FixturePath));
    }

    [Fact]
    public void SuccessfulFinalizationIsIdempotentAndRejectsReentrantMutation()
    {
        var graph = new AssemblyGraph();
        var calls = 0;
        var first = graph.Done(_ =>
        {
            calls++;
            Assert.Throws<InvalidOperationException>(() => graph.Done(_ => { }));
            Assert.Throws<InvalidOperationException>(() => graph.RecordRootAssembly("Root"));
        });
        var messages = calls;
        var second = graph.Done(_ => calls++);
        Assert.Equal(messages, calls);
        Assert.Equal(first.CreateDependencyDiagram(), second.CreateDependencyDiagram());
    }

    [Fact]
    public void AssemblyLoadedUsesMetadataIdentityAndVersionInsteadOfBasename()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var directory = Path.Combine(Environment.CurrentDirectory, "graph-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var renamed = Path.Combine(directory, "NotTheAssemblyName.dll");
            File.Copy(FixturePath, renamed);
            Assert.True(graph.AssemblyLoaded(renamed));
            var other = Path.Combine(directory, FixtureAssemblyName + ".dll");
            File.Copy(typeof(AssemblyGraph).Assembly.Location, other);
            Assert.False(graph.AssemblyLoaded(other));
            var duplicate = Assert.Single(Assert.Single(graph.Done(_ => { }).CollectDuplicateAssemblies()).Duplicates);
            Assert.Equal(renamed, duplicate.Path);
            Assert.Equal(typeof(GraphCorrectnessTests).Assembly.GetName().Version, duplicate.Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedMergePermanentlyPoisonsGraph()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "graph-invalid-il-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = File.ReadAllBytes(FixturePath);
            using (var pe = new PEReader(new MemoryStream(bytes)))
            {
                var metadata = pe.GetMetadataReader();
                var handle = metadata.MethodDefinitions.Single(h => metadata.GetString(metadata.GetMethodDefinition(h).Name) == nameof(BrokenBody));
                var definition = metadata.GetMethodDefinition(handle);
                var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
                var section = pe.PEHeaders.SectionHeaders.Single(s => definition.RelativeVirtualAddress >= s.VirtualAddress
                    && definition.RelativeVirtualAddress < s.VirtualAddress + s.VirtualSize);
                var bodyOffset = definition.RelativeVirtualAddress - section.VirtualAddress + section.PointerToRawData;
                var headerSize = (bytes[bodyOffset] & 3) == 2 ? 1 : (bytes[bodyOffset + 1] >> 4) * 4;
                bytes[bodyOffset + headerSize + body.GetILBytes()!.Length - 1] = 0x28;
            }
            var path = Path.Combine(directory, "Invalid.dll");
            File.WriteAllBytes(path, bytes);
            var graph = new AssemblyGraph();
            using var invalid = new LoadedAssembly(path);
            Assert.Throws<BadImageFormatException>(() => graph.MergeAssembly(invalid));
            using var valid = new LoadedAssembly(FixturePath);
            Assert.Throws<InvalidOperationException>(() => graph.MergeAssembly(valid));
            Assert.Throws<InvalidOperationException>(() => graph.Done(_ => { }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DispatchUsesSubstitutedMetadataAndDistinguishesExplicitImplementations()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var generic = Find(graph, loaded, typeof(IGeneric<>), "Run");
        var baseMethod = Find(graph, loaded, typeof(GenericBase<>), "Run");
        var implementation = Find(graph, loaded, typeof(ÉImplementation), "Run", "System.Int32");
        var wrongOverload = Find(graph, loaded, typeof(ÉImplementation), "Run", "System.String");
        var second = Find(graph, loaded, typeof(ISecond), "Run");
        var firstExplicit = Find(graph, loaded, typeof(ExplicitImplementation), "Undertaker.Graph.Tests.GraphCorrectnessTests.IFirst.Run");
        var secondExplicit = Find(graph, loaded, typeof(ExplicitImplementation), "Undertaker.Graph.Tests.GraphCorrectnessTests.ISecond.Run");
        generic.SetReflectionTarget();
        baseMethod.SetReflectionTarget();
        second.SetReflectionTarget();

        graph.Done(_ => { });

        Assert.True(implementation.Marked);
        Assert.False(wrongOverload.Marked);
        Assert.True(secondExplicit.Marked);
        Assert.False(firstExplicit.Marked);
        Assert.Contains(implementation.Id, generic.ReferencedSymbols);
        Assert.Contains(implementation.Id, baseMethod.ReferencedSymbols);
        Assert.DoesNotContain(firstExplicit.Id, second.ReferencedSymbols);
    }

    [Fact]
    public void ExternalResolvedMembersHaveMembershipAndDispatchLinks()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var dispose = Find(graph, loaded, typeof(DisposableFixture), nameof(IDisposable.Dispose));
        var type = (TypeSymbol)graph.SymbolTable.GetSymbol(dispose.DeclaringType!.Value);
        var contract = type.InterfacesImplemented.Select(graph.SymbolTable.GetSymbol)
            .Cast<TypeSymbol>().Single(t => t.Name.ToString() == "System.IDisposable");
        var contractMethod = contract.Members.Select(graph.SymbolTable.GetSymbol).Single(m => m.Name.ToString().EndsWith(".Dispose()", StringComparison.Ordinal));
        Find(graph, loaded, typeof(DisposableFixture), "RuntimeCall").SetReflectionTarget();

        graph.Done(_ => { });

        Assert.False(contract.Assembly.Loaded);
        Assert.Contains(contractMethod.Id, contract.Members);
        Assert.Contains(dispose.Id, contractMethod.ReferencedSymbols);
        Assert.True(contractMethod.Marked);
        Assert.True(dispose.Marked);
    }

    [Fact]
    public void InterfaceMappingKeepsInheritedSlotsUnlessTheInterfaceIsReimplemented()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var contract = Find(graph, loaded, typeof(IFirst), "Run");
        var inherited = Find(graph, loaded, typeof(InterfaceParent), "Run");
        var hidden = Find(graph, loaded, typeof(InterfaceInherited), "Run");
        var reimplemented = Find(graph, loaded, typeof(InterfaceReimplementation), "Run");
        contract.SetReflectionTarget();

        graph.Done(_ => { });

        Assert.Contains(inherited.Id, contract.ReferencedSymbols);
        Assert.Contains(reimplemented.Id, contract.ReferencedSymbols);
        Assert.DoesNotContain(hidden.Id, contract.ReferencedSymbols);
        Assert.False(hidden.Marked);
    }

    [Fact]
    public void DerivedPublicReimplementationPrecedesBaseExplicitImplementation()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var contract = Find(graph, loaded, typeof(IFirst), "Run");
        var implementation = Find(graph, loaded, typeof(DerivedPublicImplementation), "Run");
        var dependency = Find(graph, loaded, typeof(DerivedPublicImplementation), "Dependency");
        contract.SetReflectionTarget();

        graph.Done(_ => { });

        Assert.Contains(implementation.Id, contract.ReferencedSymbols);
        Assert.True(implementation.Marked);
        Assert.True(dependency.Marked);
    }

    [Fact]
    public void RuntimeFallbackUsesDerivedImplementationAndRejectsGenericOverloads()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath, new UnresolvedAssemblyResolver());
        graph.MergeAssembly(loaded);
        var derived = Find(graph, loaded, typeof(DerivedDisposableImplementation), "Dispose");
        var hidden = Find(graph, loaded, typeof(HiddenDisposableImplementation), "Dispose");
        var dependency = Find(graph, loaded, typeof(DerivedDisposableImplementation), "Dependency");
        var type = FindType(graph, loaded, typeof(DisposableWithGenericOverload));
        var contract = type.InterfacesImplemented.Select(graph.SymbolTable.GetSymbol).Cast<TypeSymbol>()
            .Single(symbol => symbol.Name == "System.IDisposable");
        var declaration = Assert.Single(contract.Members.Select(graph.SymbolTable.GetSymbol),
            symbol => symbol.Name.ToString().EndsWith(".Dispose()", StringComparison.Ordinal));
        var definition = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t =>
            t.ReflectionName == typeof(DisposableWithGenericOverload).FullName);
        Assert.Empty(definition.DirectBaseTypes.Single(baseType =>
            baseType.ReflectionName == "System.IDisposable").GetMethods());
        var methods = definition.Methods.Where(method => method.Name == "Dispose").ToArray();
        Assert.Equal([1, 0], methods.Select(method => method.TypeParameters.Count));
        var generic = graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, methods[0]);
        var nongeneric = graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, methods[1]);
        Find(graph, loaded, typeof(DisposableFixture), "RuntimeCall").SetReflectionTarget();

        graph.Done(_ => { });

        Assert.Contains(derived.Id, declaration.ReferencedSymbols);
        Assert.Contains(nongeneric.Id, declaration.ReferencedSymbols);
        Assert.DoesNotContain(generic.Id, declaration.ReferencedSymbols);
        Assert.DoesNotContain(hidden.Id, declaration.ReferencedSymbols);
        Assert.True(derived.Marked);
        Assert.True(dependency.Marked);
        Assert.True(nongeneric.Marked);
        Assert.False(generic.Marked);
        Assert.False(hidden.Marked);
    }

    [Fact]
    public void GenericMethodArityIsPartOfCanonicalIdentity()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var type = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == typeof(GenericMethods).FullName);
        var methods = type.Methods.Where(m => m.Name == "Overload")
            .Select(m => graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, m)).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.NotEqual(methods[0].Id, methods[1].Id);
        Assert.Contains("``1()", methods[0].Name.ToString(), StringComparison.Ordinal);
        Assert.Contains("``2()", methods[1].Name.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReflectionPropertiesAndEventsKeepAllAccessorsAndBodiesAlive()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var type = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == typeof(AccessorFixture).FullName);
        var assembly = graph.GetAssembly(FixtureAssemblyName);
        assembly.GetSymbol(graph, type.Properties.Single()).SetReflectionTarget();
        assembly.GetSymbol(graph, type.Events.Single()).SetReflectionTarget();

        graph.Done(_ => { });

        var accessors = type.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers).ToArray();
        Assert.Equal(4, accessors.Length);
        foreach (var accessor in accessors)
        {
            Assert.True(assembly.GetSymbol(graph, accessor).Marked);
        }
        Assert.True(Find(graph, loaded, typeof(AccessorFixture), "Dependency").Marked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAccessorUseDoesNotReviveUnusedCounterparts(bool throughInterface)
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        Find(graph, loaded, typeof(AccessorFixture), throughInterface ? "UseInterface" : "UseDirect").SetReflectionTarget();

        graph.Done(_ => { });

        Assert.True(Find(graph, loaded, typeof(AccessorFixture), "set_Value").Marked);
        Assert.True(Find(graph, loaded, typeof(AccessorFixture), "add_Event").Marked);
        Assert.False(Find(graph, loaded, typeof(AccessorFixture), "get_Value").Marked);
        Assert.False(Find(graph, loaded, typeof(AccessorFixture), "remove_Event").Marked);
        if (throughInterface)
        {
            Assert.False(Find(graph, loaded, typeof(IAccessorContract), "get_Value").Marked);
            Assert.False(Find(graph, loaded, typeof(IAccessorContract), "remove_Event").Marked);
        }
    }

    [Fact]
    public void OverloadedIndexersHaveSeparateIdentityAndDependencies()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        var type = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == typeof(IndexerFixture).FullName);
        var properties = type.Properties.Select(p => graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, p)).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.NotEqual(properties[0].Id, properties[1].Id);
        properties[0].SetReflectionTarget();
        graph.Done(_ => { });
        Assert.True(properties[0].Marked);
        Assert.False(properties[1].Marked);
    }

    [Fact]
    public void AttributesKeepConstructorNamedMembersAndNestedTypeValues()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        Find(graph, loaded, typeof(AttributeFixture), "Entry").SetReflectionTarget();
        graph.Done(_ => { });
        foreach (var type in new[] { typeof(FixedType), typeof(NamedType), typeof(ArrayType), typeof(BoxedArrayType) })
        {
            Assert.True(FindType(graph, loaded, type).Marked);
        }
        var attributeType = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == typeof(DependencyAttribute).FullName);
        foreach (var member in attributeType.Members.Where(m => m.Name is ".ctor" or "Named" or "Types" or "Boxed"))
        {
            Assert.True(graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, member).Marked);
        }
        Assert.True(Find(graph, loaded, typeof(DependencyAttribute), "set_Named").Marked);
        Assert.True(Find(graph, loaded, typeof(DependencyAttribute), "set_Types").Marked);
        Assert.False(Find(graph, loaded, typeof(DependencyAttribute), "get_Named").Marked);
        Assert.False(Find(graph, loaded, typeof(DependencyAttribute), "get_Types").Marked);
    }

    [Fact]
    public void ConstructedTypeTokensAndElementWrappersKeepApplicationTypes()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        graph.MergeAssembly(loaded);
        Find(graph, loaded, typeof(SignatureFixture), "Token").SetReflectionTarget();
        Find(graph, loaded, typeof(SignatureFixture), "Array").SetReflectionTarget();
        Find(graph, loaded, typeof(SignatureFixture), "ByRef").SetReflectionTarget();
        graph.Done(_ => { });
        Assert.True(FindType(graph, loaded, typeof(TokenType)).Marked);
        Assert.True(FindType(graph, loaded, typeof(GenericToken<>)).Marked);
        Assert.True(FindType(graph, loaded, typeof(ArrayElement)).Marked);
        Assert.True(FindType(graph, loaded, typeof(RefElement)).Marked);
    }

    [Fact]
    public void EnumReferencedBeforeItsDefinitionDoesNotExposeSyntheticConstructor()
    {
        var graph = new AssemblyGraph();
        using var loaded = new LoadedAssembly(FixturePath);
        var definition = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == typeof(EnumFixture).FullName);
        var constructor = Assert.Single(definition.GetConstructors());
        Assert.True(constructor.MetadataToken.IsNil);

        graph.MergeAssembly(loaded);
        var symbol = FindType(graph, loaded, typeof(EnumFixture));
        var assembly = graph.GetAssembly(FixtureAssemblyName);

        Assert.Empty(symbol.Members);
        Assert.Null(assembly.FindSymbol(graph, definition.ReflectionName + "..ctor()", SymbolKind.Method));
        graph.Done(_ => { });
        Assert.Null(assembly.FindSymbol(graph, definition.ReflectionName + "..ctor()", SymbolKind.Method));
    }

    [Fact]
    public void UnhomedReplacementTransfersIncomingEdgesBeforeDependencySnapshots()
    {
        var graph = new AssemblyGraph();
        var source = graph.GetAssembly("Source");
        var target = graph.GetAssembly("Target");
        source.Loaded = target.Loaded = true;
        var caller = source.GetSymbol(graph, "Source.Call()", SymbolKind.Method);
        var placeholder = graph.UnhomedAssembly.GetSymbol(graph, "Target.Run()", SymbolKind.Method);
        var replacement = target.GetSymbol(graph, "Target.Run()", SymbolKind.Method);
        caller.RecordReferencedSymbol(placeholder);

        var reporter = graph.Done(_ => { });

        Assert.Contains(replacement.Id, caller.ReferencedSymbols);
        Assert.Contains(caller.Id, replacement.Referencers);
        Assert.DoesNotContain(placeholder.Id, caller.ReferencedSymbols);
        Assert.Same(replacement, graph.SymbolTable.GetSymbol(placeholder.Id));
        Assert.Equal([target], source.RealDependencies);
        Assert.Equal(["Source"], reporter.CreateAssemblyLayerCake()[0]);
        Assert.Equal(["Target"], reporter.CreateAssemblyLayerCake()[1]);
        Assert.Contains("Source --> Target", reporter.CreateDependencyDiagram(), StringComparison.Ordinal);
    }

    private static Symbol Find(AssemblyGraph graph, LoadedAssembly loaded, Type type, string methodName, string? parameterType = null)
    {
        var definition = loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == type.FullName);
        var method = definition.Methods.Concat(definition.GetAccessors(options: GetMemberOptions.IgnoreInheritedMembers)).Single(m => m.Name == methodName
            && (parameterType == null || m.Parameters[0].Type.ReflectionName == parameterType));
        return graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph, method);
    }

    private static TypeSymbol FindType(AssemblyGraph graph, LoadedAssembly loaded, Type type) =>
        (TypeSymbol)graph.GetAssembly(FixtureAssemblyName).GetSymbol(graph,
            loaded.Decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.ReflectionName == type.FullName));

    private static void BrokenBody() { }

    private sealed class UnresolvedAssemblyResolver : IAssemblyResolver
    {
        public MetadataFile? Resolve(IAssemblyReference reference) => null;
        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => null;
        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) =>
            Task.FromResult<MetadataFile?>(null);
        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) =>
            Task.FromResult<MetadataFile?>(null);
    }

    public static class PublicApi
    {
        public static void Run() => Dependency();
        private static void Dependency() { }
    }

#pragma warning disable CA1822 // Instance metadata signatures are the regression fixtures.
    private interface IGeneric<T> { void Run(T value); }
    private class GenericBase<T> { public virtual void Run(T value) { } }
    private sealed class ÉImplementation : GenericBase<int>, IGeneric<int>
    {
        public override void Run(int value) { }
        public void Run(string value) { }
    }
    private interface IFirst { void Run(); }
    private interface ISecond { void Run(); }
    private sealed class ExplicitImplementation : IFirst, ISecond
    {
        void IFirst.Run() { }
        void ISecond.Run() { }
    }
    private class InterfaceParent : IFirst { public void Run() { } }
    private sealed class InterfaceInherited : InterfaceParent { public new void Run() { } }
    private sealed class InterfaceReimplementation : InterfaceParent, IFirst { public new void Run() { } }
    private class BaseExplicitImplementation : IFirst { void IFirst.Run() { } }
    private sealed class DerivedPublicImplementation : BaseExplicitImplementation, IFirst
    {
        public void Run() => Dependency();
        private static void Dependency() { }
    }
    private class BaseExplicitDisposable : IDisposable { void IDisposable.Dispose() { } }
    private sealed class DerivedDisposableImplementation : BaseExplicitDisposable, IDisposable
    {
        public void Dispose() => Dependency();
        private static void Dependency() { }
    }
    private sealed class HiddenDisposableImplementation : BaseExplicitDisposable
    {
        public void Dispose() { }
    }
    private sealed class DisposableWithGenericOverload : IDisposable
    {
        public void Dispose<T>() { }
        public void Dispose() { }
    }
    private static class GenericMethods
    {
        public static void Overload<T>() { }
        public static void Overload<T, TOther>() { }
    }
    private sealed class DisposableFixture : IDisposable
    {
        public void Dispose() { }
        public static void RuntimeCall(IDisposable value) => value.Dispose();
    }
    private interface IAccessorContract
    {
        int Value { get; set; }
        event Action Event;
    }
    private sealed class AccessorFixture : IAccessorContract
    {
        public int Value { get { Dependency(); return 0; } set => Dependency(); }
        public event Action Event { add => Dependency(); remove => Dependency(); }
        public static void UseDirect(AccessorFixture target)
        {
            target.Value = 1;
            target.Event += Dependency;
        }
        public static void UseInterface(IAccessorContract target)
        {
            target.Value = 1;
            target.Event += Dependency;
        }
        private static void Dependency() { }
    }
    private sealed class IndexerFixture
    {
        public int this[int index] => 0;
        public int this[string index] => 1;
    }
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class DependencyAttribute(Type type) : Attribute
    {
        public Type Named { get; set; } = type;
        public Type[] Types { get; set; } = [];
        public object? Boxed;
    }
    private static class AttributeFixture
    {
        [Dependency(typeof(FixedType), Named = typeof(NamedType), Types = [typeof(ArrayType)], Boxed = new object[] { typeof(BoxedArrayType) })]
        public static void Entry() { }
    }
    private sealed class FixedType;
    private sealed class NamedType;
    private sealed class ArrayType;
    private sealed class BoxedArrayType;
    private sealed class TokenType;
    private sealed class GenericToken<T>;
    private sealed class ArrayElement;
    private sealed class RefElement;
    private static class SignatureFixture
    {
        public static Type Token() => typeof(GenericToken<TokenType>);
        public static void Array(ArrayElement[] value) { }
        public static void ByRef(ref RefElement value) { }
        public static EnumFixture Enum(EnumFixture value) => value;
    }
    private enum EnumFixture { Value }
#pragma warning restore CA1822
}
